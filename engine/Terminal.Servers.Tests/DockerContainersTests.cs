using System.Linq;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Scan;

namespace Terminal.Servers.Tests;

public class DockerContainersTests
{
    [Fact]
    public void BuildList_ListsAllContainersWithPipeFormat()
    {
        string cmd = DockerContainers.BuildList();
        Assert.Contains("docker ps -a", cmd);
        Assert.Contains("{{.ID}}|{{.Names}}|{{.State}}|{{.Status}}|{{.Image}}", cmd);
    }

    [Fact]
    public void BuildList_Sudo_PrefixesNonInteractiveSudo()
        => Assert.StartsWith("sudo -n docker ps -a", DockerContainers.BuildList(sudo: true));

    [Fact]
    public void Parse_ReadsFieldsAndRunningState()
    {
        const string outp =
            "a1b2c3d4e5f6a1b2c3d4|web|running|Up 3 hours|laravel:latest\n" +
            "0011223344556677|db|exited|Exited (0) 2 days ago|postgres:16\n";

        var list = DockerContainers.Parse(outp);

        Assert.Equal(2, list.Count);
        Assert.Equal("web", list[0].Name);
        Assert.Equal("a1b2c3d4e5f6", list[0].ShortId);   // 12 محرفاً
        Assert.True(list[0].Running);
        Assert.Equal("db", list[1].Name);
        Assert.False(list[1].Running);
    }

    [Fact]
    public void Parse_TakesFirstNameForMultiNamedContainer()
    {
        const string outp = "id01|primary,secondary|running|Up|img\n";
        var c = Assert.Single(DockerContainers.Parse(outp));
        Assert.Equal("primary", c.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("id|name|running\n")]   // حقول ناقصة تُتخطّى
    public void Parse_SkipsBlankOrShortLines(string outp)
        => Assert.Empty(DockerContainers.Parse(outp));

    [Fact]
    public async Task ListAsync_RunsBuildListAndParses()
    {
        const string outp = "deadbeefcafe0000|api|running|Up 5m|node:20\n";
        var fake = new FakeSsh(_ => outp);

        var list = await new DockerContainers(fake).ListAsync();

        Assert.Equal("api", list.Single().Name);
        Assert.Contains("docker ps -a", fake.LastCommand);
    }
}
