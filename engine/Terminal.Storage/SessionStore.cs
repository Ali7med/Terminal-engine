using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Terminal.Storage;

/// <summary>
/// A single terminal tab captured for later restore. <paramref name="SessionId"/> links the tab to
/// its per-session command history; <paramref name="LastCommand"/> is re-run on restore;
/// <paramref name="Color"/> is the tab's marker colour (#RRGGBB, empty when uncoloured). All are
/// optional with defaults so snapshots saved before these fields deserialize cleanly (as null).
/// </summary>
public sealed record TabSnapshot(
    string Title, string? ShellKey, string? WorkingDirectory,
    string? SessionId = null, string? LastCommand = null, string? Color = null);

/// <summary>One saved window/session: an ordered list of its tabs.</summary>
public sealed record SessionSnapshot(IReadOnlyList<TabSnapshot> Tabs);

/// <summary>
/// Persists and restores terminal sessions (a window's list of tabs) as JSON rows in SQLite.
/// Each row is one saved snapshot with the unix-ms timestamp it was saved at. The store owns its
/// <c>sessions</c> table and creates it on demand, so it stays independent of other feature stores.
/// </summary>
public sealed class SessionStore
{
    private readonly AppDatabase _db;

    /// <summary>
    /// Creates the store and ensures the <c>sessions</c> table exists.
    /// </summary>
    public SessionStore(AppDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _db.Execute(
            "CREATE TABLE IF NOT EXISTS sessions (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "saved_at INTEGER NOT NULL, " +
            "payload TEXT NOT NULL)");
    }

    /// <summary>Serializes and inserts a snapshot, returning the new row id.</summary>
    public long Save(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string payload = JsonSerializer.Serialize(snapshot);
        long savedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO sessions (saved_at, payload) VALUES ($savedAt, $payload); " +
            "SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$savedAt", savedAt);
        command.Parameters.AddWithValue("$payload", payload);
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Returns the most recently saved snapshot, or <c>null</c> if the store is empty.</summary>
    public SessionSnapshot? LoadLatest()
    {
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM sessions ORDER BY id DESC LIMIT 1;";
        if (command.ExecuteScalar() is string payload)
            return Deserialize(payload);
        return null;
    }

    /// <summary>Returns every saved snapshot, newest-first.</summary>
    public IReadOnlyList<(long Id, DateTimeOffset SavedAt, SessionSnapshot Snapshot)> ListAll()
    {
        var results = new List<(long, DateTimeOffset, SessionSnapshot)>();

        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, saved_at, payload FROM sessions ORDER BY id DESC;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            var savedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
            var snapshot = Deserialize(reader.GetString(2));
            results.Add((id, savedAt, snapshot));
        }
        return results;
    }

    /// <summary>Deletes the snapshot with the given id (no-op if it does not exist).</summary>
    public void Delete(long id)
    {
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Removes every saved snapshot.</summary>
    public void Clear()
    {
        _db.Execute("DELETE FROM sessions;");
    }

    private static SessionSnapshot Deserialize(string payload) =>
        JsonSerializer.Deserialize<SessionSnapshot>(payload)
            ?? new SessionSnapshot(Array.Empty<TabSnapshot>());
}
