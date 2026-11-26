using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;

namespace Portarr.Providers.Trivy;

/// <summary>
/// Reads the image scanner companion (D-131, <c>scanner/</c>): a container that runs the Trivy command line on the images Portarr names, one
/// at a time, and answers with their vulnerabilities. Trivy's server mode is no "scan this image" API, hence the companion. Portarr tells it
/// which images run (<c>POST /v1/scans</c>) and reads what it found (<c>GET /v1/results</c>); the companion only pulls image layers from
/// registries, nothing runs on the fleet (D-16, D-31). The shared secret is read from the variable the catalog names and sent as a bearer
/// token to that address only; it is never logged nor returned.
/// </summary>
public sealed partial class TrivyImageScanner : IImageScanner
{
    public const string HttpClientName = "trivy-scanner";

    /// <summary>The companion takes at most 200 references per request, each of at most 300 characters.</summary>
    public const int MaxImagesPerRequest = 200;

    public const int MaxReferenceLength = 300;

    /// <summary>A release target is <c>owner/name</c>, each part of at most 100 characters.</summary>
    private const int MaxRepositoryLength = 201;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICatalogService _catalogService;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public TrivyImageScanner(IHttpClientFactory httpClientFactory, ICatalogService catalogService, IConfiguration configuration, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _catalogService = catalogService;
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    public async Task RequestAsync(IReadOnlyCollection<ScanTarget> targets, CancellationToken cancellationToken)
    {
        var source = Source();
        var secret = Secret(source);
        // What the companion would refuse (it validates every reference, and refuses the whole request) is left out here.
        var plain = targets.Where(IsValid).Distinct().ToList();
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        foreach (var batch in plain.Chunk(MaxImagesPerRequest))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(source.Url, "v1/scans"))
            {
                Content = JsonContent.Create(new ScanRequest([.. batch.Select(target => new ScanRequestTarget(target.Kind, target.Ref))]), ScannerJsonContext.Default.ScanRequest),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var response = await client.SendAsync(request, cancellationToken);
            EnsureSuccess(response, "POST v1/scans");
        }
    }

    public async Task<ScannerReport> ReadAsync(CancellationToken cancellationToken)
    {
        var source = Source();
        var secret = Secret(source);
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(source.Url, "v1/results"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, cancellationToken);
        EnsureSuccess(response, "GET v1/results");

        var results = await response.Content.ReadFromJsonAsync(ScannerJsonContext.Default.ScannerResults, cancellationToken)
            ?? throw new InvalidDataException("Empty answer from the scanner.");
        return ToReport(results, _timeProvider.GetUtcNow());
    }

    /// <summary>The companion's answer as the Core's report, with the Core's safety rules applied (<see cref="ScannerReport.Normalised"/>).</summary>
    internal static ScannerReport ToReport(ScannerResults results, DateTimeOffset now)
    {
        var images = (results.Images ?? []).Where(i => i.Ref is not null).Select(ToResult).ToList();
        return new ScannerReport(results.GeneratedAt ?? now, results.Database?.UpdatedAt, images).Normalised();
    }

    private static ImageScanResult ToResult(ScannerImage image) => new(
        image.Ref!,
        image.State switch
        {
            "scanned" => ImageScanState.Scanned,
            "queued" => ImageScanState.Queued,
            "scanning" => ImageScanState.Scanning,
            _ => ImageScanState.Failed,
        },
        image.Reason,
        image.ScannedAt,
        ToCounts(image.Counts),
        ToCounts(image.Fixable),
        [.. (image.Findings ?? []).Select(ToFinding)],
        // A result without a kind is an image's: the kind was added for the targets that come later.
        image.Kind ?? ScanTargetKinds.Image,
        image.Version,
        // The ids of every finding (D-133); a companion older than them sends none, and Portarr falls back to the findings' ids.
        image.Ids is null ? null : [.. image.Ids.OfType<string>()],
        image.IdsTruncated == true);

    private static VulnerabilityCounts ToCounts(ScannerCounts? counts) =>
        counts is null ? VulnerabilityCounts.None : new VulnerabilityCounts(counts.Critical, counts.High, counts.Medium, counts.Low, counts.Unknown);

    private static ImageFinding ToFinding(ScannerFinding finding) => new(
        finding.Id ?? string.Empty,
        finding.Package ?? string.Empty,
        finding.InstalledVersion ?? string.Empty,
        finding.FixedVersion,
        finding.Severity,
        finding.Title,
        Uri.TryCreate(finding.Url, UriKind.Absolute, out var url) ? url : null);

    internal static bool IsPlainReference(string reference) =>
        reference.Length is > 0 and <= MaxReferenceLength && ReferencePattern().IsMatch(reference);

    /// <summary>Whether the companion would accept the target: an image by its plain reference, a release by its repository name (<c>owner/name</c>); any other kind is left out.</summary>
    internal static bool IsValid(ScanTarget target) => target.Kind switch
    {
        ScanTargetKinds.Image => IsPlainReference(target.Ref),
        ScanTargetKinds.Release => target.Ref.Length <= MaxRepositoryLength && RepositoryPattern().IsMatch(target.Ref) && target.Ref.Split('/').All(part => part is not ("." or "..")),
        _ => false,
    };

    private ImageScannerSource Source()
    {
        return _catalogService.Current.Catalog.ImageScanner
            ?? throw new InvalidOperationException("The catalog declares no image scanner.");
    }

    private string Secret(ImageScannerSource source)
    {
        var secret = _configuration[source.SecretVariable];
        return string.IsNullOrWhiteSpace(secret) ? throw new ImageScannerUnavailableException(ImageScannerUnavailableException.SecretMissing) : secret;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string call)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{call} answered {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }

    /// <summary>The companion's own rule for an image reference: names, a tag, a digest, starting with a letter or a digit.</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/:@-]*$")]
    private static partial Regex ReferencePattern();

    /// <summary>The companion's own rule for the repository of a release: <c>owner/name</c>.</summary>
    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepositoryPattern();
}
