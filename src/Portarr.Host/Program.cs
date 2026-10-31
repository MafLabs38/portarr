using Portarr.Api.V1;
using Portarr.Core;
using Portarr.Core.Datastore.Migration;
using Portarr.Core.Instrumentation;
using Portarr.Host.Logging;
using Portarr.Http;
using Portarr.Providers.Backrest;
using Portarr.Providers.Cloudflare;
using Portarr.Providers.Docker;
using Portarr.Providers.GitHub;
using Portarr.Providers.Kubernetes;
using Portarr.Providers.NodeExporter;
using Portarr.Providers.Registries;

// The content root is the binary's folder, whatever the working directory (like the *arr apps).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Settings come from appsettings.json, then environment variables prefixed with PORTARR__.
builder.Configuration.AddEnvironmentVariables(prefix: "PORTARR__");

builder.Services.AddPortarrCore(builder.Configuration);
builder.Services.AddPortarrLogging();
builder.Services.AddDockerProvider();
builder.Services.AddCloudflareProvider();
builder.Services.AddKubernetesProvider();
builder.Services.AddBackrestProvider();
builder.Services.AddNodeExporterProvider();
builder.Services.AddGitHubProvider();
builder.Services.AddRegistriesProvider();
builder.Services
    .AddPortarrHttp()
    .AddPortarrApiV1();

var app = builder.Build();

// Pending migrations run before the first request is served.
app.Services.GetRequiredService<IMigrationController>().Migrate();
await app.Services.GetRequiredService<ILogLevelService>().RestoreAsync();

app.UsePortarrForwardedHeaders();
app.UsePortarrRequestLogging();
app.UseExceptionHandler();
app.UsePortarrSecurityHeaders();
app.UsePortarrApiDocsSecurityPolicy();
app.UseStatusCodePages();
app.UsePortarrFrontendFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UsePortarrMetrics();
app.MapControllers();
app.MapPortarrApiDocs();
app.MapPortarrHealthChecks();
app.MapPortarrFrontendFallback();

await app.RunAsync();

/// <summary>Entry point, exposed for integration tests (WebApplicationFactory).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1118:Utility classes should not have public constructors", Justification = "WebApplicationFactory<Program> needs a public type to start the host; it is not a utility class.")]
public partial class Program;
