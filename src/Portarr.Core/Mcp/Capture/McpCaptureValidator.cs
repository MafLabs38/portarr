using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Core.Mcp.Capture;

/// <summary>
/// Checks what an assistant hands over (V9, D-116) before anything is stored: bounded sizes, known clients and scopes, an address or a
/// command that is what the catalog would accept, and above all **no secret value** (the same heuristic as the catalog, D-84). A value
/// that looks like a secret, or a literal under a name that suggests a credential, refuses the capture, naming the field and never the value.
/// </summary>
public static class McpCaptureValidator
{
    public const int MaxServers = 50;

    /// <summary>The errors of a capture; empty when it can be stored.</summary>
    public static IReadOnlyList<string> Validate(CaptureRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Workstation) || request.Workstation.Length > 64 || !IsId(request.Workstation))
        {
            errors.Add("workstation must be the id of the machine: lowercase letters, digits and dashes.");
        }

        if (McpClients.Find(request.Client) is null)
        {
            errors.Add($"client must be one of {string.Join(", ", McpClients.Ids)}.");
        }

        if (!McpScopes.All.Contains(request.Scope))
        {
            errors.Add($"scope must be one of {string.Join(", ", McpScopes.All)}.");
        }

        if (request.Servers.Count is 0 or > MaxServers)
        {
            errors.Add($"servers must hold between 1 and {MaxServers} servers.");
            return errors;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (server, index) in request.Servers.Select((s, i) => (s, i)))
        {
            ValidateServer(server, $"servers[{index}]", errors);
            if (!seen.Add(server.Name ?? string.Empty))
            {
                errors.Add($"servers[{index}]: the name is used twice in this capture.");
            }
        }

        return errors;
    }

    private static void ValidateServer(CapturedServer server, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(server.Name) || server.Name.Length > 100 || CatalogParser.McpControlCharacterPattern().IsMatch(server.Name))
        {
            errors.Add($"{key}: name must be a text of at most 100 characters.");
        }
        else if (SecretScanner.Find(server.Name) is not null)
        {
            errors.Add($"{key}: name looks like it contains a secret; send the name of the server, not a value.");
        }

        if (server.Transport == McpTransports.Http)
        {
            ValidateRemote(server, key, errors);
        }
        else if (server.Transport == McpTransports.Stdio)
        {
            ValidateLocal(server, key, errors);
        }
        else
        {
            errors.Add($"{key}: transport must be {string.Join(" or ", McpTransports.All)}.");
        }

        ValidateValues(server.Headers, $"{key}.headers", header: true, errors);
        ValidateValues(server.Env, $"{key}.env", header: false, errors);
    }

    private static void ValidateRemote(CapturedServer server, string key, List<string> errors)
    {
        if (server.Command is not null || server.Args.Count > 0 || server.Env.Count > 0)
        {
            errors.Add($"{key}: a remote server has an url and headers, not a command, arguments or an environment.");
        }

        if (!Uri.TryCreate(server.Url?.Trim(), UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https")
            || url.UserInfo.Length > 0
            || url.Fragment.Length > 0
            || server.Url!.Length > CatalogParser.MaxMcpTextLength)
        {
            errors.Add($"{key}: url must be an absolute http(s) address without credentials or fragment.");
        }
        else if (SecretScanner.Find(server.Url) is not null)
        {
            errors.Add($"{key}: url looks like it contains a secret; send it without the key (the key belongs in a header, by variable).");
        }
    }

    private static void ValidateLocal(CapturedServer server, string key, List<string> errors)
    {
        if (server.Url is not null || server.Headers.Count > 0)
        {
            errors.Add($"{key}: a local server has a command and an environment, not an url or headers.");
        }

        var command = server.Command?.Trim();
        if (string.IsNullOrEmpty(command)
            || command.Length > 260
            || CatalogParser.McpControlCharacterPattern().IsMatch(command)
            || CatalogParser.McpShellCharacterPattern().IsMatch(command))
        {
            errors.Add($"{key}: command must be a program (npx, uvx, a path), not a shell line: the arguments go in args.");
        }

        if (server.Args.Count > CatalogParser.MaxMcpArguments)
        {
            errors.Add($"{key}: args may hold at most {CatalogParser.MaxMcpArguments} entries.");
        }

        foreach (var (argument, index) in server.Args.Select((a, i) => (a, i)))
        {
            if (argument is null || argument.Length > CatalogParser.MaxMcpTextLength || CatalogParser.McpControlCharacterPattern().IsMatch(argument))
            {
                errors.Add($"{key}.args[{index}]: must be a text of at most {CatalogParser.MaxMcpTextLength} characters.");
            }
            else if (SecretScanner.Find(argument) is not null)
            {
                errors.Add($"{key}.args[{index}]: looks like it contains a secret; send the argument without the value, or name a variable.");
            }
        }
    }

    private static void ValidateValues(IReadOnlyList<CapturedValue> values, string key, bool header, List<string> errors)
    {
        if (values.Count > CatalogParser.MaxMcpValues)
        {
            errors.Add($"{key}: at most {CatalogParser.MaxMcpValues} entries.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var path = $"{key}.{value.Name}";
            var namePattern = header ? CatalogParser.McpHeaderNamePattern() : CatalogParser.McpVariableNamePattern();
            if (string.IsNullOrEmpty(value.Name) || !namePattern.IsMatch(value.Name))
            {
                errors.Add($"{key}: has an invalid name.");
                continue;
            }

            if (!seen.Add(value.Name))
            {
                errors.Add($"{path}: is listed twice.");
            }

            ValidateValue(value, path, errors);
        }
    }

    private static void ValidateValue(CapturedValue value, string path, List<string> errors)
    {
        if (value.Variable is not null && value.Literal is not null)
        {
            errors.Add($"{path}: give a variable or a literal, not both.");
        }
        else if (value.Variable is not null && !CatalogParser.McpVariableNamePattern().IsMatch(value.Variable))
        {
            errors.Add($"{path}: variable must be the name of an environment variable (letters, digits and underscores).");
        }
        else if (value.Literal is { } literal)
        {
            if (literal.Length == 0 || literal.Length > CatalogParser.MaxMcpTextLength || CatalogParser.McpControlCharacterPattern().IsMatch(literal))
            {
                errors.Add($"{path}: literal must be a text of at most {CatalogParser.MaxMcpTextLength} characters.");
            }
            else if (CatalogParser.McpSensitiveNamePattern().IsMatch(value.Name))
            {
                errors.Add($"{path}: looks like a credential: send the name of the variable that holds it, never the value.");
            }
            else if (SecretScanner.Find(literal) is not null)
            {
                errors.Add($"{path}: looks like it contains a secret; send a variable name, never the value.");
            }
        }
    }

    private static bool IsId(string value) => value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') && !value.StartsWith('-') && !value.EndsWith('-');
}
