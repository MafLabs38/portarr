using System.Reflection;
using Portarr.Reader.Containers;
using Portarr.Reader.Machine;

namespace Portarr.Reader;

/// <summary>
/// The reader (D-100): one small read-only service per machine. It speaks the two protocols Portarr already reads, a
/// node_exporter-format <c>/v1/host</c> and a filtered Docker window at <c>/v1/docker/</c>, behind one token.
/// </summary>
public static class ReaderApp
{
    public static readonly string[] Capabilities = ["host", "docker"];

    /// <summary>Builds the app; <paramref name="settings"/> overrides the environment (tests).</summary>
    public static WebApplication Build(string[] args, IEnumerable<KeyValuePair<string, string?>>? settings = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddEnvironmentVariables(ReaderOptions.EnvironmentPrefix);
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        var options = builder.Configuration.Get<ReaderOptions>() ?? new ReaderOptions();
        options.Validate();
        builder.WebHost.UseUrls(options.Urls);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<HostReader>();
        builder.Services.AddSingleton<DockerGateway>();

        var app = builder.Build();
        app.UseMiddleware<TokenMiddleware>();
        MapRoutes(app, options);
        return app;
    }

    private static void MapRoutes(WebApplication app, ReaderOptions options)
    {
        var version = typeof(ReaderApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
        var name = string.IsNullOrWhiteSpace(options.HostName) ? Environment.MachineName : options.HostName;

        app.MapGet(TokenMiddleware.HealthPath, () => Results.Text("ok"));
        app.MapGet("/v1/info", () => Results.Json(new { name = "portarr-reader", version, host = name, capabilities = Capabilities }));
        app.MapGet("/v1/host", async (HostReader reader, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Text(NodeExporterText.Write(await reader.ReadAsync(cancellationToken)), NodeExporterText.ContentType);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                return Results.Json(new { message = "The host's /proc could not be read." }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        var docker = app.MapGroup("/v1/docker");
        docker.MapGet("info", (DockerGateway gateway, CancellationToken cancellationToken) => gateway.InfoAsync(cancellationToken));
        docker.MapGet("containers/json", (DockerGateway gateway, bool? all, CancellationToken cancellationToken) => gateway.ListAsync(all ?? false, cancellationToken));
        docker.MapGet("containers/{name}/json", (DockerGateway gateway, string name, CancellationToken cancellationToken) =>
            DockerGateway.IsValidName(name) ? gateway.InspectAsync(name, cancellationToken) : Task.FromResult(Results.BadRequest()));
        docker.MapGet("containers/{name}/stats", (DockerGateway gateway, string name, CancellationToken cancellationToken) =>
            DockerGateway.IsValidName(name) ? gateway.StatsAsync(name, cancellationToken) : Task.FromResult(Results.BadRequest()));
        docker.MapGet("containers/{name}/logs", async (HttpContext context, DockerGateway gateway, string name, bool? stdout, bool? stderr, bool? timestamps, int? tail, bool? follow) =>
        {
            if (!DockerGateway.IsValidName(name))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await gateway.LogsAsync(context, name, new LogQuery(stdout ?? true, stderr ?? true, timestamps ?? false, tail ?? 100, follow ?? false), context.RequestAborted);
        });
    }
}
