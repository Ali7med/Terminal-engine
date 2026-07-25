using System;
using System.Linq;
using Terminal.Storage;
using Xunit;

namespace Terminal.Storage.Tests;

/// <summary>
/// اختبارات مطابقة جسر الكتالوج: يجب أن يُطبَّع أمرُ الكتالوج بنفس مُطبِّع قاعدة التعلّم، وإلّا
/// اقتُرح على المستخدم حفظ أمر يملكه أصلاً.
/// </summary>
public sealed class CommandTemplateBridgeTests
{
    [Fact]
    public void CatalogEntryAndRunShareHash_WhenSameShape()
    {
        // ما يخزّنه المستخدم في الكتالوج، وما نفّذه فعلاً — بقيم مختلفة، نفس الشكل.
        string catalogHash = CommandTemplate.Normalize("docker compose -f docker-compose.yml up -d").Hash;
        string runHash = CommandTemplate.Normalize("docker compose -f prod.yml up -d").Hash;

        Assert.Equal(catalogHash, runHash);
    }

    [Fact]
    public void DifferentCommands_DoNotCollide()
    {
        string a = CommandTemplate.Normalize("docker compose up -d").Hash;
        string b = CommandTemplate.Normalize("docker compose down").Hash;

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CandidateExcludedOnceItsHashMatchesACatalogEntry()
    {
        using var db = new TestDatabase();
        var store = new AiKnowledgeStore(db.Db, t => t);

        for (int i = 0; i < 6; i++) store.RecordCommand("terraform apply -auto-approve", "bash");
        CommandStat candidate = Assert.Single(store.CatalogCandidates(minRuns: 5));

        // محاكاة الجسر: بمجرّد تسجيل اقتراح لهذا القالب، لا يعود مرشّحاً (اقتراح واحد لكلّ قالب).
        store.RecordSuggestion("catalog", candidate.TemplateHash, candidate.Sample);
        Assert.Empty(store.CatalogCandidates(minRuns: 5));
    }
}
