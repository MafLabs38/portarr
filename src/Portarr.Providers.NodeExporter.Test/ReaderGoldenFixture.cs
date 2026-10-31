using Portarr.Core.Infrastructure;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary>
/// What the Portarr reader writes for the machine (D-100, D-102) is read by Portarr's own node_exporter sampler. The reader is a Rust
/// program: its tests write the host text for two fixed kernel states into <c>reader/tests/fixtures/golden-host-*.txt</c>, and this reads
/// the same files, so the format both sides agree on is held by one pair of files and the live infrastructure page keeps its values (D-72).
/// </summary>
public sealed class ReaderGoldenFixture
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_give_the_readings_node_exporter_would_give_for_the_readers_text()
    {
        var sampler = new NodeExporterSampler();
        var key = new HostMachineKey("server-1", "server-1");

        var first = await sampler.ReadAsync(key, Golden("golden-host-1.txt"), Token);
        // One second later cpu0 spent 50 of 100 ticks idle and cpu1 all of its 100: a quarter of the machine was busy.
        var second = await sampler.ReadAsync(key, Golden("golden-host-2.txt"), Token);

        first.CpuPercent.Should().BeNull("a load is only defined between two reads");
        second.Cores.Should().Be(2);
        second.CpuPercent.Should().Be(25);
        second.CorePercents.Should().Equal(50, 0);
        second.Memory.TotalBytes.Should().Be(16000L * 1024);
        second.Memory.UsedBytes.Should().Be((16000L - 8000) * 1024, "used = MemTotal - MemAvailable");
        second.Memory.SwapUsedBytes.Should().Be(100L * 1024);
        second.Load1.Should().Be(0.5);
        second.TemperatureCelsius.Should().Be(45);
    }

    private static FileStream Golden(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "reader", "tests", "fixtures")))
        {
            directory = directory.Parent;
        }

        return File.OpenRead(Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("reader/tests/fixtures was not found above the test assembly."), "reader", "tests", "fixtures", name));
    }
}
