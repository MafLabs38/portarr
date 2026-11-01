using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;
using Portarr.Http;
using Portarr.Http.Authentication;

namespace Portarr.Mcp.Chat;

/// <summary>Whether the chat is on and which agents the runner offers; <c>Reachable</c> false when it does not answer.</summary>
public sealed record ChatStatusResource(bool Enabled, bool Reachable, JsonElement? Agents);

/// <summary>A new conversation: the agent (<c>claude-code</c>) and its model (<c>sonnet</c>, <c>opus</c>…), both optional.</summary>
public sealed record ChatConversationRequest(string? Agent, string? Model, string? Title);

/// <summary>The operator's message, typed or pasted (multi-line), and optionally another model for this turn on.</summary>
public sealed record ChatMessageRequest(string Text, string? Model);

/// <summary>The operator's answer to a write the agent proposes.</summary>
public sealed record ChatPermissionRequest([property: System.Text.Json.Serialization.JsonRequired] bool Approve);

/// <summary>An empty JSON body, so every write to the chat is a JSON request (never a cross-site form post).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Its type is the contract: a body bound to it must be JSON, which a cross-site form post cannot send.")]
public sealed record ChatCancelRequest;

/// <summary>
/// The built-in chat (D-89): a proxy to the optional <c>portarr-agent</c> runner, which runs an agent CLI whose only
/// tool is Portarr's own MCP server (D-87, D-88). Off (404) without <c>PORTARR__AGENT__URL</c>. Per user since accounts
/// (V7, D-98): it needs <c>chat.use</c>, a conversation belongs to the user who created it (list, read, send, cancel,
/// events and delete are the owner's; user managers also see, and look after, those without an owner),
/// approving a change needs <c>catalog.write</c>, and each message carries a short-lived token so the agent's MCP calls
/// run as the user.
/// </summary>
[ApiController]
[Authorize(Policy = Permissions.ChatUse)]
[Route("api/v1/assistant/chat")]
public sealed partial class ChatController : ControllerBase
{
    public const int MaxMessageLength = 20_000;

    private readonly AgentRunnerClient _runner;
    private readonly ChatService _chat;
    private readonly AgentOptions _options;
    private readonly ILogger<ChatController> _logger;

    public ChatController(AgentRunnerClient runner, ChatService chat, IOptions<AgentOptions> options, ILogger<ChatController> logger)
    {
        _runner = runner;
        _chat = chat;
        _options = options.Value;
        _logger = logger;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,100}$")]
    private static partial Regex RequestIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$")]
    private static partial Regex NamePattern();

