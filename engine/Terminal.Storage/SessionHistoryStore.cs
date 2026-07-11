using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Terminal.Storage;

/// <summary>
/// Per-session command history: the ordered list of commands executed within a single terminal
/// session (keyed by an opaque session id). Powers up/down recall scoped to that session and
/// survives app restart, so a restored session keeps its recall list. Rows are deleted when the
/// user closes that session. Backed by a <c>session_history</c> table in the shared
/// <see cref="AppDatabase"/>; the table is created on first use.
/// </summary>
public sealed class SessionHistoryStore
{
    private readonly AppDatabase _db;

    public SessionHistoryStore(AppDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _db.Execute(
            """
            CREATE TABLE IF NOT EXISTS session_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                command TEXT NOT NULL
            );
            """);
        _db.Execute(
            "CREATE INDEX IF NOT EXISTS ix_session_history_session ON session_history (session_id, id);");
    }

    /// <summary>Appends one command to a session's history (ignores empty session id / blank command).</summary>
    public void Append(string sessionId, string command)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrWhiteSpace(command)) return;

        using var connection = _db.Connect();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO session_history (session_id, command) VALUES ($s, $c);";
        cmd.Parameters.AddWithValue("$s", sessionId);
        cmd.Parameters.AddWithValue("$c", command);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns the session's commands in execution order (oldest first).</summary>
    public IReadOnlyList<string> List(string sessionId)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(sessionId)) return results;

        using var connection = _db.Connect();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT command FROM session_history WHERE session_id = $s ORDER BY id ASC;";
        cmd.Parameters.AddWithValue("$s", sessionId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) results.Add(reader.GetString(0));
        return results;
    }

    /// <summary>Deletes all history rows for a session (called when the user closes that session).</summary>
    public void DeleteSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        using var connection = _db.Connect();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM session_history WHERE session_id = $s;";
        cmd.Parameters.AddWithValue("$s", sessionId);
        cmd.ExecuteNonQuery();
    }
}
