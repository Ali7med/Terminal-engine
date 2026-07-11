using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Scan;

namespace Terminal.Servers.Tests;

public class FileOperationsTests
{
    [Fact]
    public void BuildDelete_QuotesAndStopsOptionParsing()
        => Assert.Equal("rm -f -- '/var/log/app.log'", FileOperations.BuildDelete("/var/log/app.log"));

    [Fact]
    public void BuildDeleteFolder_UsesRecursiveForce()
        => Assert.Equal("rm -rf -- '/var/tmp/cache'", FileOperations.BuildDeleteFolder("/var/tmp/cache"));

    [Fact]
    public void BuildMkdir_UsesParents()
        => Assert.Equal("mkdir -p -- '/var/www/new'", FileOperations.BuildMkdir("/var/www/new"));

    [Fact]
    public void BuildRename_QuotesBothPaths()
        => Assert.Equal("mv -- '/a/old.txt' '/a/new.txt'",
            FileOperations.BuildRename("/a/old.txt", "/a/new.txt"));

    [Fact]
    public void BuildTail_ClampsLinesAndQuotes()
    {
        Assert.Equal("tail -n 500 -- '/var/log/syslog' 2>/dev/null", FileOperations.BuildTail("/var/log/syslog", 500));
        Assert.Equal("tail -n 1 -- '/x' 2>/dev/null", FileOperations.BuildTail("/x", 0));
    }

    [Fact]
    public async Task DeleteAsync_SendsCommand_OnSuccess()
    {
        var fake = new FakeSsh(_ => "");
        var ops = new FileOperations(fake);

        await ops.DeleteAsync("/tmp/x");

        Assert.Equal("rm -f -- '/tmp/x'", fake.LastCommand);
    }

    [Fact]
    public async Task DeleteAsync_Throws_OnNonZeroExit()
    {
        var fake = new FakeSsh(_ => new CommandResult(1, "", "Permission denied"));
        var ops = new FileOperations(fake);

        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(() => ops.DeleteAsync("/root/x"));
        Assert.Contains("Permission denied", ex.Message);
    }

    [Fact]
    public async Task TailAsync_ReturnsStdout()
    {
        var fake = new FakeSsh(_ => "line1\nline2\n");
        var ops = new FileOperations(fake);

        string log = await ops.TailAsync("/var/log/syslog", 100);

        Assert.Contains("line2", log);
    }
}
