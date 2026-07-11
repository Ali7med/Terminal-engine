using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Scan;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Tests;

/// <summary>اتّصال SSH وهميّ: يُعيد نتيجة معدّة لكلّ أمر (بلا شبكة) ويلتقط آخر أمر نُفِّذ.</summary>
internal sealed class FakeSsh : ISshConnection
{
    private readonly Func<string, CommandResult> _respond;

    /// <summary>يُعيد الخرج المعطى ورمز خروج 0 لأيّ أمر.</summary>
    public FakeSsh(Func<string, string> respond)
        => _respond = cmd => new CommandResult(0, respond(cmd), string.Empty);

    /// <summary>تحكّم كامل بالنتيجة (رمز الخروج/الخطأ) لكلّ أمر.</summary>
    public FakeSsh(Func<string, CommandResult> respond) => _respond = respond;

    public string? LastCommand { get; private set; }
    public bool IsConnected => true;
    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<CommandResult> RunAsync(string command, CancellationToken ct = default)
    {
        LastCommand = command;
        return Task.FromResult(_respond(command));
    }
    public void Disconnect() { }
    public void Dispose() { }
}

public class StorageScannerTests
{
    [Fact]
    public async Task ScanSubfolders_RemovesRootLine_KeepsChildrenSortedDesc()
    {
        // du -k --max-depth=1 /var  → children + the /var total line
        const string du =
            "40960\t/var/log\n" +
            "10240\t/var/cache\n" +
            "102400\t/var\n" +          // سطر الجذر نفسه
            "2048\t/var/tmp\n";

        var scanner = new StorageScanner(new FakeSsh(_ => du));
        var scan = await scanner.ScanSubfoldersAsync("/var/");

        Assert.Equal("/var", scan.Path);                 // طُبّع (بلا شرطة أخيرة)
        Assert.Equal(102400L * 1024, scan.TotalBytes);   // من سطر الجذر
        Assert.Equal(3, scan.Children.Count);            // بلا الجذر
        Assert.Equal("/var/log", scan.Children[0].Path); // الأكبر أوّلاً
        Assert.DoesNotContain(scan.Children, c => c.Path == "/var");
    }

    [Fact]
    public async Task QuickScanDisks_ParsesDf()
    {
        const string df =
            "Filesystem 1024-blocks Used Available Capacity Mounted on\n" +
            "/dev/sda1 1000 400 600 40% /\n";
        var scanner = new StorageScanner(new FakeSsh(_ => df));

        var disks = await scanner.QuickScanDisksAsync();

        Assert.Single(disks);
        Assert.Equal("/", disks[0].MountPoint);
        Assert.Equal(40, disks[0].UsePercent);
    }

    [Fact]
    public async Task ListFiles_UsesMaxDepthAndParses()
    {
        const string find = "300\t2026-02-02T10:00\t/etc/hosts\n200\t2026-02-01T09:00\t/etc/fstab\n";
        var fake = new FakeSsh(_ => find);
        var scanner = new StorageScanner(fake);

        var files = await scanner.ListFilesAsync("/etc");

        Assert.Contains("-maxdepth 1", fake.LastCommand);
        Assert.Contains("'/etc'", fake.LastCommand);
        Assert.Equal(2, files.Count);
        Assert.Equal("hosts", files[0].Name);
    }

    [Fact]
    public async Task LargestFiles_ParsesFindOutput()
    {
        const string find = "5242880\t2026-01-01T00:00\t/a/big.bin\n";
        var scanner = new StorageScanner(new FakeSsh(_ => find));

        var files = await scanner.LargestFilesAsync("/a", 10);

        Assert.Single(files);
        Assert.Equal("big.bin", files[0].Name);
        Assert.Equal(5242880L, files[0].SizeBytes);
    }
}
