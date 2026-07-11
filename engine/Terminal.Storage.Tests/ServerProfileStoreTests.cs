using System.Linq;
using Terminal.Storage;

namespace Terminal.Storage.Tests;

public class ServerProfileStoreTests
{
    private static ServerProfileRow Sample(string id, string name = "web", int sort = 0) =>
        new(id, name, "10.0.0.5", 22, "root", 0, "CIPHER", null, "#3B82F6", "prod box", null, sort);

    [Fact]
    public void Upsert_ThenGet_RoundTrips()
    {
        using var t = new TestDatabase();
        var store = new ServerProfileStore(t.Db);

        store.Upsert(Sample("a", "web-1"));
        var row = store.Get("a");

        Assert.NotNull(row);
        Assert.Equal("web-1", row!.Name);
        Assert.Equal("10.0.0.5", row.Host);
        Assert.Equal(22, row.Port);
        Assert.Equal("CIPHER", row.SecretCipher);
        Assert.Equal("#3B82F6", row.Color);
    }

    [Fact]
    public void Upsert_SameId_Updates_NotDuplicates()
    {
        using var t = new TestDatabase();
        var store = new ServerProfileStore(t.Db);

        store.Upsert(Sample("a", "old"));
        store.Upsert(Sample("a", "new"));

        var all = store.GetAll();
        Assert.Single(all);
        Assert.Equal("new", all[0].Name);
    }

    [Fact]
    public void GetAll_OrdersBySortThenName()
    {
        using var t = new TestDatabase();
        var store = new ServerProfileStore(t.Db);

        store.Upsert(Sample("a", "zeta", sort: 1));
        store.Upsert(Sample("b", "alpha", sort: 0));
        store.Upsert(Sample("c", "beta", sort: 0));

        var all = store.GetAll();
        Assert.Equal(new[] { "alpha", "beta", "zeta" }, all.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Delete_RemovesRow()
    {
        using var t = new TestDatabase();
        var store = new ServerProfileStore(t.Db);

        store.Upsert(Sample("a"));
        store.Delete("a");

        Assert.Null(store.Get("a"));
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void UpdateLastConnected_SetsTimestamp()
    {
        using var t = new TestDatabase();
        var store = new ServerProfileStore(t.Db);

        store.Upsert(Sample("a"));
        store.UpdateLastConnected("a", 1_700_000_000_000);

        Assert.Equal(1_700_000_000_000, store.Get("a")!.LastConnectedUnixMs);
    }
}
