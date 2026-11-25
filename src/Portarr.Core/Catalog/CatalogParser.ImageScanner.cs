namespace Portarr.Core.Catalog;

// The image scanner companion (D-131): where it listens and the variable that holds its shared secret.
public static partial class CatalogParser
{
    private sealed partial class Builder
    {
        /// <summary>
        /// <c>imageScanner:</c>: the address of the scanner companion and the name of the variable holding the secret both share, never the
        /// secret. Like the other connectors, the variable is confined to its namespace; http (the secret in clear) needs <c>allowInsecure</c>.
        /// </summary>
        private ImageScannerSource? BuildImageScanner(ImageScannerDocument? document)
        {
            if (document is null)
            {
                return null;
            }

            const string owner = "imageScanner";
            var errors = _errors.Count;

            var url = CatalogValues.HttpUrl(document.Url);
            if (url is null || url.Query.Length > 0 || url.Fragment.Length > 0)
            {
                _errors.Add($"{owner}: url must be the scanner's address (e.g. http://scanner:8489), without credentials, query or fragment.");
            }
            else if (url.Scheme == HttpScheme && document.AllowInsecure != true)
            {
                _errors.Add($"{owner}: url is plain http and the shared secret would travel in clear; use https, or set allowInsecure: true for a scanner on a network you trust (a Compose network).");
            }

            if (CatalogValues.SecretVariableError("secretEnv", document.SecretEnv, ImageScannerSource.SecretPrefix, ImageScannerSource.DefaultSecretVariable, "secret", _allowedSecretVariables, out _) is { } secretError)
            {
                _errors.Add($"{owner}: {secretError}");
            }

            if (_errors.Count > errors || url is null)
            {
                return null;
            }

            // The requests are built from this address: without a trailing slash the last segment of a path prefix would be dropped.
            var normalized = new UriBuilder(url) { Path = url.AbsolutePath.TrimEnd(Path.AltDirectorySeparatorChar) + Path.AltDirectorySeparatorChar }.Uri;
            return new ImageScannerSource(normalized, CatalogValues.Blank(document.SecretEnv) ?? ImageScannerSource.DefaultSecretVariable, document.AllowInsecure == true);
        }
    }
}
