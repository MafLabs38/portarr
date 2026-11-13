using System.Net;
using System.Text;

namespace Portarr.Providers.GpuExporter.Test;

/// <summary>An HTTP client factory whose client answers from a function: the request in, the response out, every request kept.</summary>
internal sealed class StubHttp : IHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;

    public StubHttp(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        _answer = answer;
    }

    public List<Uri> Requests { get; } = [];

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    public HttpClient CreateClient(string name) => new(new Handler(this));

    private sealed class Handler(StubHttp owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner.Requests.Add(request.RequestUri!);
            return Task.FromResult(owner._answer(request));
        }
    }
}
