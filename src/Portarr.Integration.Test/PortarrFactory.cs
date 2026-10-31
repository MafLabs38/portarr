using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Checks;
using Portarr.Core.Configuration;
using Portarr.Core.Maintenance;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test;

/// <summary>
/// Runs the real host in memory with a controllable clock, on the test database.
/// </summary>
public sealed class PortarrFactory : WebApplicationFactory<Program>
{
    private readonly PortarrOptions _options;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    /// <param name="options">The test database and data folder.</param>
    /// <param name="settings">More settings, as configuration keys (<c>LogsOfAllWorkloads</c>).</param>
    /// <param name="configureServices">Replaces services, e.g. a workload source reading a faked tool.</param>
    public PortarrFactory(PortarrOptions options, IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)
    {
        _options = options;
        _settings = settings ?? new Dictionary<string, string?>();
        _configureServices = configureServices;
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Replaces the Docker connector, so API tests never need a Docker Engine.</summary>
    public FakeWorkloadSource Workloads { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataFolder"] = _options.DataFolder,
            ["Database:Type"] = _options.Database.Type.ToString(),
            ["Database:ConnectionString"] = _options.Database.ConnectionString,
            ["LogsOfAllWorkloads"] = _options.LogsOfAllWorkloads.ToString(),
        }.Concat(_settings)));
        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
            services.RemoveAll<IWorkloadSource>();
            services.AddSingleton<IWorkloadSource>(Workloads);

            // Registries and code hosts are on the internet: tests never call them.
            services.RemoveAll<IImageRegistry>();
            services.RemoveAll<ICodeRepositorySource>();

            // App checks never leave the test: every URL answers 200.
            services.AddHttpClient(HttpCheckRunner.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new OkHandler());

            _configureServices?.Invoke(services);
        });
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
