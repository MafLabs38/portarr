using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Portarr.Common;
using Portarr.Mcp.Chat;
using Portarr.Mcp.Digest;
using Portarr.Mcp.Knowledge;
using Portarr.Mcp.Server;

namespace Portarr.Mcp;

public static class McpServiceCollectionExtensions
{
    /// <summary>Where the MCP endpoint is mapped (Streamable HTTP).</summary>
    public const string Path = "/mcp";

    public const string RateLimitPolicyName = "mcp";
    public const string AuthorizationPolicyName = "mcp";

    /// <summary>Requests per client and per minute: an assistant answering one question makes a handful of calls.</summary>
    public const int RequestsPerMinute = 120;

    public const string Instructions = """
        Portarr is a portal over a homelab: apps and stacks, environments and machines (VMs under their hypervisor),
        data assets and backups, code repositories and container images, all from its live view, plus what the
        operator wrote down: notes, how to reach each machine, network links and external providers.
        Call get_overview first to see what needs attention. Read the resource portarr://knowledge/infrastructure to
        learn the whole map at once, portarr://knowledge/glossary for the meaning of statuses and codes, and
        get_environment_knowledge for access, links and providers.
        Portarr never acts on the infrastructure: suggest what the operator could do.
        """;

    /// <summary>Added to the instructions when the catalog write tools are on (D-86).</summary>
    public const string WriteInstructions = """
        The write tools edit only Portarr's catalog file (its description of the fleet), never the infrastructure.
        Read the item first with get_catalog_item, send its version with the write, change only what the operator
        asked for and say what you changed. Never write a secret: name the key file or the variable instead.
        get_portal and set_portal read and change the name, logo and name/version colors shown in the sidebar.
        """;

    /// <summary>
    /// The MCP server (V5): its tools and resources are always registered, so the Assistant page can list them,
    /// but <see cref="MapPortarrMcp"/> only maps the endpoint when <see cref="McpOptions.Enabled"/> is true.
    /// Also serves <c>/api/v1/assistant</c> (status and knowledge preview) to the UI.
    /// </summary>
    public static IMvcBuilder AddPortarrMcp(this IMvcBuilder mvc, IConfiguration configuration)
    {
        var services = mvc.Services;
        services.AddOptions<McpOptions>()
            .Bind(configuration.GetSection(McpOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<McpOptions>, McpOptionsValidator>();

        // The built-in chat (D-89): a proxy to the optional portarr-agent runner.
        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AgentOptions>, AgentOptionsValidator>();
        services.AddHttpClient<AgentRunnerClient>();

        services.AddSingleton<FleetDigest>();
        services.AddSingleton<KnowledgeBase>();
        services.AddHttpContextAccessor();
        services.AddSingleton<CatalogTools>();

        // Small answers: null fields are left out.
        var json = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "portarr", Title = BuildInfo.AppName, Version = BuildInfo.Version };
                options.ServerInstructions = Instructions;
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<PortarrTools>(json)
            .WithResources<KnowledgeResources>();

        // The catalog write tools (D-86) exist only when asked for: an assistant cannot even see them otherwise.
        // Added once the options are bound (not while registering), so every configuration source counts.
        services.AddSingleton<IConfigureOptions<McpServerOptions>>(provider => new CatalogToolsSetup(provider, json));

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicyName, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());

        // Counted before authentication: wrong keys are rate-limited too.
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(RateLimitPolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));

        return mvc.AddApplicationPart(typeof(McpServiceCollectionExtensions).Assembly);
    }

    /// <summary>Maps <c>/mcp</c> when enabled; otherwise the path stays a plain 404.</summary>
    public static WebApplication MapPortarrMcp(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<McpOptions>>().Value.Enabled)
        {
            app.MapMcp(Path)
                .RequireRateLimiting(RateLimitPolicyName)
                .RequireAuthorization(AuthorizationPolicyName);
        }

        return app;
    }

    /// <summary>The write tools and their instructions, when <see cref="McpOptions.WriteTools"/> is on.</summary>
    private sealed class CatalogToolsSetup : IConfigureOptions<McpServerOptions>
    {
        private readonly IServiceProvider _provider;
        private readonly JsonSerializerOptions _json;

        public CatalogToolsSetup(IServiceProvider provider, JsonSerializerOptions json)
        {
            _provider = provider;
            _json = json;
        }

        public void Configure(McpServerOptions options)
        {
            if (!_provider.GetRequiredService<IOptions<McpOptions>>().Value.WriteTools)
            {
                return;
            }

            options.ServerInstructions = Instructions + WriteInstructions;
            options.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();
            var target = _provider.GetRequiredService<CatalogTools>();
            foreach (var method in typeof(CatalogTools).GetMethods().Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
            {
                options.ToolCollection.Add(McpServerTool.Create(method, target, new McpServerToolCreateOptions { Services = _provider, SerializerOptions = _json }));
            }
        }
    }

    /// <summary>Whether a request targets the MCP endpoint (for the request log and the SPA fallback).</summary>
    public static bool IsMcpRequest(HttpContext context) => context.Request.Path.StartsWithSegments(Path, StringComparison.OrdinalIgnoreCase);
}
