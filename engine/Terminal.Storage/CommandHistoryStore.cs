using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Terminal.Storage;

/// <summary>
/// One executed command as recorded in the history table.
/// </summary>
/// <param name="Id">Auto-increment row id.</param>
/// <param name="Command">The command text as typed.</param>
/// <param name="ExecutedAt">When the command was recorded (UTC).</param>
/// <param name="Shell">The shell it ran under, if known (e.g. <c>cmd</c>, <c>pwsh</c>).</param>
/// <param name="WorkingDirectory">The working directory at execution time, if known.</param>
public sealed record HistoryEntry(long Id, string Command, DateTimeOffset ExecutedAt, string? Shell, string? WorkingDirectory);

/// <summary>
/// Persists the log of commands the user has run so the UI can offer recall (up-arrow style)
/// and search. Backed by a single <c>command_history</c> table in the shared
/// <see cref="AppDatabase"/>; the table is created on first use.
/// </summary>
public sealed class CommandHistoryStore
{
    private readonly AppDatabase _db;

    /// <summary>
    /// Creates the store and ensures its table exists.
    /// </summary>
    public CommandHistoryStore(AppDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _db.Execute(
            """
            CREATE TABLE IF NOT EXISTS command_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                command TEXT NOT NULL,
                executed_at INTEGER NOT NULL,
                shell TEXT NULL,
                cwd TEXT NULL
            );
            """);
    }

    /// <summary>
    /// Records a command. Null or whitespace-only commands are ignored. The timestamp is the
    /// current UTC time stored as Unix milliseconds.
    /// </summary>
    public void Add(string command, string? shell = null, string? cwd = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        using var connection = _db.Connect();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO command_history (command, executed_at, shell, cwd) VALUES ($command, $executedAt, $shell, $cwd);";
        cmd.Parameters.AddWithValue("$command", command);
        cmd.Parameters.AddWithValue("$executedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$shell", (object?)shell ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cwd", (object?)cwd ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns the most recently executed distinct commands, newest first. Repeated commands
    /// collapse to a single entry keyed on their newest occurrence. Capped at <paramref name="limit"/>.
    /// </summary>
    public IReadOnlyList<string> Recent(int limit = 100)
    {
        var results = new List<string>();
        if (limit <= 0)
            return results;

        using var connection = _db.Connect();
        using var cmd = connection.CreateCommand();
        // Group by command text and keep the newest row per command, then order those groups newest-first.
        cmd.CommandText =
            """
            SELECT command, MAX(id) AS newest
            FROM command_history
            GROUP BY command
            ORDER BY newest DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(reader.GetString(0));

        return results;
    }

    /// <summary>
    /// Returns history entries whose command text contains <paramref name="term"/> (case-insensitive
    /// substring match), newest first, capped at <paramref name="limit"/>.
    /// </summary>
    public IReadOnlyList<HistoryEntry> Search(string term, int limit = 100)
    {
        var results = new List<HistoryEntry>();
        if (limit <= 0 || term is null)
            return results;

        using var connection = _db.Connect();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, command, executed_at, shell, cwd
            FROM command_history
            WHERE command LIKE $pattern ESCAPE '\'
            ORDER BY id DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$pattern", "%" + EscapeLike(term) + "%");
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            string command = reader.GetString(1);
            var executedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2));
            string? shell = reader.IsDBNull(3) ? null : reader.GetString(3);
            string? cwd = reader.IsDBNull(4) ? null : reader.GetString(4);
            results.Add(new HistoryEntry(id, command, executedAt, shell, cwd));
        }

        return results;
    }

    /// <summary>
    /// Returns the most-recent distinct stored command that starts with <paramref name="prefix"/>
    /// (ordinal, case-sensitive) and is strictly longer than it — for inline autocomplete (ghost
    /// text). Returns <c>null</c> if <paramref name="prefix"/> is null/whitespace or nothing matches.
    /// Wildcard characters (<c>%</c>, <c>_</c>) in the prefix are matched literally.
    /// </summary>
    public string? Suggest(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return null;

        using var connection = _db.Connect();
        using var cmd = connection.CreateCommand();
        // Newest matching command (by MAX row id) that starts with the (literal) prefix but isn't
        // exactly equal to it. LIKE anchors the escaped literal prefix with a trailing %; SQLite's
        // ASCII LIKE is case-insensitive, so an ordinal-cased substr check enforces case-sensitivity.
        cmd.CommandText =
            """
            SELECT command
            FROM command_history
            WHERE command LIKE $pattern ESCAPE '\'
              AND substr(command, 1, $len) = $prefix
              AND command <> $prefix
            GROUP BY command
            ORDER BY MAX(id) DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$pattern", EscapeLike(prefix) + "%");
        cmd.Parameters.AddWithValue("$prefix", prefix);
        cmd.Parameters.AddWithValue("$len", prefix.Length);

        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Deletes every recorded command.
    /// </summary>
    public void Clear()
    {
        _db.Execute("DELETE FROM command_history;");
    }

    // SQLite LIKE treats % and _ as wildcards; escape them (plus the escape char) so a search
    // term is matched literally as a substring.
    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