    /// <summary>Whether the chat is on, and the agents and models the runner offers.</summary>
    [HttpGet]
    [EndpointSummary("Whether the built-in chat is on, and the agents and models it offers.")]
    public async Task<ChatStatusResource> GetAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new ChatStatusResource(false, false, null);
        }

        try
        {
            var response = await _runner.SendAsync(HttpMethod.Get, "v1/agents", null, cancellationToken);
            return response is { StatusCode: StatusCodes.Status200OK, Json: { } json }
                ? new ChatStatusResource(true, true, JsonDocument.Parse(json).RootElement.Clone())
                : new ChatStatusResource(true, false, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(exception.GetType().Name);
            return new ChatStatusResource(true, false, null);
        }
    }

    /// <summary>The conversations, latest first.</summary>
    [HttpGet("conversations")]
    [EndpointSummary("The chat's conversations, latest first.")]
    public Task<IActionResult> ListAsync(CancellationToken cancellationToken) => RunAsync(() => _chat.ListAsync(Caller(), cancellationToken), cancellationToken);

    [HttpPost("conversations")]
    [EndpointSummary("Starts a conversation with an agent and a model.")]
    public Task<IActionResult> CreateAsync([FromBody] ChatConversationRequest request, CancellationToken cancellationToken)
    {
        if (!ValidName(request.Agent) || !ValidName(request.Model) || request.Title?.Length > 200)
        {
            return Task.FromResult<IActionResult>(Invalid("agent and model are short lowercase names; title is 200 characters at most."));
        }

        return CreateConversationAsync(request, cancellationToken);
    }

    private Task<IActionResult> CreateConversationAsync(ChatConversationRequest request, CancellationToken cancellationToken) =>
        RunAsync(() => _chat.CreateAsync(request, Caller(), cancellationToken), cancellationToken);

    /// <summary>A conversation with its stored events (the history the panel shows).</summary>
    [HttpGet("conversations/{id}")]
    [EndpointSummary("A conversation and its history.")]
    public async Task<IActionResult> GetConversationAsync(string id, CancellationToken cancellationToken) =>
        await OwnsAsync(id, cancellationToken) ? await ForwardAsync(HttpMethod.Get, $"v1/conversations/{id}", null, cancellationToken) : NotFound();

    [HttpDelete("conversations/{id}")]
    [EndpointSummary("Deletes a conversation (its process stops).")]
    public async Task<IActionResult> DeleteAsync(string id, CancellationToken cancellationToken) =>
        await OwnsAsync(id, cancellationToken) ? await RunAsync(() => _chat.DeleteAsync(id, cancellationToken), cancellationToken) : NotFound();

    /// <summary>Sends the operator's message; the answer arrives on the event stream.</summary>
    /// <response code="409">A turn is running.</response>
    /// <response code="429">Too many conversations are running at once.</response>
    [HttpPost("conversations/{id}/messages")]
    [EndpointSummary("Sends a message; the answer arrives on the event stream.")]
    public async Task<IActionResult> SendAsync(string id, [FromBody] ChatMessageRequest request, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaxMessageLength || !ValidName(request.Model))
        {
            return Invalid($"text is 1 to {MaxMessageLength} characters; model a short lowercase name.");
        }

        return await RunAsync(() => _chat.SendAsync(id, request, Caller(), cancellationToken), cancellationToken);
    }

    /// <summary>Approves or rejects a catalog write the agent proposes.</summary>
    [HttpPost("conversations/{id}/permissions/{requestId}")]
    [EndpointSummary("Approves or rejects a change the agent proposes.")]
    public async Task<IActionResult> AnswerAsync(string id, string requestId, [FromBody] ChatPermissionRequest request, CancellationToken cancellationToken)
    {
        if (!RequestIdPattern().IsMatch(requestId) || !await OwnsAsync(id, cancellationToken))
        {
            return NotFound();
        }

        // Every card is a change to the catalog: approving one is writing it. Rejecting is always allowed.
        if (request.Approve && !User.HasPermission(Permissions.CatalogWrite))
        {
            return Problem(title: "Not allowed", detail: "Approving a change needs the catalog.write permission.", statusCode: StatusCodes.Status403Forbidden);
        }

        return await ForwardAsync(HttpMethod.Post, $"v1/conversations/{id}/permissions/{requestId}", request, cancellationToken);
    }

    /// <summary>Stops the running turn; pending writes are rejected.</summary>
    [HttpPost("conversations/{id}/cancel")]
    [EndpointSummary("Stops the running turn.")]
    public async Task<IActionResult> CancelAsync(string id, [FromBody] ChatCancelRequest request, CancellationToken cancellationToken) =>
        await OwnsAsync(id, cancellationToken) ? await ForwardAsync(HttpMethod.Post, $"v1/conversations/{id}/cancel", request, cancellationToken) : NotFound();

    /// <summary>The conversation's events as server-sent events: the stored history after <c>after</c>, then live ones.</summary>
    /// <remarks>Each event carries its sequence number as the SSE id: EventSource resumes from it after a reconnect.</remarks>
    [HttpGet("conversations/{id}/events")]
    [EnableRateLimiting(HttpServiceCollectionExtensions.LogStreamsPolicyName)]
    [Produces("text/event-stream")]
    [EndpointSummary("The conversation's events, as server-sent events.")]
    public async Task StreamAsync(string id, [FromQuery] int after, [FromHeader(Name = "Last-Event-ID")] string? lastEventId, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !await OwnsAsync(id, cancellationToken))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        try
        {
            using var upstream = await _runner.OpenStreamAsync(
                $"v1/conversations/{id}/events?after={Math.Max(0, after)}",
                int.TryParse(lastEventId, out var seq) && seq >= 0 ? seq.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                cancellationToken);
            if (!upstream.IsSuccessStatusCode)
            {
                Response.StatusCode = (int)upstream.StatusCode == StatusCodes.Status404NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status502BadGateway;
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            await using var body = await upstream.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[8192];
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The panel closed the stream.
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            LogUnreachable(exception.GetType().Name);
            if (!Response.HasStarted)
            {
                Response.StatusCode = StatusCodes.Status502BadGateway;
            }
        }
    }

    private static bool ValidName(string? value) => value is null || NamePattern().IsMatch(value);

    private Task<bool> OwnsAsync(string id, CancellationToken cancellationToken) => _chat.CanAccessAsync(id, Caller(), cancellationToken);

    private ChatCaller Caller() => new(User.UserId(), User.Identity?.Name ?? string.Empty, User.HasPermission(Permissions.UsersManage));

    private ObjectResult Invalid(string detail) => Problem(detail, statusCode: StatusCodes.Status400BadRequest);

    private Task<IActionResult> ForwardAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        RunAsync(() => _runner.SendAsync(method, path, body, cancellationToken), cancellationToken);

    /// <summary>Calls the runner and passes its answer through; a runner that does not answer, or refuses the secret, is a 502.</summary>
    private async Task<IActionResult> RunAsync(Func<Task<AgentRunnerResponse>> call, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return NotFound();
        }

        try
        {
            var response = await call();
            if (response.StatusCode == StatusCodes.Status401Unauthorized)
            {
                // The runner refused Portarr's secret: a configuration error, not the operator's.
                LogUnreachable("secret refused");
                return Problem("The agent runner refused Portarr's secret (PORTARR__AGENT__SECRET).", statusCode: StatusCodes.Status502BadGateway);
            }

            return response.Json is null
                ? StatusCode(response.StatusCode)
                : new ContentResult { StatusCode = response.StatusCode, Content = response.Json, ContentType = "application/json" };
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(exception.GetType().Name);
            return Problem("The agent runner did not answer.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The agent runner did not answer ({Error})")]
    private partial void LogUnreachable(string error);
}
