namespace Portarr.Mcp.Digest;

/// <summary>
/// A URL as an assistant may see it: scheme, host, port and path only. Credentials (<c>user:password@</c>), the
/// query (often a token) and the fragment never leave Portarr (V5 security model).
/// </summary>
public static class SafeUrl
{
    public static string Of(Uri url)
    {
        if (!url.IsAbsoluteUri)
        {
            return string.Empty;
        }

        var builder = new UriBuilder(url) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.ToString();
    }
}
