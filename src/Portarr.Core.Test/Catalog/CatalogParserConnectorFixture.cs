using Portarr.Core.Catalog;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

/// <summary>The machinery that gives a catalog section to the connector that owns it (D-105), tested with connectors of the tests.</summary>
public sealed class CatalogParserConnectorFixture
{
    private sealed class RecordingConnector : IEnvironmentConnector
    {
        private readonly Func<EnvironmentSectionContext, EnvironmentConnectorResult?>? _parse;

        public RecordingConnector(string section, Func<EnvironmentSectionContext, EnvironmentConnectorResult?>? parse = null)
        {
            Section = section;
            _parse = parse;
        }

        public string Section { get; }

        public List<bool> SingleMachineSeen { get; } = [];

        public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
        {
            SingleMachineSeen.Add(context.SingleMachine);
            return _parse is null ? new EnvironmentConnectorResult(new object()) : _parse(context);
        }
    }

    [Fact]
    public void should_give_each_section_to_its_own_connector()
    {
        var parsed = CatalogParser.Parse("environments:\n  - { id: server-1, alpha: {}, beta: {} }\n", [new StubEnvironmentConnector("alpha"), new StubEnvironmentConnector("beta")]);

        parsed.Errors.Should().BeEmpty();
        var environment = parsed.Catalog.Environments.Single();
        environment.Declares("alpha").Should().BeTrue();
        environment.Declares("beta").Should().BeTrue();
        environment.Declares("gamma").Should().BeFalse();
    }

    [Fact]
    public void should_refuse_a_section_nobody_owns_naming_its_line()
    {
        var parsed = CatalogParser.Parse("environments:\n  - id: server-1\n    alpha: {}\n", [new StubEnvironmentConnector("beta")]);

        parsed.Errors.Should().ContainSingle().Which.Should().StartWith("Invalid YAML at line 3").And.Contain("alpha");
    }

    [Fact]
    public void should_refuse_a_section_declared_twice()
    {
        var parsed = CatalogParser.Parse("environments:\n  - { id: server-1, alpha: { a: 1 }, alpha: { a: 2 } }\n", [new StubEnvironmentConnector("alpha")]);

        parsed.Errors.Should().ContainSingle().Which.Should().StartWith("Invalid YAML at line 2").And.Contain("declared twice");
    }

    [Fact]
    public void should_refuse_a_key_that_is_not_a_plain_word()
    {
        var parsed = CatalogParser.Parse("environments:\n  - { id: server-1, [a, b]: 1 }\n", [new StubEnvironmentConnector("alpha")]);

        parsed.Errors.Should().ContainSingle().Which.Should().StartWith("Invalid YAML at line 2").And.Contain("plain word");
    }

    [Fact]
    public void should_leave_the_environment_undeclared_when_the_connector_makes_nothing_of_the_section()
    {
        var parsed = CatalogParser.Parse("environments:\n  - { id: server-1, alpha: {} }\n", [new RecordingConnector("alpha", _ => null)]);

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments.Single().Declares("alpha").Should().BeFalse();
    }

    [Fact]
    public void should_prefix_a_connector_error_with_its_environment()
    {
        var connector = new RecordingConnector("alpha", context =>
        {
            context.Error("alpha.size must be a number.");
            return null;
        });

        var parsed = CatalogParser.Parse("environments:\n  - { id: server-1, alpha: {} }\n", [connector]);

        parsed.Errors.Should().Equal("Environment 'server-1': alpha.size must be a number.");
    }

    [Fact]
    public void should_tell_a_connector_whether_the_environment_is_one_machine()
    {
        var connector = new RecordingConnector("alpha");

        var parsed = CatalogParser.Parse("""
            environments:
              - { id: host, docker: { url: "http://10.0.0.10:2375" }, alpha: {} }
              - { id: bare, alpha: {} }
              - { id: cluster, kubernetes: { server: "https://10.0.0.20:6443" }, alpha: {} }
              - { id: edge, cloudflare: { accountId: abc123 }, alpha: {} }
            """, [connector]);

        parsed.Errors.Should().BeEmpty();
        connector.SingleMachineSeen.Should().Equal(true, true, false, false);
    }

