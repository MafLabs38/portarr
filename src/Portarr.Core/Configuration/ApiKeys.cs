using System.Security.Cryptography;
using System.Text;

namespace Portarr.Core.Configuration;

/// <summary>The shared keys that stand in for accounts until C2 (MCP, D-70; catalog writes, D-85).</summary>
public static class ApiKeys
{
    /// <summary>Shortest key accepted: 32 characters (e.g. <c>openssl rand -hex 32</c> gives 64).</summary>
    public const int MinimumLength = 32;

    public const string HeaderName = "X-Api-Key";

    private const string BearerPrefix = "Bearer ";

    /// <summary>The key a request carries: <c>X-Api-Key</c> first, else a bearer token; null without either.</summary>
    public static string? Read(string? apiKeyHeader, string? authorizationHeader)
    {
        if (!string.IsNullOrEmpty(apiKeyHeader))
        {
            return apiKeyHeader;
        }

        return authorizationHeader?.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) == true ? authorizationHeader[BearerPrefix.Length..].Trim() : null;
    }

    /// <summary>
    /// Compares the SHA-256 hashes of both keys in constant time, so neither the content nor the length of the
    /// key leaks through timing.
    /// </summary>
    public static bool Matches(string? presented, string? expected)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected.Trim())));
    }
}
