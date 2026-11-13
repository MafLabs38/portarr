using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// A read-only API key keeps the permissions flagged read-only (D-115). The flag guards nothing by itself, so this is the promise it makes: no action
/// behind such a permission writes. Only GET reaches it.
/// </summary>
public sealed class ReadOnlyPermissionFixture
{
    [Fact]
    public void should_only_open_actions_that_read_behind_a_permission_a_read_only_key_keeps_beyond_view()
    {
        var readOnly = Permissions.All.Where(p => p.ReadOnly).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var controllers = typeof(Portarr.Api.V1.Mcp.McpServersController).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t));

        var writers = new List<string>();
        foreach (var controller in controllers)
        {
            var classPolicies = controller.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var verbs = action.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList();
                var policies = classPolicies.Concat(action.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy)).ToList();
                if (policies.Any(p => p is not null && readOnly.Contains(p)) && verbs.Any(v => v != "GET"))
                {
                    writers.Add($"{controller.Name}.{action.Name} ({string.Join(", ", verbs)})");
                }
            }
        }

        readOnly.Should().NotBeEmpty();
        writers.Should().BeEmpty("a read-only API key keeps these permissions: nothing behind them may write");
    }
}
