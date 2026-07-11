using System;
using System.IO;
using Terminal.Storage;

namespace Terminal.Storage.Tests;

/// <summary>
/// A disposable <see cref="AppDatabase"/> backed by a unique temp file, so each test gets an
/// isolated database that is deleted on dispose. Feature-store tests should use this instead of
/// the real <c>%AppData%</c> location. Usage: <c>using var db = new TestDatabase();</c>.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _path;

    public AppDatabase Db { get; }

    public TestDatabase()
    {
        // A GUID-named temp file keeps parallel tests (and parallel authors) from colliding.
        _path = Path.Combine(Path.GetTempPath(), $"terminal-storage-test-{Guid.NewGuid():N}.db");
        Db = new AppDatabase(_path);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); // release the file handle before deleting
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch (IOException) { /* best-effort cleanup */ }
        }
    }
}
