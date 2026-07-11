using System;
using System.Collections.Generic;

namespace Terminal.Storage;

/// <summary>
/// A durable key/value settings store backed by SQLite. This is the persistent replacement for
/// the JSON-file settings store: it keeps a single <c>settings(key, value)</c> table in the shared
/// <see cref="AppDatabase"/> and exposes simple get/set/remove operations. Keys are unique
/// (PRIMARY KEY) so writes are upserts. A stored value may be SQL NULL — the key is still present.
/// </summary>
public sealed class SettingsSqliteStore
{
    private readonly AppDatabase _db;

    /// <summary>
    /// Creates the store and ensures its backing table exists.
    /// </summary>
    public SettingsSqliteStore(AppDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _db.Execute("CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT)");
    }

    /// <summary>Returns the value for <paramref name="key"/>, or null if the key is absent or its value is NULL.</summary>
    public string? Get(string key)
    {
        TryGet(key, out string? value);
        return value;
    }

    /// <summary>
    /// Looks up <paramref name="key"/>. Returns true when the key exists (even if its stored value is
    /// NULL, in which case <paramref name="value"/> is null); false when the key is absent.
    /// </summary>
    public bool TryGet(string key, out string? value)
    {
        ValidateKey(key);

        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            value = null;
            return false;
        }

        value = reader.IsDBNull(0) ? null : reader.GetString(0);
        return true;
    }

    /// <summary>
    /// Inserts or updates <paramref name="key"/> with <paramref name="value"/>. A null value stores
    /// SQL NULL (the key remains present). Existing keys are overwritten, never duplicated.
    /// </summary>
    public void Set(string key, string? value)
    {
        ValidateKey(key);

        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO settings (key, value) VALUES ($key, $value) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>Deletes <paramref name="key"/> if present; a no-op otherwise.</summary>
    public void Remove(string key)
    {
        ValidateKey(key);

        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    /// <summary>Returns every key/value pair currently stored. NULL values map to null entries.</summary>
    public IReadOnlyDictionary<string, string?> GetAll()
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);

        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM settings";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string key = reader.GetString(0);
            string? value = reader.IsDBNull(1) ? null : reader.GetString(1);
            result[key] = value;
        }

        return result;
    }

    /// <summary>Removes all settings.</summary>
    public void Clear()
    {
        _db.Execute("DELETE FROM settings");
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Setting key must be non-null and non-whitespace.", nameof(key));
    }
}
