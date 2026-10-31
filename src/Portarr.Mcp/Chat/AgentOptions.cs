using Microsoft.Extensions.Options;

namespace Portarr.Mcp.Chat;

/// <summary>
/// The built-in chat (D-89), section <c>Agent</c>: <c>PORTARR__AGENT__URL</c> (the optional <c>portarr-agent</c>
/// runner, e.g. <c>http://portarr-agent:8488</c>) and <c>PORTARR__AGENT__SECRET</c> (shared with the runner). Without a
/// URL the chat is off: its endpoints answer 404 and the panel's icon is hidden.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public const int MinimumSecretLength = Portarr.Core.Configuration.ApiKeys.MinimumLength;

    public string? Url { get; set; }

    /// <summary>Sent to the runner as a bearer token. Never returned by the API, never logged.</summary>
    public string? Secret { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Url);
}

/// <summary>Fails startup on a malformed runner URL or a weak secret.</summary>
public sealed class AgentOptionsValidator : IValidateOptions<AgentOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add("Agent:Url must be an http(s) address without credentials (PORTARR__AGENT__URL, e.g. http://portarr-agent:8488).");
        }

        if (options.Secret is null || options.Secret.Trim().Length < AgentOptions.MinimumSecretLength)
        {
            errors.Add($"Agent:Secret must be at least {AgentOptions.MinimumSecretLength} characters when Agent:Url is set (PORTARR__AGENT__SECRET, e.g. openssl rand -hex 32).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
