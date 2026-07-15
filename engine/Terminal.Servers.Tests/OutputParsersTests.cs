using System.Linq;
using Terminal.Servers.Parsing;
using Terminal.Servers.Scan;

namespace Terminal.Servers.Tests;

public class OutputParsersTests
{
    [Fact]
    public void ParseMemory_ReadsMemAndSwap_ComputesRealUsedFromAvailable()
    {
        const string free =
            "              total        used        free      shared  buff/cache   available\n" +
            "Mem:       16384000    12000000      500000      200000     3884000     2400000\n" +
            "Swap:       2097152      524288     1572864\n";

        var m = OutputParsers.ParseMemory(free);

        Assert.Equal(16384000, m.TotalKb);
        Assert.Equal(3884000, m.BuffCacheKb);
        Assert.Equal(2400000, m.AvailableKb);
        Assert.Equal(16384000 - 2400000, m.RealUsedKb);       // الحقيقيّ = الإجماليّ − المتاح (لا عمود used)
        Assert.Equal(85.4, m.UsedPercent, 1);
        Assert.Equal(524288, m.SwapUsedKb);
        Assert.Equal(25.0, m.SwapPercent, 1);
    }

    [Fact]
    public void ParseMemory_MissingAvailableColumn_EstimatesFromFreePlusCache()
    {
        // busybox/إصدار قديم: بلا عمود available
        const string free =
            "             total       used       free     shared    buffers\n" +
            "Mem:       2048000     900000     148000      10000     1000000\n";
        var m = OutputParsers.ParseMemory(free);
        Assert.Equal(148000 + 1000000, m.AvailableKb);
    }

    [Fact]
    public void ParsePsMem_ReadsRssAndSkipsHeader()
    {
        const string ps =
            "  PID USER        RSS %MEM COMMAND\n" +
            " 1234 postgres 1048576  6.4 postgres: writer\n" +
            "  987 root      524288  3.2 /usr/bin/dockerd\n";
        var list = OutputParsers.ParsePsMem(ps);
        Assert.Equal(2, list.Count);
        Assert.Equal(1234, list[0].Pid);
        Assert.Equal(1048576, list[0].RssKb);
        Assert.Equal(6.4, list[0].MemPercent, 1);
        Assert.Equal("postgres: writer", list[0].Command);
    }

    [Fact]
    public void ParseDf_ReadsRowsAndConvertsToBytes_SkipsHeader()
    {
        const string df =
            "Filesystem     1024-blocks     Used Available Capacity Mounted on\n" +
            "/dev/sda1         41152736 12345678  26707058      32% /\n" +
            "/dev/sdb1        102400000 51200000  51200000      50% /data\n";

        var disks = OutputParsers.ParseDf(df);

        Assert.Equal(2, disks.Count);
        Assert.Equal("/dev/sda1", disks[0].Filesystem);
        Assert.Equal("/", disks[0].MountPoint);
        Assert.Equal(41152736L * 1024, disks[0].TotalBytes);
        Assert.Equal(12345678L * 1024, disks[0].UsedBytes);
        Assert.Equal(32, disks[0].UsePercent);
        Assert.Equal("/data", disks[1].MountPoint);
    }

    [Fact]
    public void ParseDf_HandlesMountPathWithSpaces()
    {
        const string df =
            "Filesystem 1024-blocks Used Available Capacity Mounted on\n" +
            "/dev/sdc1 1000 500 500 50% /mnt/my disk\n";

        var disks = OutputParsers.ParseDf(df);

        Assert.Single(disks);
        Assert.Equal("/mnt/my disk", disks[0].MountPoint);
    }

    [Fact]
    public void ParseDu_SortsDescendingAndConvertsKbToBytes()
    {
        const string du =
            "1024\t/var/log\n" +
            "4096\t/var/www\n" +
            "512\t/var/tmp\n";

        var dirs = OutputParsers.ParseDu(du);

        Assert.Equal(3, dirs.Count);
        Assert.Equal("/var/www", dirs[0].Path);      // الأكبر أوّلاً
        Assert.Equal(4096L * 1024, dirs[0].SizeBytes);
        Assert.Equal("/var/tmp", dirs[2].Path);
    }

    [Fact]
    public void ParseFindFiles_ReadsSizeDateAndName()
    {
        const string find =
            "104857600\t2026-07-01T13:45\t/var/log/big.log\n" +
            "2048\t2025-12-31T09:00\t/home/user/notes.txt\n";

        var files = OutputParsers.ParseFindFiles(find);

        Assert.Equal(2, files.Count);
        Assert.Equal(104857600L, files[0].SizeBytes);
        Assert.Equal("big.log", files[0].Name);
        Assert.Equal("/var/log/big.log", files[0].Path);
        Assert.NotNull(files[0].Modified);
        Assert.Equal("notes.txt", files[1].Name);
    }

    [Fact]
    public void ParseLoadAvg_ReadsFirstThreeValues()
    {
        var (l1, l5, l15) = OutputParsers.ParseLoadAvg("0.15 0.30 0.45 2/512 9876");
        Assert.Equal(0.15, l1);
        Assert.Equal(0.30, l5);
        Assert.Equal(0.45, l15);
    }

    [Fact]
    public void ParseFree_ReadsMemLine()
    {
        const string free =
            "               total        used        free      shared  buff/cache   available\n" +
            "Mem:        16384000     8192000     4096000      100000     4096000     7900000\n" +
            "Swap:        2048000           0     2048000\n";

        var (total, used, freeKb) = OutputParsers.ParseFree(free);

        Assert.Equal(16384000, total);
        Assert.Equal(8192000, used);
        Assert.Equal(4096000, freeKb);
    }

    [Fact]
    public void ParsePs_SkipsHeaderAndReadsProcesses()
    {
        const string ps =
            "  PID USER     %CPU %MEM COMMAND\n" +
            " 1234 root     55.0  3.2 mysqld\n" +
            " 5678 www-data 12.5  1.1 nginx\n";

        var procs = OutputParsers.ParsePs(ps);

        Assert.Equal(2, procs.Count);
        Assert.Equal(1234, procs[0].Pid);
        Assert.Equal("root", procs[0].User);
        Assert.Equal(55.0, procs[0].CpuPercent);
        Assert.Equal("mysqld", procs[0].Command);
    }

    [Fact]
    public void PerfMonitor_ParseSnapshot_SplitsMarkedSectionsAndComputesMemPercent()
    {
        string combined =
            "0.10 0.20 0.30 1/200 4242\n" +
            "===FREE===\n" +
            "               total        used        free\n" +
            "Mem:            1000         250         750\n" +
            "===UP===\n" +
            "up 3 days, 4 hours\n" +
            "===PS===\n" +
            "  PID USER %CPU %MEM COMMAND\n" +
            " 42 root 9.0 1.0 sshd\n";

        var snap = PerfMonitor.ParseSnapshot(combined);

        Assert.Equal(0.10, snap.LoadAvg1);
        Assert.Equal(1000, snap.MemTotalKb);
        Assert.Equal(250, snap.MemUsedKb);
        Assert.Equal(25.0, snap.MemUsedPercent);
        Assert.Equal("up 3 days, 4 hours", snap.Uptime);
        Assert.Single(snap.TopProcesses);
        Assert.Equal("sshd", snap.TopProcesses[0].Command);
    }

    [Fact]
    public void ShellQuote_EscapesSingleQuotes()
    {
        Assert.Equal("'/var/log'", StorageScanner.ShellQuote("/var/log"));
        Assert.Equal("'it'\\''s'", StorageScanner.ShellQuote("it's"));
    }
}
