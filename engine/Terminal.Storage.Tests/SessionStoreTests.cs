using System.Collections.Generic;
using System.Linq;
using Terminal.Storage;

namespace Terminal.Storage.Tests;

public class SessionStoreTests
{
    private static SessionSnapshot Snapshot(params TabSnapshot[] tabs) => new(tabs);

    [Fact]
    public void Save_then_LoadLatest_round_trips_tabs_exactly()
    {
        using var t = new TestDatabase();
        var store = new SessionStore(t.Db);

        var snapshot = Snapshot(
            new TabSnapshot("Build", "pwsh", @"C:\work\repo"),
            new TabSnapshot("Logs", null, null),
            new TabSnapshot("Bash", "bash", @"/home/user"));
        store.Save(snapshot);

        var loaded = store.LoadLatest();

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Tabs.Count);

        Assert.Equal("Build", loaded.Tabs[0].Title);
        Assert.Equal("pwsh", loaded.Tabs[0].ShellKey);
        Assert.Equal(@"C:\work\repo", loaded.Tabs[0].WorkingDirectory);

        Assert.Equal("Logs", loaded.Tabs[1].Title);
        Assert.Null(loaded.Tabs[1].ShellKey);
        Assert.Null(loaded.Tabs[1].WorkingDirectory);

        Assert.Equal("Bash", loaded.Tabs[2].Title);
        Assert.Equal("bash", loaded.Tabs[2].ShellKey);
        Assert.Equal(@"/home/user", loaded.Tabs[2].WorkingDirectory);
    }

    [Fact]
    public void LoadLatest_returns_the_newest_of_several()
    {
        using var t = new TestDatabase();
        var store = new SessionStore(t.Db);

        store.Save(Snapshot(new TabSnapshot("First", null, null)));
        store.Save(Snapshot(new TabSnapshot("Second", null, null)));
        long third = store.Save(Snapshot(new TabSnapshot("Third", "pwsh", @"C:\latest")));

        var loaded = store.LoadLatest();

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Tabs);
        Assert.Equal("Third", loaded.Tabs[0].Title);
        Assert.Equal(@"C:\latest", loaded.Tabs[0].WorkingDirectory);
        Assert.True(third > 0);
    }

    [Fact]
    public void Empty_store_LoadLatest_is_null_and_ListAll_is_empty()
    {
        using var t = new TestDatabase();
        var store = new SessionStore(t.Db);

        Assert.Null(store.LoadLatest());
        Assert.Empty(store.ListAll());
    }

    [Fact]
    public void ListAll_is_newest_first()
    {
        using var t = new TestDatabase();
        var store = new SessionStore(t.Db);

        long id1 = store.Save(Snapshot(new TabSnapshot("A", null, null)));
        long id2 = store.Save(Snapshot(new TabSnapshot("B", null, null)));
        long id3 = store.Save(Snapshot(new TabSnapshot("C", null, null)));

        var all = store.ListAll();

        Assert.Equal(3, all.Count);
        Assert.Equal(id3, all[0].Id);
        Assert.Equal(id2, all[1].Id);
        Assert.Equal(id1, all[2].Id);

        Assert.Equal("C", all[0].Snapshot.Tabs[0].Title);
        Assert.Equal("A", all[2].Snapshot.Tabs[0].Title);
    }

    [Fact]
    public void ListAll_carries_ids_and_timestamps()
    {
        using var t = new TestDatabase();
        var store = new SessionStore(t.Db);

        long id = store.Save(Snapshot(new TabSnapshot("A", null, null)));
        var only = store.ListAll().Single();

        Assert.Equal(id, only.Id);
        Assert.True(only.SavedAt > System.DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Delete_removes_one_snapshot()
    {
        using var t = new TestDatabase();
        var store = new SessionStore(t.Db);

        long keep = store.Save(Snapshot(new TabSnapshot("Keep", null, null)));
        long drop = store.Save(Snapshot(new TabSnapshot("Drop", null, null)));

        store.Delete(drop);

        var all = store.ListAll();
        Assert.Single(all);
        Assert.Equal(keep, all[0].Id);
        Assert.Equal("Keep", all[0].Snapshot.Tabs[0].Title);
    }

    [Fact]
    public void Clear_empties_the_store()
    {
        using var t = new TestDatabase();
        var store = new SessionStore(t.Db);

        store.Save(Snapshot(new TabSnapshot("A", null, null)));
        store.Save(Snapshot(new TabSnapshot("B", null, null)));

        store.Clear();

        Assert.Empty(store.ListAll());
        Assert.Null(store.LoadLatest());
    }

    [Fact]
    public void Snapshot_with_zero_tabs_round_trips()
    {
        using var t = new TestDatabase();
        var store = new SessionStore(t.Db);

        store.Save(new SessionSnapshot(new List<TabSnapshot>()));

        var loaded = store.LoadLatest();

        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Tabs);
    }
}
