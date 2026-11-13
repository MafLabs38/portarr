using Portarr.Core.Platforms;

namespace Portarr.Core.Test.Platforms;

public sealed class KubernetesVersionFixture
{
    [Theory]
    [InlineData("v1.31.4+k3s1", 1, 31, 4, false, "k3s", 1, "1.31")]
    [InlineData("1.31.4", 1, 31, 4, false, null, 0, "1.31")]
    [InlineData("v1.32.0-rc1+k3s2", 1, 32, 0, true, "k3s", 2, "1.32")]
    [InlineData("v1.30.7+rke2r1", 1, 30, 7, false, "rke2r1", 0, "1.30")]
    [InlineData(" v1.29.15+k3s1 ", 1, 29, 15, false, "k3s", 1, "1.29")]
    public void should_read_a_kubelet_version(string text, int major, int minor, int patch, bool preRelease, string? distribution, int build, string line)
    {
        var version = KubernetesVersion.Parse(text)!;

        version.Should().BeEquivalentTo(new { Major = major, Minor = minor, Patch = patch, PreRelease = preRelease, Distribution = distribution, Build = build, Line = line });
        version.IsK3s.Should().Be(distribution == "k3s");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.31")]
    [InlineData("v1.31.4+")]
    [InlineData("v1.31.4 extra")]
    [InlineData("v1.31.99999")]
    public void should_not_read_what_is_not_a_version(string? text)
    {
        KubernetesVersion.Parse(text).Should().BeNull();
    }

    [Theory]
    [InlineData("v1.31.4+k3s1", "v1.31.5+k3s1")]
    [InlineData("v1.31.9+k3s1", "v1.32.0+k3s1")]
    [InlineData("v1.31.4+k3s1", "v1.31.4+k3s2")]
    [InlineData("v1.31.4-rc1+k3s1", "v1.31.4+k3s1")]
    [InlineData("v1.9.0+k3s1", "v1.10.0+k3s1")]
    public void should_order_versions_as_kubernetes_does(string older, string newer)
    {
        var (a, b) = (KubernetesVersion.Parse(older)!, KubernetesVersion.Parse(newer)!);

        (a < b).Should().BeTrue();
        (b > a).Should().BeTrue();
        a.CompareTo(b).Should().BeNegative();
        b.CompareTo(a).Should().BePositive();
        (a <= b).Should().BeTrue();
        (b >= a).Should().BeTrue();
    }
}
