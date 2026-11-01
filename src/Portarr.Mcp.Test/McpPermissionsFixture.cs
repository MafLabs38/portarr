using System.Reflection;
using ModelContextProtocol.Server;
using Portarr.Core.Accounts;

namespace Portarr.Mcp.Test;

/// <summary>The MCP permission model fails closed: a tool or a resource that declares no permission is denied.</summary>
public sealed class McpPermissionsFixture
{
    private static readonly Assembly McpAssembly = typeof(RequiresPermissionsAttribute).Assembly;

    [Fact]
    public void should_declare_the_permissions_of_every_tool_and_resource()
    {
        var undeclared = McpAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null || m.GetCustomAttribute<McpServerResourceAttribute>() is not null)
            .Where(m => m.GetCustomAttribute<RequiresPermissionsAttribute>() is null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        undeclared.Should().BeEmpty("a tool or resource with no [RequiresPermissions] is denied, so it would be unusable");
    }

    [Fact]
    public void should_ask_the_catalog_write_tools_for_catalog_write()
    {
        var writes = typeof(Server.CatalogTools).GetMethods()
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is { ReadOnly: false });

        writes.Should().OnlyContain(m => m.GetCustomAttribute<RequiresPermissionsAttribute>()!.Permissions.Contains(Permissions.CatalogWrite));
    }

    [Fact]
    public void should_deny_what_declares_nothing()
    {
        McpAccess.Declared([]).Should().BeNull();
        McpAccess.Declared(["unrelated metadata"]).Should().BeNull();
        McpAccess.Allows(null, []).Should().BeFalse();
    }

    [Fact]
    public void should_read_the_declared_permissions_once_each()
    {
        var declared = McpAccess.Declared([new RequiresPermissionsAttribute(Permissions.AppsView, Permissions.DataView), new RequiresPermissionsAttribute(Permissions.AppsView)]);

        declared.Should().BeEquivalentTo(Permissions.AppsView, Permissions.DataView);
    }
}
