namespace Portarr.Providers.Kubernetes.Test;

public sealed class KubernetesQuantityFixture
{
    [Theory]
    [InlineData("4", 4)]
    [InlineData("250m", 0.25)]
    [InlineData("40122367n", 0.040122367)]
    [InlineData("1500u", 0.0015)]
    [InlineData("1.5", 1.5)]
    [InlineData("1238720Ki", 1_268_449_280)]
    [InlineData("512Mi", 536_870_912)]
    [InlineData("2Gi", 2_147_483_648)]
    [InlineData("2G", 2_000_000_000)]
    [InlineData("1e3", 1000)]
    [InlineData("128974848", 128_974_848)]
    public void should_parse_cpu_and_memory_quantities(string text, double expected)
    {
        KubernetesQuantity.TryParse(text, out var quantity).Should().BeTrue();
        ((double)quantity).Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ki")]
    [InlineData("-1")]
    [InlineData("four")]
    [InlineData(null)]
    public void should_reject_what_is_not_a_quantity(string? text)
    {
        KubernetesQuantity.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void should_read_cores_and_bytes_from_a_resource_list()
    {
        var resources = new Dictionary<string, string> { ["cpu"] = "1500m", ["memory"] = "1Gi" };

        KubernetesQuantity.Cores(resources).Should().Be(1.5);
        KubernetesQuantity.Bytes(resources).Should().Be(1_073_741_824);
        KubernetesQuantity.Cores(null).Should().BeNull();
    }
}
