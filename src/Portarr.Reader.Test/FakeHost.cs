namespace Portarr.Reader.Test;

/// <summary>A fake <c>/proc</c> and <c>/sys</c> on disk, in a folder of its own, deleted with the object.</summary>
internal sealed class FakeHost : IDisposable
{
    public FakeHost()
    {
        Root = Path.Combine(Path.GetTempPath(), "portarr-reader-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Proc);
        Directory.CreateDirectory(Path.Combine(Sys, "class", "hwmon", "hwmon0"));
        Directory.CreateDirectory(Path.Combine(Sys, "class", "hwmon", "hwmon1"));
        File.WriteAllText(Path.Combine(Sys, "class", "hwmon", "hwmon0", "name"), "coretemp\n");
        File.WriteAllText(Path.Combine(Sys, "class", "hwmon", "hwmon0", "temp1_input"), "45000\n");
        File.WriteAllText(Path.Combine(Sys, "class", "hwmon", "hwmon1", "name"), "nvme\n");
        File.WriteAllText(Path.Combine(Sys, "class", "hwmon", "hwmon1", "temp1_input"), "99000\n");
        Write(cpu0: (150, 300), cpu1: (150, 300));
    }

    public string Root { get; }

    public string Proc => Path.Combine(Root, "proc");

    public string Sys => Path.Combine(Root, "sys");

    /// <summary>Writes the kernel files: each cpu as (user ticks, idle ticks).</summary>
    public void Write((long User, long Idle) cpu0, (long User, long Idle) cpu1)
    {
        File.WriteAllText(
            Path.Combine(Proc, "stat"),
            $"cpu  {cpu0.User + cpu1.User} 0 100 {cpu0.Idle + cpu1.Idle} 0 0 0 0 0 0\n"
            + $"cpu0 {cpu0.User} 0 50 {cpu0.Idle} 0 0 0 0 0 0\n"
            + $"cpu1 {cpu1.User} 0 50 {cpu1.Idle} 0 0 0 0 0 0\n"
            + "intr 12345\nctxt 999\nbtime 1700000000\n");
        File.WriteAllText(
            Path.Combine(Proc, "meminfo"),
            "MemTotal:       16000 kB\nMemFree:         4000 kB\nMemAvailable:    8000 kB\nBuffers:          100 kB\nCached:          2000 kB\n"
            + "SReclaimable:     300 kB\nSwapTotal:       1000 kB\nSwapFree:         900 kB\nShmem:           5000 kB\nHugePages_Total:    0\n");
        File.WriteAllText(Path.Combine(Proc, "loadavg"), "0.50 0.40 0.30 1/200 12345\n");
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }
}
