using System.IO;
using Terminal.Storage;

namespace Terminal.Storage.Tests;

public class AppDatabaseTests
{
    [Fact]
    public void Connect_creates_the_database_file()
    {
        using var t = new TestDatabase();
        Assert.False(File.Exists(t.Db.FilePath)); // not created until first connection

        using (var conn = t.Db.Connect())
            Assert.Equal(System.Data.ConnectionState.Open, conn.State);

        Assert.True(File.Exists(t.Db.FilePath));
    }

    [Fact]
    public void Execute_then_query_round_trips_a_row()
    {
        using var t = new TestDatabase();
        t.Db.Execute("CREATE TABLE IF NOT EXISTS probe (id INTEGER PRIMARY KEY, name TEXT);");

        using var conn = t.Db.Connect();
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO probe (name) VALUES ($n);";
            insert.Parameters.AddWithValue("$n", "hello");
            insert.ExecuteNonQuery();
        }

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT name FROM probe WHERE id = 1;";
        Assert.Equal("hello", select.ExecuteScalar());
    }

    [Fact]
    public void Wal_journal_mode_is_enabled()
    {
        using var t = new TestDatabase();
        using var conn = t.Db.Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", ((string)cmd.ExecuteScalar()!).ToLowerInvariant());
    }

    [Fact]
    public void Default_path_lives_under_the_terminal_launcher_app_data_folder()
    {
        Assert.EndsWith(Path.Combine("HeliumRedTools", "TerminalLauncher", "terminal.db"), AppDatabase.DefaultPath);
    }
}
