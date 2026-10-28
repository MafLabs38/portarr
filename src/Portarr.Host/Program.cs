using Portarr.Api.V1.System;
using Portarr.Core;
using Portarr.Http;

// The content root is the binary's folder, whatever the working directory (like the *arr apps).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Settings come from appsettings.json, then environment variables prefixed with PORTARR__.
builder.Configuration.AddEnvironmentVariables(prefix: "PORTARR__");

builder.Services.AddPortarrCore();
builder.Services
    .AddPortarrHttp()
    .AddApplicationPart(typeof(SystemController).Assembly);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UsePortarrFrontendFiles();
app.UseRouting();
app.MapControllers();
app.MapPortarrFrontendFallback();

await app.RunAsync();

/// <summary>Entry point, exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