    [Fact]
    public void should_check_the_hosts_a_connector_gives_like_any_other_host()
    {
        var connector = new RecordingConnector("alpha", _ => new EnvironmentConnectorResult(new object(), new Dictionary<string, string> { ["vm-1"] = "hypervisor" }));

        var known = CatalogParser.Parse("environments:\n  - { id: hypervisor, docker: { url: \"http://10.0.0.10:2375\" } }\n  - { id: cluster, alpha: {} }\n", [connector]);
        var unknown = CatalogParser.Parse("environments:\n  - { id: cluster, alpha: {} }\n", [connector]);

        known.Errors.Should().BeEmpty();
        known.Catalog.Environments.Single(e => e.Id == "cluster").HostOf("vm-1").Should().Be("hypervisor");
        unknown.Errors.Should().ContainSingle().Which.Should().Contain("host 'hypervisor' is not an environment of the catalog");
    }

    [Fact]
    public void should_refuse_two_sections_that_give_one_machine_two_hosts()
    {
        var alpha = new RecordingConnector("alpha", _ => new EnvironmentConnectorResult(new object(), new Dictionary<string, string> { ["vm-1"] = "one" }));
        var beta = new RecordingConnector("beta", _ => new EnvironmentConnectorResult(new object(), new Dictionary<string, string> { ["vm-1"] = "two" }));

        var parsed = CatalogParser.Parse("environments:\n  - { id: one, docker: { url: \"http://10.0.0.10:2375\" } }\n  - { id: two, docker: { url: \"http://10.0.0.11:2375\" } }\n  - { id: cluster, alpha: {}, beta: {} }\n", [alpha, beta]);

        parsed.Errors.Should().ContainSingle().Which.Should().Contain("machine 'vm-1' is given the hosts 'one' and 'two'");
    }

    [Fact]
    public void should_read_the_same_catalog_whatever_the_order_the_connectors_are_registered_in()
    {
        const string Yaml = "environments:\n  - { id: server-1, alpha: {}, beta: {} }\n";

        var one = CatalogParser.Parse(Yaml, [new StubEnvironmentConnector("alpha"), new StubEnvironmentConnector("beta")]);
        var other = CatalogParser.Parse(Yaml, [new StubEnvironmentConnector("beta"), new StubEnvironmentConnector("alpha")]);

        one.Errors.Should().BeEmpty();
        other.Errors.Should().BeEmpty();
        other.Catalog.Environments.Single().Connectors!.Keys.Should().BeEquivalentTo(one.Catalog.Environments.Single().Connectors!.Keys);
    }

    [Fact]
    public void should_not_accept_two_connectors_for_one_section()
    {
        var register = () => CatalogParser.Parse("environments: []\n", [new StubEnvironmentConnector("alpha"), new StubEnvironmentConnector("alpha")]);

        register.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("'alpha' is owned by two connectors");
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("kubernetes")]
    [InlineData("id")]
    [InlineData("notes")]
    public void should_not_let_a_connector_take_a_key_core_still_reads(string section)
    {
        var register = () => new CatalogParserService([new StubEnvironmentConnector(section)]);

        register.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain($"'{section}', which Core still reads itself");
    }

    [Fact]
    public void should_not_hand_back_settings_of_another_type()
    {
        var parsed = CatalogParser.Parse("environments:\n  - { id: server-1, alpha: {} }\n", [new StubEnvironmentConnector("alpha")]);
        var environment = parsed.Catalog.Environments.Single();

        var read = () => environment.Settings<string>("alpha");

        environment.Settings<object>("alpha").Should().NotBeNull();
        environment.Settings<string>("beta").Should().BeNull();
        read.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("'alpha' in environment 'server-1'");
    }
}
