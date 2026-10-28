using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Configuration;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test;

/// <summary>
/// Runs the real host in memory with a controllable clock, on the test database.
/// </summary>
public sealed class PortarrFactory : WebApplicationFactory<Program>
{
    private readonly PortarrOptions _options;

    public PortarrFactory(PortarrOptions options)
    {
        _options = options;
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
        }));
        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
            services.RemoveAll<IWorkloadSource>();
            services.AddSingleton<IWorkloadSource>(Workloads);
        });
    }
}
