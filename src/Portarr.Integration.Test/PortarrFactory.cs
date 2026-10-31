using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
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
    /// <param name="settings">More settings, as configuration keys (<c>Mcp:Enabled</c>).</param>
    /// <param name="configureServices">Replaces services, e.g. the agent runner's HTTP handler.</param>
    public PortarrFactory(PortarrOptions options, IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)
    {
        _options = options;
        _settings = settings ?? new Dictionary<string, string?>();
        _configureServices = configureServices;
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Replaces the Docker connector, so API tests never need a Docker Engine.</summary>
    public FakeWorkloadSource Workloads { get; } = new();

    /// <summary>The password of the administrator and of the role users the tests sign in as.</summary>
    public const string Password = "correct horse battery staple";

    public const string AdminUser = "admin";

    private readonly Dictionary<string, string> _sessions = [];
    private readonly SemaphoreSlim _signIn = new(1, 1);

    /// <summary>
    /// A client signed in as the user of a built-in or custom role (the first call creates the administrator through the
    /// setup endpoint; other roles get a user the administrator creates). The session cookie is kept per role, so a test
    /// run does not hit the login rate limit, and the client sends the CSRF header like the UI does.
    /// </summary>
    public async Task<HttpClient> CreateSignedInClientAsync(string role = "Admin", bool csrfHeader = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (csrfHeader)
        {
            client.DefaultRequestHeaders.Add("X-Requested-With", "portarr");
        }

        client.DefaultRequestHeaders.Add("Cookie", await SessionAsync(role));
        return client;
    }

    private async Task<string> SessionAsync(string role)
    {
        await _signIn.WaitAsync();
        try
        {
            if (_sessions.TryGetValue(role, out var known))
            {
                return known;
            }

            using var anonymous = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            anonymous.DefaultRequestHeaders.Add("X-Requested-With", "portarr");
            if (!_sessions.ContainsKey("Admin"))
            {
                var status = await anonymous.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/v1/auth/status");
                var admin = status!["setupRequired"].GetBoolean()
                    ? await anonymous.PostAsJsonAsync("/api/v1/auth/setup", new { username = AdminUser, password = Password })
                    : await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = AdminUser, password = Password });
                admin.EnsureSuccessStatusCode();
                _sessions["Admin"] = CookieOf(admin);
            }

            if (role != "Admin")
            {
                using var manager = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                manager.DefaultRequestHeaders.Add("X-Requested-With", "portarr");
                manager.DefaultRequestHeaders.Add("Cookie", _sessions["Admin"]);
                var roles = await manager.GetFromJsonAsync<RoleList>("/api/v1/roles");
                var id = roles!.Roles.Single(r => r.Name == role).Id;
                var username = $"{role.ToLowerInvariant().Replace(' ', '-')}-user";
                var created = await manager.PostAsJsonAsync("/api/v1/users", new { username, password = Password, roleId = id });
                if (created.StatusCode != System.Net.HttpStatusCode.Conflict)
                {
                    created.EnsureSuccessStatusCode();
                }

                var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username, password = Password });
                login.EnsureSuccessStatusCode();
                _sessions[role] = CookieOf(login);
            }

            return _sessions[role];
        }
        finally
        {
            _signIn.Release();
        }
    }

    private static string CookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("portarr_session=", StringComparison.Ordinal)).Split(';')[0];

    private sealed record RoleList(List<RoleItem> Roles);

    private sealed record RoleItem(int Id, string Name);

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

    /// <summary>
    /// Closes the connections of the host that just stopped: Npgsql keeps them in a pool for minutes, and a suite that
    /// starts a host (with its own database) per fixture would otherwise exhaust the server's connections once it grows
    /// ("53300: sorry, too many clients already").
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            NpgsqlConnection.ClearAllPools();
        }
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
