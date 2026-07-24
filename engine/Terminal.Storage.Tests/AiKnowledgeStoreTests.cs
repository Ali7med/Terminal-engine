using System;
using System.Collections.Generic;
using Terminal.Storage;
using Xunit;

namespace Terminal.Storage.Tests;

/// <summary>
/// اختبارات قاعدة المعرفة المحلّيّة. تركّز على المبدأين الحاكمين: التجميع (لا صفّ لكلّ تنفيذ)
/// وحجب الأسرار قبل الكتابة على القرص.
/// </summary>
public sealed class AiKnowledgeStoreTests
{
    /// <summary>مُنقّح اختباريّ بسيط: يحجب كلّ ما بعد <c>--token=</c>.</summary>
    private static string FakeRedact(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"(--token=)\S+", "$1[محجوب]");

    private static AiKnowledgeStore NewStore(TestDatabase db) => new(db.Db, FakeRedact);

    [Fact]
    public void RepeatedCommands_CollapseToOneTemplate()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        store.RecordCommand("git checkout feature/login", "pwsh", @"C:\proj", succeeded: true);
        store.RecordCommand("git checkout fix/crash", "pwsh", @"C:\proj", succeeded: true);
        store.RecordCommand("git checkout hotfix/urgent", "pwsh", @"C:\proj", succeeded: false);

        IReadOnlyList<CommandStat> top = store.TopCommands();

        CommandStat stat = Assert.Single(top);
        Assert.Equal(3, stat.RunCount);
        Assert.Equal(2, stat.SuccessCount);
        Assert.Equal(1, stat.FailCount);
    }

    [Fact]
    public void DifferentShells_TrackedSeparately()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        store.RecordCommand("ls -la", "bash");
        store.RecordCommand("ls -la", "pwsh");

        Assert.Equal(2, store.TopCommands().Count);
        Assert.Single(store.TopCommands(shell: "bash"));
    }

    [Fact]
    public void SecretsAreRedactedBeforeReachingDisk()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        store.RecordCommand("deploy --token=super-secret-value", "pwsh");

        CommandStat stat = Assert.Single(store.TopCommands());
        Assert.DoesNotContain("super-secret-value", stat.Sample, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", stat.Template, StringComparison.Ordinal);
    }

    [Fact]
    public void BannedTemplates_AreHiddenFromRanking()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);
        store.RecordCommand("npm run build", "pwsh");

        string hash = Assert.Single(store.TopCommands()).TemplateHash;
        store.SetBanned(hash, banned: true);

        Assert.Empty(store.TopCommands());
    }

    [Fact]
    public void ErrorSolution_IsRecalledByFingerprint()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        string fingerprint = store.RecordError(1, "fatal: not a git repository");
        store.SetErrorSolution(fingerprint, "run git init");

        // نفس الخطأ من مسار مختلف يجب أن يُطابق نفس البصمة (التطبيع يزيل المسارات والأرقام).
        string again = store.RecordError(1, "fatal: not a git repository");
        Assert.Equal(fingerprint, again);

        ErrorPattern? pattern = store.FindError(fingerprint);
        Assert.NotNull(pattern);
        Assert.Equal("run git init", pattern!.Solution);
        Assert.Equal(2, pattern.SeenCount);
    }

    [Fact]
    public void RejectedSuggestion_IsRememberedAsPermanent()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        long id = store.RecordSuggestion("catalog", "abc123", "npm run build");
        Assert.False(store.WasRejected("catalog", "abc123"));

        store.DecideSuggestion(id, SuggestionVerdict.Rejected);
        Assert.True(store.WasRejected("catalog", "abc123"));
    }

    [Fact]
    public void CatalogCandidates_RespectThresholdAndPriorSuggestions()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        for (int i = 0; i < 6; i++) store.RecordCommand("docker compose up -d", "pwsh");
        store.RecordCommand("rare-command", "pwsh");

        CommandStat candidate = Assert.Single(store.CatalogCandidates(minRuns: 5));
        Assert.Equal(6, candidate.RunCount);

        // اقتراح واحد لكلّ قالب: بعد عرضه لا يعود مرشّحاً.
        store.RecordSuggestion("catalog", candidate.TemplateHash, candidate.Sample);
        Assert.Empty(store.CatalogCandidates(minRuns: 5));
    }

    [Fact]
    public void AcceptanceRate_CountsOnlyDecidedSuggestions()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        Assert.Null(store.AcceptanceRate());

        store.DecideSuggestion(store.RecordSuggestion("fix", "s1", "p"), SuggestionVerdict.Accepted);
        store.DecideSuggestion(store.RecordSuggestion("fix", "s2", "p"), SuggestionVerdict.Rejected);
        store.RecordSuggestion("fix", "s3", "p"); // معلّق — لا يُحتسب

        Assert.Equal(0.5, store.AcceptanceRate());
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        store.RecordCommand("git status", "pwsh");
        store.RecordError(1, "boom");
        store.ClearAll();

        Assert.Empty(store.TopCommands());
    }

    [Fact]
    public void DeleteCommand_AlsoRemovesItsSuggestions()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);
        store.RecordCommand("dotnet build", "pwsh");

        string hash = Assert.Single(store.TopCommands()).TemplateHash;
        store.DecideSuggestion(store.RecordSuggestion("catalog", hash, "dotnet build"), SuggestionVerdict.Rejected);

        store.DeleteCommand(hash);

        Assert.Empty(store.TopCommands());
        // الاقتراح اليتيم كان سيُبقي الرفض حيّاً عن موضوع حذفه المستخدم.
        Assert.False(store.WasRejected("catalog", hash));
    }
}

/// <summary>اختبارات تطبيع الأوامر — المُطبِّع نفسه يُستعمل في طرفَي مقارنة جسر الكتالوج.</summary>
public sealed class CommandTemplateTests
{
    [Theory]
    [InlineData(@"cd C:\Users\me\proj", "cd <path>")]
    [InlineData("cat /var/log/syslog", "cat <path>")]
    [InlineData("curl https://example.com/api", "curl <url>")]
    [InlineData("git commit -m \"fix login\"", "git commit -m <str>")]
    [InlineData("kill 12345", "kill <n>")]
    public void Normalize_ReplacesVariableParts(string input, string expected)
        => Assert.Equal(expected, CommandTemplate.Normalize(input).Template);

    [Fact]
    public void SameShape_SharesHash_DifferentShape_DoesNot()
    {
        string a = CommandTemplate.Normalize("git push origin feature/a").Hash;
        string b = CommandTemplate.Normalize("git push origin fix/b").Hash;
        string c = CommandTemplate.Normalize("git pull origin main").Hash;

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ErrorFingerprint_IgnoresPathsAndNumbers()
    {
        string one = CommandTemplate.ErrorFingerprint(1, @"error CS0103 in C:\a\b.cs line 12");
        string two = CommandTemplate.ErrorFingerprint(1, @"error CS0103 in D:\x\y.cs line 99");

        Assert.Equal(one, two);
    }

    [Fact]
    public void ErrorFingerprint_DistinguishesExitCodes()
        => Assert.NotEqual(
            CommandTemplate.ErrorFingerprint(1, "boom"),
            CommandTemplate.ErrorFingerprint(2, "boom"));
}
