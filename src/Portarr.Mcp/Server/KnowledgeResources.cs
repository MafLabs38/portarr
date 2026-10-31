using System.ComponentModel;
using ModelContextProtocol.Server;
using Portarr.Mcp.Knowledge;

namespace Portarr.Mcp.Server;

/// <summary>The knowledge base resources (V5, D-69): the whole map in one read, and the vocabulary.</summary>
[McpServerResourceType]
public sealed class KnowledgeResources
{
    private readonly KnowledgeBase _knowledgeBase;

    public KnowledgeResources(KnowledgeBase knowledgeBase)
    {
        _knowledgeBase = knowledgeBase;
    }

    [McpServerResource(UriTemplate = KnowledgeBase.InfrastructureUri, Name = "infrastructure", Title = "Infrastructure knowledge base", MimeType = "text/markdown")]
    [Description("The whole map of the homelab, generated from the live state: environments and machines (VMs under their hypervisor), stacks and apps with URLs, roles and where they run, data assets and backup jobs, repositories and images.")]
    public string Infrastructure() => _knowledgeBase.BuildInfrastructure();

    [McpServerResource(UriTemplate = KnowledgeBase.GlossaryUri, Name = "glossary", Title = "Glossary", MimeType = "text/markdown")]
    [Description("What Portarr's statuses, roles, coverage states, verdicts and reason codes mean.")]
    public static string GlossaryText() => Glossary.Markdown;
}
