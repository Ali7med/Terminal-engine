using System.Linq;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Scan;

namespace Terminal.Servers.Tests;

public class ContainerFilesTests
{
    [Fact]
    public void BuildList_UsesExecLsWithQuotedPathAndStopParsing()
    {
        string cmd = ContainerFiles.BuildList("abc123", "/var/www");
        Assert.Contains("docker exec abc123 ls -1Ap -- '/var/www'", cmd);
        Assert.DoesNotContain("sudo", cmd);
    }

    [Fact]
    public void BuildList_Sudo_PrefixesNonInteractiveSudo()
    {
        string cmd = ContainerFiles.BuildList("abc123", "/", sudo: true);
        Assert.StartsWith("sudo -n docker exec abc123 ls", cmd);
    }

    [Fact]
    public void Parse_DirsFirstThenFilesAlphabetical_TrailingSlashMarksDir()
    {
        const string outp = "zeta.txt\nbin/\nalpha.log\netc/\n";
        var e = ContainerFiles.Parse(outp);

        Assert.Equal(4, e.Count);
        Assert.Equal("bin", e[0].Name);   Assert.True(e[0].IsDir);
        Assert.Equal("etc", e[1].Name);   Assert.True(e[1].IsDir);
        Assert.Equal("alpha.log", e[2].Name); Assert.False(e[2].IsDir);
        Assert.Equal("zeta.txt", e[3].Name);  Assert.False(e[3].IsDir);
    }

    [Fact]
    public void Parse_SkipsDotEntriesAndBlanks()
        => Assert.Empty(ContainerFiles.Parse("./\n../\n\n"));

    [Fact]
    public void BuildRead_UsesHeadWithByteCapAndQuotedPath()
    {
        string cmd = ContainerFiles.BuildRead("abc123", "/etc/hosts");
        Assert.Contains("docker exec abc123 head -c 524288 -- '/etc/hosts'", cmd);
    }

    [Fact]
    public void BuildRead_Sudo_PrefixesNonInteractiveSudo()
        => Assert.StartsWith("sudo -n docker exec abc head -c", ContainerFiles.BuildRead("abc", "/f", sudo: true));

    [Fact]
    public void BuildCat_StreamsRawWithoutStderrMerge()
    {
        string cmd = ContainerFiles.BuildCat("abc123", "/app/image.png");
        Assert.Equal("docker exec abc123 cat -- '/app/image.png'", cmd);
        Assert.DoesNotContain("2>&1", cmd);
    }

    [Fact]
    public void BuildCat_Sudo_PrefixesNonInteractiveSudo()
        => Assert.StartsWith("sudo -n docker exec abc cat --", ContainerFiles.BuildCat("abc", "/f", sudo: true));

    [Theory]
    [InlineData("plain text\nline two", false)]
    [InlineData("has\0null byte", true)]
    public void LooksBinary_DetectsNulByte(string content, bool expected)
        => Assert.Equal(expected, ContainerFiles.LooksBinary(content));

    [Theory]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/var/www/", "/var/www")]
    [InlineData("  /etc  ", "/etc")]
    public void NormalizePath_TrimsAndDefaultsToRoot(string input, string expected)
        => Assert.Equal(expected, ContainerFiles.NormalizePath(input));

    [Theory]
    [InlineData("/", "app", "/app")]
    [InlineData("/var", "www", "/var/www")]
    [InlineData("/var/", "www", "/var/www")]
    public void Join_HandlesRootAndTrailingSlash(string parent, string child, string expected)
        => Assert.Equal(expected, ContainerFiles.Join(parent, child));

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/var", "/")]
    [InlineData("/var/www/html", "/var/www")]
    public void Parent_ClimbsOneLevel(string path, string expected)
        => Assert.Equal(expected, ContainerFiles.Parent(path));

    [Fact]
    public async Task ListAsync_ThrowsWhenExecFails()
    {
        var fake = new FakeSsh(_ => new CommandResult(1, "Error: No such container", ""));
        var files = new ContainerFiles(fake, "gone");
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => files.ListAsync("/"));
    }

    [Fact]
    public void BuildDelete_FileVsFolder()
    {
        Assert.Contains("docker exec c rm -f -- '/a.txt'", ContainerFiles.BuildDelete("c", "/a.txt", recursive: false));
        Assert.Contains("docker exec c rm -rf -- '/dir'", ContainerFiles.BuildDelete("c", "/dir", recursive: true));
    }

    [Fact]
    public void BuildMkdir_UsesMkdirP()
        => Assert.Contains("docker exec c mkdir -p -- '/new'", ContainerFiles.BuildMkdir("c", "/new"));

    [Fact]
    public void BuildRename_UsesMvWithBothQuoted()
        => Assert.Contains("docker exec c mv -- '/a' '/b'", ContainerFiles.BuildRename("c", "/a", "/b"));

    [Fact]
    public void BuildCopyIn_QuotesContainerDestWithColon()
    {
        string cmd = ContainerFiles.BuildCopyIn("c", "/tmp/x", "/app/dest");
        Assert.Contains("docker cp '/tmp/x' 'c:/app/dest'", cmd);
    }

    [Fact]
    public void WriteBuilders_Sudo_PrefixesNonInteractiveSudo()
    {
        Assert.StartsWith("sudo -n docker exec c rm", ContainerFiles.BuildDelete("c", "/a", false, sudo: true));
        Assert.StartsWith("sudo -n docker cp", ContainerFiles.BuildCopyIn("c", "/h", "/d", sudo: true));
    }

    [Fact]
    public async Task DeleteAsync_ThrowsOnFailure()
    {
        var fake = new FakeSsh(_ => new CommandResult(1, "", "Permission denied"));
        await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => new ContainerFiles(fake, "c").DeleteAsync("/x", recursive: false));
    }
}
