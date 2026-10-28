using Portarr.Api.V1;
using Portarr.Core;
using Portarr.Core.Datastore.Migration;
using Portarr.Core.Instrumentation;
using Portarr.Host.Logging;
using Portarr.Http;
using Portarr.Providers.Docker;

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
builder.Services
    .AddPortarrHttp()
    .AddPortarrApiV1();

var app = builder.Build();

// Pending migrations run before the first request is served.
app.Services.GetRequiredService<IMigrationController>().Migrate();
await app.Services.GetRequiredService<ILogLevelService>().RestoreAsync();

app.UsePortarrRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UsePortarrFrontendFiles();
app.UseRouting();
app.UsePortarrMetrics();
app.MapControllers();
app.MapPortarrHealthChecks();
app.MapPortarrFrontendFallback();

await app.RunAsync();

/// <summary>Entry point, exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
