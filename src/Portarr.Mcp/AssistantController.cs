using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog.Editing;
using Portarr.Mcp.Knowledge;

namespace Portarr.Mcp;

/// <summary>A tool of the MCP server, as the Assistant page lists it. <c>ReadOnly</c> is false for the catalog write tools.</summary>
public sealed record AssistantToolResource(string Name, string? Title, string? Description, bool ReadOnly = true);

public sealed record AssistantResourceResource(string Uri, string Name, string? Description);

/// <summary>
/// The MCP server's state for the Assistant page. <c>Path</c> is relative: the UI joins it with the address it is
/// served from. <c>KeyConfigured</c> only says a key is set; the key itself is never returned. <c>WriteTools</c> says
/// whether the catalog write tools are registered, <c>CatalogWritable</c> whether catalog edits are on (D-86).
/// </summary>
public sealed record AssistantStatusResource(
    bool Enabled,
    string Path,
    bool KeyConfigured,
    string HeaderName,
    IReadOnlyList<AssistantToolResource> Tools,
    IReadOnlyList<AssistantResourceResource> Resources,
    bool WriteTools = false,
    bool CatalogWritable = false);

public sealed record KnowledgeResource(string Uri, DateTimeOffset GeneratedAt, string Markdown);

[ApiController]
[Authorize(Policy = Permissions.AssistantView)]
[Route("api/v1/assistant")]
public sealed class AssistantController : ControllerBase
{
    private readonly IOptions<McpOptions> _options;
    private readonly IEnumerable<McpServerTool> _tools;
    private readonly IEnumerable<McpServerResource> _resources;
    private readonly KnowledgeBase _knowledgeBase;
    private readonly TimeProvider _timeProvider;
    private readonly ICatalogEditor _editor;

    public AssistantController(
        IOptions<McpOptions> options,
        IOptions<McpServerOptions> serverOptions,
        IEnumerable<McpServerTool> tools,
        IEnumerable<McpServerResource> resources,
        KnowledgeBase knowledgeBase,
        TimeProvider timeProvider,
        ICatalogEditor editor)
    {
        _editor = editor;
        _options = options;
        // Tools registered in DI, and those added to the server options (the write tools, D-86).
        _tools = tools.Concat(serverOptions.Value.ToolCollection?.ToArray() ?? []).DistinctBy(t => t.ProtocolTool.Name).ToList();
        _resources = resources;
        _knowledgeBase = knowledgeBase;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    [EndpointSummary("The MCP server of Portarr: whether it is on, its address, its tools and resources, and whether the catalog write tools are on.")]
    public AssistantStatusResource Get()
    {
        var options = _options.Value;
        return new AssistantStatusResource(
            options.Enabled,
            McpServiceCollectionExtensions.Path,
            !string.IsNullOrWhiteSpace(options.ApiKey),
            Portarr.Core.Configuration.ApiKeys.HeaderName,
            _tools.Select(t => new AssistantToolResource(t.ProtocolTool.Name, t.ProtocolTool.Title, t.ProtocolTool.Description, t.ProtocolTool.Annotations?.ReadOnlyHint ?? false))
                .OrderBy(t => !t.ReadOnly)
                .ThenBy(t => t.Name, StringComparer.Ordinal)
                .ToList(),
            _resources.Where(r => r.ProtocolResource is not null)
                .Select(r => new AssistantResourceResource(r.ProtocolResource!.Uri, r.ProtocolResource.Name, r.ProtocolResource.Description))
                .ToList(),
            options.WriteTools,
            _editor.Writable);
    }

    /// <summary>The knowledge base an assistant reads, for the operator to see what it learns. Same data as the pages.</summary>
    [HttpGet("knowledge")]
    [EndpointSummary("The knowledge base an assistant reads, as Markdown.")]
    public KnowledgeResource GetKnowledge()
    {
        return new KnowledgeResource(KnowledgeBase.InfrastructureUri, _timeProvider.GetUtcNow(), _knowledgeBase.BuildInfrastructure());
    }
}
