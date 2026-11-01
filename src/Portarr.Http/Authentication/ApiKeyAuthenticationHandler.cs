using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;
using Portarr.Core.Configuration;

namespace Portarr.Http.Authentication;

/// <summary>
/// Authenticates a request that carries a key, as <c>X-Api-Key</c> or <c>Authorization: Bearer</c> (V7, D-97): a per-user
/// <c>ptr_</c> key (the owner's permissions, narrowed by the key's scope), one of the deprecated environment keys (the
/// <c>system</c> principal, with the permissions of its source), or, on <c>/mcp</c> only, the chat runner's <c>ptc_</c> token. Environment keys are compared
/// through their SHA-256 hashes in constant time, so neither the content nor the length of a key leaks through timing.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";

    private readonly IApiKeyService _apiKeys;
    private readonly IChatAccess _chat;
    private readonly IEnumerable<ISystemApiKeySource> _systemKeys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeys,
        IChatAccess chat,
        IEnumerable<ISystemApiKeySource> systemKeys)
        : base(options, logger, encoder)
    {
        _apiKeys = apiKeys;
        _chat = chat;
        _systemKeys = systemKeys;
    }

    /// <summary>The key a request carries: <c>X-Api-Key</c> first, else a bearer token; null without either.</summary>
    public static string? ReadKey(HttpRequest request) => ApiKeys.Read(request.Headers[ApiKeys.HeaderName].ToString(), request.Headers.Authorization.ToString());

    /// <summary>Whether the request carries a key at all (a cookie is not looked at then).</summary>
    public static bool HasKey(HttpRequest request) => ReadKey(request) is not null;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ReadKey(Request);
        if (presented is null)
        {
            return AuthenticateResult.NoResult();
        }

        // Every environment key is compared, whichever matches, so the time does not tell which one is set.
        HashSet<string>? systemPermissions = null;
        foreach (var source in _systemKeys.Where(k => ApiKeys.Matches(presented, k.Key)))
        {
            (systemPermissions ??= new HashSet<string>(StringComparer.Ordinal)).UnionWith(source.Permissions);
        }

        if (systemPermissions is not null)
        {
            return Success(PortarrClaims.CreatePrincipal(AccountIdentity.ForSystem(systemPermissions), AuthMethods.System, SchemeName));
        }

        // Each service recognises its own prefix and answers null for any other key, so what the client sends only ever
        // selects the lookup, never skips one.
        if (await _apiKeys.AuthenticateAsync(presented, Context.RequestAborted) is { } owner)
        {
            return Success(PortarrClaims.CreatePrincipal(owner, AuthMethods.ApiKey, SchemeName));
        }

        // The chat token opens the MCP endpoint and nothing else.
        if (Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase)
            && await _chat.AuthenticateTokenAsync(presented, Context.RequestAborted) is { } chat)
        {
            return Success(PortarrClaims.CreatePrincipal(chat.Identity, AuthMethods.ChatToken, SchemeName, chat.ConversationId));
        }

        return AuthenticateResult.Fail("Invalid API key.");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"portarr\"";
        return Task.CompletedTask;
    }

    private static AuthenticateResult Success(System.Security.Claims.ClaimsPrincipal principal) =>
        AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
}
