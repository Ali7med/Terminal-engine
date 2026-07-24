using System;
using System.Linq;
using Terminal.Storage;
using Xunit;

namespace Terminal.Storage.Tests;

/// <summary>
/// اختبارات القراءات التي يعتمد عليها بانِي «ملفّ معرفة المستخدم» ونافذة «ذاكرة التطبيق».
/// البانِي نفسه في طبقة الواجهة، لكنّ مدخلاته كلّها من هنا — فاختبارها يثبّت العقد بينهما.
/// </summary>
public sealed class AiProfileDataTests
{
    private static string NoRedact(string text) => text;

    private static AiKnowledgeStore NewStore(TestDatabase db) => new(db.Db, NoRedact);

    [Fact]
    public void RecentErrors_PutsSolvedOnesFirst()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        store.RecordError(1, "unsolved failure here");
        string solved = store.RecordError(127, "command not found: pnpm");
        store.SetErrorSolution(solved, "npm i -g pnpm");

        var errors = store.RecentErrors();

        Assert.Equal(2, errors.Count);
        Assert.NotNull(errors[0].Solution);   // ما له حلّ يتصدّر — هو ما يفيد الاستدعاء والملفّ
    }

    [Fact]
    public void RecentSuggestions_ReturnsNewestFirstWithVerdict()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        long first = store.RecordSuggestion("catalog", "h1", "npm run build");
        store.RecordSuggestion("fix", "h2", "git init");
        store.DecideSuggestion(first, SuggestionVerdict.Accepted);

        var log = store.RecentSuggestions();

        Assert.Equal(2, log.Count);
        Assert.Equal("fix", log[0].Kind);   // الأحدث أوّلاً
        Assert.Equal(SuggestionVerdict.Accepted, log.Single(s => s.Kind == "catalog").Verdict);
    }

    [Fact]
    public void TopCommands_RanksPinnedFirstThenFrequency()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        for (int i = 0; i < 10; i++) store.RecordCommand("dotnet build", "pwsh");
        store.RecordCommand("rare thing", "pwsh");

        string rareHash = store.TopCommands().Single(c => c.Template.StartsWith("rare", StringComparison.Ordinal)).TemplateHash;
        store.SetPinned(rareHash, pinned: true);

        var ranked = store.TopCommands();

        // المثبَّت يتصدّر رغم أنّ تكراره أقلّ بكثير — التثبيت تفضيل صريح يتقدّم على الإحصاء.
        Assert.Equal(rareHash, ranked[0].TemplateHash);
    }

    [Fact]
    public void ProfileInputs_ExcludeBannedTemplates()
    {
        using var db = new TestDatabase();
        var store = NewStore(db);

        for (int i = 0; i < 5; i++) store.RecordCommand("secret-tool deploy", "pwsh");
        string hash = store.TopCommands().Single().TemplateHash;

        store.SetBanned(hash, banned: true);

        // المحظور لا يصل إلى الترتيب ولا إلى الملفّ المحقون — الحظر يجب أن يمنع الإرسال لا العرض فقط.
        Assert.Empty(store.TopCommands());
    }
}
