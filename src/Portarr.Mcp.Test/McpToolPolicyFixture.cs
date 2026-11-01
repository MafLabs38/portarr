using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Portarr.Mcp.Server;

namespace Portarr.Mcp.Test;

/// <summary>
/// The agent runner decides which of Portarr's tools run without asking by their name (D-88): get_, list_ and find_ run,
/// the rest wait for the operator. That is only safe while the name says what the tool does, so this ties the two: a tool
/// is read-only exactly when its name starts with one of those, and the runner's list of read tools is exactly the read tools.
/// </summary>
public sealed partial class McpToolPolicyFixture
{
    private static readonly string[] ReadPrefixes = ["get_", "list_", "find_"];

    private static IEnumerable<(string Name, bool ReadOnly)> Tools() =>
        new[] { typeof(PortarrTools), typeof(CatalogTools) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .OfType<McpServerToolAttribute>()
            .Select(tool => (tool.Name!, tool.ReadOnly));

    [Fact]
    public void should_name_a_tool_as_a_read_tool_exactly_when_it_is_read_only()
    {
        var mismatched = Tools()
            .Where(tool => ReadPrefixes.Any(prefix => tool.Name.StartsWith(prefix, StringComparison.Ordinal)) != tool.ReadOnly)
            .Select(tool => $"{tool.Name} (ReadOnly = {tool.ReadOnly})")
            .ToList();

        mismatched.Should().BeEmpty("the runner trusts the prefix: a write tool named get_… would run without asking");
    }

    [Fact]
    public void should_list_in_the_runner_exactly_the_read_only_tools()
    {
        var policy = File.ReadAllText(Path.Combine(RepositoryRoot(), "agent", "src", "policy.ts"));
        var block = KnownReadTools().Match(policy).Groups["tools"].Value;
        var known = Quoted().Matches(block).Select(m => m.Groups[1].Value).ToList();

        known.Should().BeEquivalentTo(Tools().Where(tool => tool.ReadOnly).Select(tool => tool.Name));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "agent", "src", "policy.ts")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("agent/src/policy.ts was not found above the test binaries.");
    }

    [GeneratedRegex(@"knownReadTools\s*=\s*\[(?<tools>.*?)\]", RegexOptions.Singleline)]
    private static partial Regex KnownReadTools();

    [GeneratedRegex(@"'([a-z_]+)'")]
    private static partial Regex Quoted();
}
