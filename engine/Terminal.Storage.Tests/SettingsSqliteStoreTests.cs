using System;
using Terminal.Storage;

namespace Terminal.Storage.Tests;

public class SettingsSqliteStoreTests
{
    [Fact]
    public void Set_then_Get_round_trips_a_value()
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);

        store.Set("theme", "dark");

        Assert.Equal("dark", store.Get("theme"));
    }

    [Fact]
    public void Set_twice_overwrites_and_does_not_duplicate()
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);

        store.Set("theme", "dark");
        store.Set("theme", "light");

        Assert.Equal("light", store.Get("theme"));
        Assert.Single(store.GetAll()); // upsert, not a second row
    }

    [Fact]
    public void TryGet_returns_true_for_present_key()
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);
        store.Set("font", "Cascadia");

        bool found = store.TryGet("font", out string? value);

        Assert.True(found);
        Assert.Equal("Cascadia", value);
    }

    [Fact]
    public void TryGet_returns_false_for_absent_key()
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);

        bool found = store.TryGet("missing", out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Get_returns_null_for_absent_key()
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);

        Assert.Null(store.Get("missing"));
    }

    [Fact]
    public void Remove_deletes_the_key()
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);
        store.Set("temp", "value");

        store.Remove("temp");

        Assert.False(store.TryGet("temp", out _));
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Null_value_round_trips_as_present_but_null()
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);

        store.Set("flag", null);

        Assert.True(store.TryGet("flag", out string? value)); // key present
        Assert.Null(value);                                   // value is NULL
        Assert.Null(store.Get("flag"));
    }

    [Fact]
    public void GetAll_returns_every_pair()
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);
        store.Set("a", "1");
        store.Set("b", "2");
        store.Set("c", null);

        var all = store.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Equal("1", all["a"]);
        Assert.Equal("2", all["b"]);
        Assert.Null(all["c"]);
    }

    [Fact]
    public void Clear_empties_the_store()
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);
        store.Set("a", "1");
        store.Set("b", "2");

        store.Clear();

        Assert.Empty(store.GetAll());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_key_throws(string? key)
    {
        using var t = new TestDatabase();
        var store = new SettingsSqliteStore(t.Db);

        Assert.Throws<ArgumentException>(() => store.Set(key!, "x"));
        Assert.Throws<ArgumentException>(() => store.Get(key!));
        Assert.Throws<ArgumentException>(() => store.TryGet(key!, out _));
        Assert.Throws<ArgumentException>(() => store.Remove(key!));
    }
}
