using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;

namespace Portarr.Mcp.Chat;

/// <summary>Who is calling the chat: the user account (null for an environment key), their name, and whether they manage users.</summary>
public sealed record ChatCaller(int? UserId, string Name, bool IsUserManager);

/// <summary>
/// The rules of the per-user chat (V7, D-98) that sit between the controller and the runner: a conversation belongs to the
/// user who created it; the list shows only the caller's own conversations (and, to a user manager, those without an
/// owner, which they look after); a conversation is registered right after the runner creates it and removed from the
/// runner again if that fails; deleting forgets the ownership once the runner no longer has it; each message carries a
/// short-lived token so the agent's MCP calls run as the user. The controller maps the answers to HTTP.
/// </summary>
public sealed partial class ChatService
{
    private readonly AgentRunnerClient _runner;
    private readonly IChatAccess _access;
    private readonly AgentOptions _options;
    private readonly ILogger<ChatService> _logger;

    public ChatService(AgentRunnerClient runner, IChatAccess access, IOptions<AgentOptions> options, ILogger<ChatService> logger)
    {
        _runner = runner;
        _access = access;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")]
    private static partial Regex IdPattern();

    /// <summary>
    /// Whether the conversation exists for this caller: a well-formed id and the caller's own conversation (or an ownerless
    /// one, for a user manager). Everything else is "no", never "forbidden": another user's conversation is not revealed.
    /// </summary>
    public async Task<bool> CanAccessAsync(string id, ChatCaller caller, CancellationToken cancellationToken) =>
        IdPattern().IsMatch(id) && await _access.CanAccessAsync(id, caller.UserId, caller.IsUserManager, cancellationToken);

    /// <summary>The runner's conversations that the caller may open, each with its owner's name.</summary>
    public async Task<AgentRunnerResponse> ListAsync(ChatCaller caller, CancellationToken cancellationToken)
    {
        var response = await _runner.SendAsync(HttpMethod.Get, "v1/conversations", null, cancellationToken);
        if (response is not { StatusCode: StatusCodes.Status200OK, Json: { } json } || JsonNode.Parse(json) is not JsonArray all)
        {
            return response;
        }

        var owners = await _access.ListAsync(cancellationToken);
        var visible = new JsonArray();
        foreach (var item in all.OfType<JsonObject>().ToList())
        {
            owners.TryGetValue(item["id"]?.GetValue<string>() ?? string.Empty, out var owner);
            var own = owner?.UserId is { } ownerId && ownerId == caller.UserId;
            var ownerless = owner?.UserId is null && caller.IsUserManager;
            if (own || ownerless)
            {
                all.Remove(item);
                item["owner"] = owner?.OwnerName;
                visible.Add(item);
            }
        }

        return new AgentRunnerResponse(StatusCodes.Status200OK, visible.ToJsonString());
    }

    /// <summary>Starts a conversation and records its owner; when that fails, the runner's conversation is removed so none is left ownerless.</summary>
    public async Task<AgentRunnerResponse> CreateAsync(object request, ChatCaller caller, CancellationToken cancellationToken)
    {
        var response = await _runner.SendAsync(HttpMethod.Post, "v1/conversations", request, cancellationToken);
        if (response is not { StatusCode: StatusCodes.Status201Created, Json: { } json }
            || JsonNode.Parse(json)?["id"]?.GetValue<string>() is not { } id
            || caller.UserId is not { } userId)
        {
            return response;
        }

        try
        {
            await _access.RegisterAsync(id, userId, caller.Name, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRegisterFailed(exception.GetType().Name);
            await TryRemoveAsync(id);
            throw;
        }

        return response;
    }

    /// <summary>Deletes the runner's conversation and forgets its owner once the runner no longer has it.</summary>
    public async Task<AgentRunnerResponse> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var response = await _runner.SendAsync(HttpMethod.Delete, $"v1/conversations/{id}", null, cancellationToken);
        if (response.StatusCode is < 300 or StatusCodes.Status404NotFound)
        {
            await _access.ForgetAsync(id, cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Sends the message with a token tied to the user and the conversation, instead of a shared key. A caller that is not
    /// a user account (an environment key) sends none: the runner falls back to its own key, when it has one.
    /// </summary>
    public async Task<AgentRunnerResponse> SendAsync(string id, ChatMessageRequest request, ChatCaller caller, CancellationToken cancellationToken)
    {
        var token = caller.UserId is { } userId && _options.Enabled ? await _access.MintTokenAsync(userId, id, cancellationToken) : null;
        return await _runner.SendAsync(HttpMethod.Post, $"v1/conversations/{id}/messages", new { request.Text, request.Model, McpToken = token }, cancellationToken);
    }

    /// <summary>Best effort: the conversation was created but could not be registered, and nobody must be left able to see it.</summary>
    private async Task TryRemoveAsync(string id)
    {
        try
        {
            await _runner.SendAsync(HttpMethod.Delete, $"v1/conversations/{id}", null, CancellationToken.None);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            LogCleanupFailed(exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A chat conversation was created but its owner could not be recorded ({Error}); removing it from the runner")]
    private partial void LogRegisterFailed(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The conversation that could not be recorded could not be removed from the runner either ({Error})")]
    private partial void LogCleanupFailed(string error);
}
