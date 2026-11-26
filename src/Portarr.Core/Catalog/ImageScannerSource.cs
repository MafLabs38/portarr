namespace Portarr.Core.Catalog;

/// <summary>The scanner companion the catalog declares (<c>imageScanner:</c>): where it listens and the variable holding the shared secret, never the secret.</summary>
public sealed record ImageScannerSource(Uri Url, string SecretVariable, bool AllowInsecure = false)
{
    public const string DefaultSecretVariable = "PORTARR_SCANNER_SECRET";

    /// <summary>The namespace of the variables a catalog may name as the scanner's secret.</summary>
    public const string SecretPrefix = "PORTARR_SCANNER_";

    /// <summary>What a change of the declaration depends on: the monitor asks again at once when it changes.</summary>
    public string Fingerprint => string.Join('|', Url, SecretVariable);
}
