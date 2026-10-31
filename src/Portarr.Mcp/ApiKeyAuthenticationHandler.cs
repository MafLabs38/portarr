using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;

namespace Portarr.Mcp;

/// <summary>
/// Authenticates an MCP client by the shared key of <see cref="McpOptions.ApiKey"/>, sent as <c>X-Api-Key</c> or
/// <c>Authorization: Bearer</c> (V5, D-70). Keys are compared through their SHA-256 hashes in constant time, so
/// neither the content nor the length of the key leaks through timing. Replaced by scoped API keys in C2.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PortarrMcpApiKey";
    public const string HeaderName = ApiKeys.HeaderName;

    private readonly IOptionsMonitor<McpOptions> _mcpOptions;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<McpOptions> mcpOptions)
        : base(options, logger, encoder)
    {
        _mcpOptions = mcpOptions;
    }

    /// <summary>The key a request carries: <c>X-Api-Key</c> first, else a bearer token.</summary>
    public static string? ReadKey(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        return ApiKeys.Read(request.Headers[HeaderName].ToString(), request.Headers.Authorization.ToString());
    }

    public static bool Matches(string? presented, string? expected) => ApiKeys.Matches(presented, expected);

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ReadKey(Request);
        if (presented is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Matches(presented, _mcpOptions.CurrentValue.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "mcp-client")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"portarr-mcp\"";
        return Task.CompletedTask;
    }
}
