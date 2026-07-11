using System.Linq;
using Terminal.Storage;
using Xunit;

namespace Terminal.Storage.Tests;

public sealed class SessionHistoryStoreTests
{
    [Fact]
    public void List_ReturnsCommandsInExecutionOrder()
    {
        using var db = new TestDatabase();
        var store = new SessionHistoryStore(db.Db);

        store.Append("s1", "cmd one");
        store.Append("s1", "cmd two");
        store.Append("s1", "cmd three");

        Assert.Equal(new[] { "cmd one", "cmd two", "cmd three" }, store.List("s1").ToArray());
    }

    [Fact]
    public void History_IsScopedPerSession()
    {
        using var db = new TestDatabase();
        var store = new SessionHistoryStore(db.Db);

        store.Append("s1", "alpha");
        store.Append("s2", "beta");

        Assert.Equal(new[] { "alpha" }, store.List("s1").ToArray());
        Assert.Equal(new[] { "beta" }, store.List("s2").ToArray());
    }

    [Fact]
    public void DeleteSession_RemovesOnlyThatSession()
    {
        using var db = new TestDatabase();
        var store = new SessionHistoryStore(db.Db);
        store.Append("s1", "a");
        store.Append("s2", "b");

        store.DeleteSession("s1");

        Assert.Empty(store.List("s1"));
        Assert.Equal(new[] { "b" }, store.List("s2").ToArray());
    }

    [Fact]
    public void Append_IgnoresBlankCommandsAndEmptySessionId()
    {
        using var db = new TestDatabase();
        var store = new SessionHistoryStore(db.Db);

        store.Append("s1", "   ");
        store.Append("", "orphan");

        Assert.Empty(store.List("s1"));
    }

    [Fact]
    public void List_PersistsAcrossStoreInstances()
    {
        using var db = new TestDatabase();
        new SessionHistoryStore(db.Db).Append("s1", "kept");

        // نسخة جديدة على نفس القاعدة (يحاكي إعادة تشغيل التطبيق مع استرجاع الجلسة).
        var reopened = new SessionHistoryStore(db.Db);
        Assert.Equal(new[] { "kept" }, reopened.List("s1").ToArray());
    }
}
