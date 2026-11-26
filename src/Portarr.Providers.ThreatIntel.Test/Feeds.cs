using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NSubstitute;
using Portarr.Core.Configuration;
using Portarr.Core.Maintenance;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.ThreatIntel.Test;

/// <summary>The two feeds in their publishers' formats, and a fake that serves them: what the connector's tests read.</summary>
internal static class Feeds
{
    public static readonly Uri KevUrl = new("https://feeds.example/kev.json");
    public static readonly Uri EpssUrl = new("https://feeds.example/epss.csv.gz");

    /// <summary>The KEV catalog in CISA's JSON format.</summary>
    public static string Kev(DateTimeOffset released, IEnumerable<KeyValuePair<string, KnownExploited>> entries)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "CISA Catalog of Known Exploited Vulnerabilities",
            ["catalogVersion"] = released.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
            ["dateReleased"] = released.ToString("yyyy-MM-ddTHH:mm:ss.ffffZ", CultureInfo.InvariantCulture),
            ["vulnerabilities"] = entries.Select(e => new Dictionary<string, object?>
            {
                ["cveID"] = e.Key,
                ["vendorProject"] = e.Value.VendorProject,
                ["product"] = e.Value.Product,
                ["vulnerabilityName"] = e.Value.Name,
                ["dateAdded"] = e.Value.DateAdded.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["shortDescription"] = "A flaw.",
                ["requiredAction"] = "Apply mitigations per vendor instructions.",
                ["dueDate"] = e.Value.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["knownRansomwareCampaignUse"] = e.Value.KnownRansomwareUse ? "Known" : "Unknown",
                ["notes"] = string.Empty,
                ["cwes"] = Array.Empty<string>(),
            }),
        });
    }

    /// <summary>The EPSS scores in FIRST's CSV format, before compression.</summary>
    public static string EpssText(DateTimeOffset scoreDate, IEnumerable<KeyValuePair<string, ExploitPrediction>> rows)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"#model_version:v2025.03.14,score_date:{scoreDate:yyyy-MM-ddTHH:mm:ssZ}\n");
        text.Append("cve,epss,percentile\n");
        foreach (var (id, prediction) in rows)
        {
            text.Append(CultureInfo.InvariantCulture, $"{id},{prediction.Score:0.#####},{prediction.Percentile:0.#####}\n");
        }

        return text.ToString();
    }

    public static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    public static KevEpssExploitIntelligence Subject(HttpMessageHandler handler, Uri? kevUrl = null, Uri? epssUrl = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ThreatIntelHttp.ClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var options = new PortarrOptions { ThreatIntel = new ThreatIntelOptions { KevUrl = kevUrl ?? KevUrl, EpssUrl = epssUrl ?? EpssUrl } };
        return new KevEpssExploitIntelligence(factory, Options.Create(options));
    }

    /// <summary>A fake publisher: answers each request with what <c>Answer</c> says, and records it.</summary>
    public sealed class Publisher : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public bool Failing { get; set; }

        public IReadOnlyList<RecordedRequest> Recorded => [.. Requests.Select(r => new RecordedRequest(r.Method, r.RequestUri!.PathAndQuery))];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Failing ? throw new HttpRequestException("connection refused") : Task.FromResult(Answer(request));
        }
    }

    public static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    public static HttpResponseMessage Ok(Stream stream) => new(HttpStatusCode.OK) { Content = new StreamContent(stream) };

    public static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }
}
