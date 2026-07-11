using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Terminal.Storage;

/// <summary>
/// Central SQLite connection factory for TerminalLauncher's local storage. One database file
/// holds every feature table (command history, settings, sessions, …). Each feature store owns
/// its own table and creates it on demand via <see cref="Execute"/> (CREATE TABLE IF NOT EXISTS),
/// so stores stay independent and never share a schema file — which is what lets separate authors
/// add stores in parallel without touching one another's code.
/// </summary>
public sealed class AppDatabase
{
    /// <summary>Default on-disk location: <c>%AppData%\HeliumRedTools\TerminalLauncher\terminal.db</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher", "terminal.db");

    private readonly string _connectionString;

    /// <summary>The resolved database file path this instance connects to.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Creates a connection factory. <paramref name="dbPath"/> defaults to <see cref="DefaultPath"/>;
    /// pass a custom path (e.g. a temp file) in tests. The parent directory is created if missing.
    /// </summary>
    public AppDatabase(string? dbPath = null)
    {
        FilePath = dbPath ?? DefaultPath;
        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    /// <summary>
    /// Opens a new connection to the database. WAL journaling is enabled so a writer does not block
    /// readers, and foreign keys are enforced. The caller owns and must dispose the connection.
    /// </summary>
    public SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// Runs a single non-query statement on a fresh connection — typically a store's
    /// <c>CREATE TABLE IF NOT EXISTS …</c> at initialization.
    /// </summary>
    public void Execute(string sql)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
