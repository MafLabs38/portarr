using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Portarr.Integration.Test;

/// <summary>
/// A third-party tool (Backrest, the Kubernetes API, node_exporter, GitHub…) played by WireMock.Net over real HTTP on
/// a local port, so the connector under test runs with its real HTTP stack (TESTING.md §3). No Docker needed.
/// </summary>
public sealed class ToolServer : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    public string Url => _server.Url!;

    /// <summary>Every request the tool received so far, as <c>METHOD /path</c>.</summary>
    public IReadOnlyList<string> Calls => _server.LogEntries.Select(e => $"{e.RequestMessage?.Method} {e.RequestMessage?.Path}").ToList();

    /// <summary>Every request the tool received so far, as the full URL with its query string.</summary>
    public IReadOnlyList<string> Urls => _server.LogEntries.Select(e => e.RequestMessage?.Url ?? string.Empty).ToList();

    /// <summary>The <c>Authorization</c> header of every request (empty when there was none).</summary>
    public IReadOnlyList<string> Authorizations => _server.LogEntries
        .Select(e => e.RequestMessage?.Headers is { } headers && headers.TryGetValue("Authorization", out var values) ? values.FirstOrDefault() ?? string.Empty : string.Empty)
        .ToList();

    /// <summary>Answers <paramref name="method"/> <paramref name="path"/> with a JSON (or text) body.</summary>
    public ToolServer Serve(string method, string path, string body, int status = 200, string contentType = "application/json")
    {
        _server.Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", contentType).WithBody(body));
        return this;
    }

    /// <summary>Answers a POST whose body contains <paramref name="bodyPart"/> (Connect protocol methods that share a path).</summary>
    public ToolServer ServePost(string path, string bodyPart, string body)
    {
        _server.Given(Request.Create().WithPath(path).UsingPost().WithBody(new WireMock.Matchers.WildcardMatcher($"*{bodyPart}*")))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body));
        return this;
    }

    /// <summary>Forgets every answer and call, to serve a different state.</summary>
    public void Reset() => _server.Reset();

    public void Dispose() => _server.Dispose();
}
