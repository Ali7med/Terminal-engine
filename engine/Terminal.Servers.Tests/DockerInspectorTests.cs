using System.Linq;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Scan;

namespace Terminal.Servers.Tests;

public class DockerInspectorTests
{
    [Theory]
    [InlineData("/var/lib/docker/overlay2/d0fdcfc82eea/diff/var/www/html/storage/logs/laravel.log", DockerPathKind.Overlay, "d0fdcfc82eea")]
    [InlineData("/var/lib/docker/overlay2/abc123/merged/app.log", DockerPathKind.Overlay, "abc123")]
    [InlineData("/var/lib/docker/volumes/pg-data/_data/base/1.db", DockerPathKind.Volume, "pg-data")]
    [InlineData("/home/user/notes.txt", DockerPathKind.None, "")]
    [InlineData("/var/log/syslog", DockerPathKind.None, "")]
    public void Classify_ExtractsLayerOrVolumeId(string path, DockerPathKind kind, string key)
    {
        var (k, id) = DockerInspector.Classify(path);
        Assert.Equal(kind, k);
        Assert.Equal(key, id);
    }

    [Fact]
    public void BuildOverlayLookup_GrepsForIdSafely()
    {
        string cmd = DockerInspector.BuildOverlayLookup("d0fdcfc82eea");
        Assert.Contains("docker inspect", cmd);
        Assert.Contains(".GraphDriver.Data.UpperDir", cmd);
        Assert.Contains("grep -F -- 'd0fdcfc82eea'", cmd);
    }

    [Fact]
    public void Parse_Overlay_MarksWritableLayerWhenIdInUpperDir()
    {
        // Name|Status|Image|coolify|compose|UpperDir|MergedDir
        const string line =
            "/app-web-123|running|laravel:latest|my-store|my-store|" +
            "/var/lib/docker/overlay2/d0fdcfc82eea/diff|/var/lib/docker/overlay2/d0fdcfc82eea/merged\n";

        var lookup = DockerInspector.Parse(DockerPathKind.Overlay, "d0fdcfc82eea", line);

        var m = Assert.Single(lookup.Matches);
        Assert.Equal("app-web-123", m.Name);          // شرطة البداية أُزيلت
        Assert.Equal("running", m.Status);
        Assert.Equal("my-store", m.CoolifyName);
        Assert.True(m.WritableLayer);                  // المعرّف في UpperDir → مالك حصريّ
    }

    [Fact]
    public void Parse_Volume_YieldsMountingContainersNotWritable()
    {
        const string line = "/db|running|postgres:16|store-db||pg-data other-vol\n";

        var lookup = DockerInspector.Parse(DockerPathKind.Volume, "pg-data", line);

        var m = Assert.Single(lookup.Matches);
        Assert.Equal("db", m.Name);
        Assert.False(m.WritableLayer);                 // حجم مُركَّب — ليس طبقة كتابة
    }

    [Fact]
    public async Task ResolveAsync_NonDockerPath_ReturnsNoneWithoutSsh()
    {
        var fake = new FakeSsh(_ => "should-not-run");
        var lookup = await new DockerInspector(fake).ResolveAsync("/etc/hosts");

        Assert.Equal(DockerPathKind.None, lookup.Kind);
        Assert.Empty(lookup.Matches);
        Assert.Null(fake.LastCommand);                 // لم يُشغَّل أيّ أمر عن بُعد
    }

    [Fact]
    public async Task ResolveAsync_OverlayPath_RunsLookupAndParses()
    {
        const string outp =
            "/app-web|running|laravel|store|store|" +
            "/var/lib/docker/overlay2/d0fdcfc82eea/diff|/var/lib/docker/overlay2/d0fdcfc82eea/merged\n";
        var fake = new FakeSsh(_ => outp);

        var lookup = await new DockerInspector(fake).ResolveAsync(
            "/var/lib/docker/overlay2/d0fdcfc82eea/diff/var/www/html/storage/logs/laravel.log");

        Assert.Equal(DockerPathKind.Overlay, lookup.Kind);
        Assert.Equal("d0fdcfc82eea", lookup.Key);
        Assert.Equal("app-web", lookup.Matches.Single().Name);
        Assert.Contains("grep -F", fake.LastCommand);
    }
}
