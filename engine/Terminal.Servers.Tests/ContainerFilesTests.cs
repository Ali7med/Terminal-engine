using System.Linq;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Scan;

namespace Terminal.Servers.Tests;

public class ContainerFilesTests
{
    [Fact]
    public void BuildList_UsesExecLsLongWithQuotedPathAndStopParsing()
    {
        string cmd = ContainerFiles.BuildList("abc123", "/var/www");
        Assert.Contains("docker exec abc123 ls -lA -- '/var/www'", cmd);   // فرع الاحتياط
        Assert.DoesNotContain("sudo", cmd);
    }

    [Fact]
    public void BuildList_RequestsFullTimeIsoWithDefaultFallback()
    {
        string cmd = ContainerFiles.BuildList("abc123", "/var/www");
        Assert.Contains("ls -lA --full-time -- '/var/www' 2>/dev/null", cmd);   // ISO أوّلاً
        Assert.Contains("||", cmd);                                             // ثمّ الافتراضيّ
    }

    [Fact]
    public void BuildList_CapturesFirstOutput_SoFailedAttemptCannotConcatWithFallback()
    {
        // ls قد يطبع السرد ثمّ يخرج برمز غير صفريّ ⇒ لو طُبع مباشرةً لاختلط مع سرد الاحتياط (صفوف مكرّرة)
        string cmd = ContainerFiles.BuildList("abc123", "/var/www");
        Assert.StartsWith("o=$(", cmd);
        Assert.Contains(") && printf '%s\\n' \"$o\" ||", cmd);
    }

    [Fact]
    public void BuildList_Sudo_PrefixesNonInteractiveSudo()
    {
        string cmd = ContainerFiles.BuildList("abc123", "/", sudo: true);
        Assert.Contains("sudo -n docker exec abc123 ls", cmd);
    }

    [Fact]
    public void Parse_IsoFullTime_Busybox_NormalizesToMinutes()
    {
        // busybox --full-time: التاريخ حقلان (بلا منطقة زمنيّة) — YYYY-MM-DD HH:MM:SS
        const string outp =
            "-rw-r--r-- 1 root root  123 2026-07-15 10:30:45 alpha.log\n" +
            "drwxr-xr-x 2 root root 4096 2026-01-02 09:00:00 bin\n";
        var e = ContainerFiles.Parse(outp);
        Assert.Equal(2, e.Count);
        Assert.Equal("bin", e[0].Name); Assert.True(e[0].IsDir);
        Assert.Equal("2026-01-02 09:00", e[0].Modified);
        Assert.Equal("alpha.log", e[1].Name);
        Assert.Equal("2026-07-15 10:30", e[1].Modified);   // الثواني مقصوصة
        Assert.Equal(123, e[1].Size);
    }

    [Fact]
    public void Parse_IsoFullTime_Gnu_StripsTimezoneField()
    {
        // GNU --full-time: يضيف كسور ثانية + حقل منطقة زمنيّة (+0000) قبل الاسم
        const string outp =
            "-rw-r--r-- 1 root root 42 2026-07-15 10:30:45.123456789 +0000 my file.txt\n";
        var c = Assert.Single(ContainerFiles.Parse(outp));
        Assert.Equal("my file.txt", c.Name);           // منطقة TZ لا تُبتلَع في الاسم
        Assert.Equal("2026-07-15 10:30", c.Modified);
        Assert.Equal(42, c.Size);
    }

    [Fact]
    public void Parse_Busybox_KeepsNameStartingWithTimezoneLikeToken()
    {
        // بلا كسور ثانية ⇒ لا حقل TZ (busybox). اسم يبدأ بنمط ‎±HHMM‎ يجب ألّا يُبتَر.
        const string outp =
            "-rw-r--r-- 1 root root 42 2026-07-15 10:30:45 +0300 offsets.txt\n" +
            "-rw-r--r-- 1 root root 11 2026-07-15 10:30:45 +0200\n";
        var e = ContainerFiles.Parse(outp);
        Assert.Equal(2, e.Count);
        Assert.Contains(e, x => x.Name == "+0300 offsets.txt");
        Assert.Contains(e, x => x.Name == "+0200");     // ملفّ اسمه منطقة زمنيّة لا يختفي
    }

    [Fact]
    public void Parse_LegacyDates_GetChronologicalSortKey_NotAlphabeticalByMonth()
    {
        const string outp =
            "-rw-r--r-- 1 root root 1 Apr 30 2025 old-april.txt\n" +
            "-rw-r--r-- 1 root root 1 Jan 2 10:15 recent-jan.txt\n" +
            "-rw-r--r-- 1 root root 1 Dec 12 09:15 recent-dec.txt\n" +
            "-rw-r--r-- 1 root root 1 Sep 1 2020 old-sep.txt\n";
        var byOldest = ContainerFiles.Parse(outp)
            .OrderBy(x => x.ModifiedSort, System.StringComparer.Ordinal)
            .Select(x => x.Name).ToList();

        // الأقدم أوّلاً: 2020 ثمّ 2025 ثمّ الحديثة (صيغة الوقت) مرتّبة بالشهر/اليوم لا أبجديّاً
        Assert.Equal(new[] { "old-sep.txt", "old-april.txt", "recent-jan.txt", "recent-dec.txt" }, byOldest);
    }

    [Fact]
    public void Parse_IsoSortKey_MatchesDisplayText()
    {
        const string outp = "-rw-r--r-- 1 root root 1 2026-07-15 10:30:45 a.txt\n";
        var c = Assert.Single(ContainerFiles.Parse(outp));
        Assert.Equal("2026-07-15 10:30", c.Modified);
        Assert.Equal("2026-07-15 10:30", c.ModifiedSort);
    }

    [Fact]
    public void Parse_LongFormat_DirsFirst_WithSizeAndType()
    {
        const string outp =
            "total 20\n" +
            "-rw-r--r--    1 root     root          4096 Jul 15 10:30 zeta.txt\n" +
            "drwxr-xr-x    2 root     root          4096 Jul 15 10:30 bin\n" +
            "-rw-r--r--    1 root     root           123 Jul 14 09:00 alpha.log\n" +
            "drwxr-xr-x    5 root     root          4096 Jul 15 10:30 etc\n" +
            "lrwxrwxrwx    1 root     root             7 Jul 15 10:30 link -> alpha.log\n";
        var e = ContainerFiles.Parse(outp);

        Assert.Equal(5, e.Count);
        Assert.Equal("bin", e[0].Name);   Assert.True(e[0].IsDir);   Assert.Equal('d', e[0].Type);
        Assert.Equal("etc", e[1].Name);   Assert.True(e[1].IsDir);
        Assert.Equal("alpha.log", e[2].Name); Assert.False(e[2].IsDir); Assert.Equal(123, e[2].Size);
        Assert.Equal("link", e[3].Name);  Assert.Equal('l', e[3].Type);   // -> target مُزال
        Assert.Equal("zeta.txt", e[4].Name);  Assert.Equal(4096, e[4].Size);
    }

    [Fact]
    public void Parse_HandlesNameWithSpaces()
    {
        const string outp = "-rw-r--r-- 1 root root 42 Jul 15 10:30 my  file .txt\n";
        var c = Assert.Single(ContainerFiles.Parse(outp));
        Assert.Equal("my  file .txt", c.Name);
        Assert.Equal(42, c.Size);
    }

    [Fact]
    public void Parse_SkipsTotalLineAndBlanks()
        => Assert.Empty(ContainerFiles.Parse("total 0\n\n"));

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
