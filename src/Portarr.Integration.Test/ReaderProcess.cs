using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Portarr.Integration.Test;

/// <summary>
/// The Portarr reader (D-100, D-102) as it ships: the Rust binary, started as a process on a free local port. The tests that need it
/// run when <c>PORTARR_READER_BIN</c> names the binary (the CI builds it first) and are skipped otherwise, so a .NET-only checkout
/// still runs everything else.
/// </summary>
public sealed class ReaderProcess : IAsyncDisposable
{
    public const string BinaryVariable = "PORTARR_READER_BIN";

    private readonly Process _process;

    private ReaderProcess(Process process, Uri baseUrl)
    {
        _process = process;
        BaseUrl = baseUrl;
    }

    /// <summary>Whether the reader binary is available (the <c>SkipUnless</c> of the tests that start it).</summary>
    public static bool Available => File.Exists(Environment.GetEnvironmentVariable(BinaryVariable));

    public Uri BaseUrl { get; }

    public static async Task<ReaderProcess> StartAsync(string token, string dockerUrl, string procPath, CancellationToken cancellationToken)
    {
        var port = FreePort();
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable(BinaryVariable)!) { UseShellExecute = false, RedirectStandardError = true };
        start.Environment["PORTARR_READER_TOKEN"] = token;
        start.Environment["PORTARR_READER_DOCKER"] = dockerUrl;
        start.Environment["PORTARR_READER_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["PORTARR_READER_PROCPATH"] = procPath;
        start.Environment["PORTARR_READER_SYSPATH"] = procPath;

        var process = Process.Start(start) ?? throw new InvalidOperationException("The reader did not start.");
        var reader = new ReaderProcess(process, new Uri($"http://127.0.0.1:{port}"));
        try
        {
            await reader.WaitUntilHealthyAsync(cancellationToken);
            return reader;
        }
        catch
        {
            await reader.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }

        _process.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task WaitUntilHealthyAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = BaseUrl, Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"The reader exited with code {_process.ExitCode}: {await _process.StandardError.ReadToEndAsync(cancellationToken)}");
            }

            try
            {
                if ((await http.GetAsync("/healthz", cancellationToken)).StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("The reader did not answer /healthz within five seconds.");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
