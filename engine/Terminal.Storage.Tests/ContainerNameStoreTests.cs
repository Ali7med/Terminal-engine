using System.Linq;
using Terminal.Storage;
using Xunit;

namespace Terminal.Storage.Tests;

public class ContainerNameStoreTests
{
    [Fact]
    public void Set_Then_GetForProfile_RoundTrips()
    {
        using var db = new TestDatabase();
        var store = new ContainerNameStore(db.Db);

        store.Set("srv1", "web-1", "الموقع");
        store.Set("srv1", "db-1", "قاعدة البيانات");

        var map = store.GetForProfile("srv1");
        Assert.Equal(2, map.Count);
        Assert.Equal("الموقع", map["web-1"]);
        Assert.Equal("قاعدة البيانات", map["db-1"]);
    }

    [Fact]
    public void Names_AreIsolatedPerProfile()
    {
        using var db = new TestDatabase();
        var store = new ContainerNameStore(db.Db);

        store.Set("srv1", "web-1", "متجر");
        store.Set("srv2", "web-1", "مدوّنة");   // نفس مفتاح الحاوية، خادم مختلف

        Assert.Equal("متجر", store.GetForProfile("srv1")["web-1"]);
        Assert.Equal("مدوّنة", store.GetForProfile("srv2")["web-1"]);
    }

    [Fact]
    public void Set_UpsertsExistingKey()
    {
        using var db = new TestDatabase();
        var store = new ContainerNameStore(db.Db);

        store.Set("srv1", "web-1", "أوّل");
        store.Set("srv1", "web-1", "ثانٍ");

        Assert.Equal("ثانٍ", store.GetForProfile("srv1")["web-1"]);
    }

    [Fact]
    public void Set_EmptyName_RemovesEntry()
    {
        using var db = new TestDatabase();
        var store = new ContainerNameStore(db.Db);

        store.Set("srv1", "web-1", "اسم");
        store.Set("srv1", "web-1", "   ");   // فارغ ⇒ حذف

        Assert.Empty(store.GetForProfile("srv1"));
    }

    [Fact]
    public void GetAll_ReturnsEveryProfilesNames()
    {
        using var db = new TestDatabase();
        var store = new ContainerNameStore(db.Db);

        store.Set("srv1", "web-1", "أ");
        store.Set("srv2", "api-1", "ب");

        var all = store.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, x => x.ProfileId == "srv1" && x.ContainerKey == "web-1" && x.CustomName == "أ");
        Assert.Contains(all, x => x.ProfileId == "srv2" && x.ContainerKey == "api-1" && x.CustomName == "ب");
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        using var db = new TestDatabase();
        var store = new ContainerNameStore(db.Db);

        store.Set("srv1", "web-1", "اسم");
        store.Remove("srv1", "web-1");

        Assert.Empty(store.GetForProfile("srv1"));
    }
}
