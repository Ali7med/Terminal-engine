using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Terminal.Storage;

/// <summary>
/// صفّ بروفايل خادم كما يُخزَّن. الأسرار (<paramref name="SecretCipher"/>/<paramref name="KeyPassphraseCipher"/>)
/// نصوص **مُعمّاة** (base64 DPAPI) تنتجها الطبقة الأعلى — هذا المتجر لا يعمّي ولا يفكّ، بل يخزّن كما هو.
/// </summary>
public sealed record ServerProfileRow(
    string Id,
    string Name,
    string Host,
    int Port,
    string Username,
    int AuthKind,
    string? SecretCipher,
    string? KeyPassphraseCipher,
    string? Color,
    string? Notes,
    long? LastConnectedUnixMs,
    int SortOrder);

/// <summary>
/// يخزّن بروفايلات الخوادم (اتّصالات SSH) في جدول <c>server_profiles</c> بـ SQLite. يملك جدوله
/// وينشئه عند الحاجة، على نمط بقيّة المتاجر (<see cref="SessionStore"/>). الأسرار تُحفَظ مُعمّاةً فقط.
/// </summary>
public sealed class ServerProfileStore
{
    private readonly AppDatabase _db;

    public ServerProfileStore(AppDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _db.Execute(
            "CREATE TABLE IF NOT EXISTS server_profiles (" +
            "id TEXT PRIMARY KEY, " +
            "name TEXT NOT NULL, " +
            "host TEXT NOT NULL, " +
            "port INTEGER NOT NULL, " +
            "username TEXT NOT NULL, " +
            "auth_kind INTEGER NOT NULL, " +
            "secret_cipher TEXT, " +
            "key_passphrase_cipher TEXT, " +
            "color TEXT, " +
            "notes TEXT, " +
            "last_connected INTEGER, " +
            "sort_order INTEGER NOT NULL DEFAULT 0)");
    }

    /// <summary>يُدرج أو يحدّث بروفايلاً حسب <c>id</c> (upsert).</summary>
    public void Upsert(ServerProfileRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO server_profiles " +
            "(id, name, host, port, username, auth_kind, secret_cipher, key_passphrase_cipher, color, notes, last_connected, sort_order) " +
            "VALUES ($id, $name, $host, $port, $username, $auth, $secret, $keypass, $color, $notes, $last, $sort) " +
            "ON CONFLICT(id) DO UPDATE SET " +
            "name=$name, host=$host, port=$port, username=$username, auth_kind=$auth, " +
            "secret_cipher=$secret, key_passphrase_cipher=$keypass, color=$color, notes=$notes, " +
            "last_connected=$last, sort_order=$sort;";
        Bind(command, row);
        command.ExecuteNonQuery();
    }

    /// <summary>كلّ البروفايلات مرتّبةً حسب <c>sort_order</c> ثمّ الاسم.</summary>
    public IReadOnlyList<ServerProfileRow> GetAll()
    {
        var result = new List<ServerProfileRow>();
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, host, port, username, auth_kind, secret_cipher, key_passphrase_cipher, " +
            "color, notes, last_connected, sort_order FROM server_profiles " +
            "ORDER BY sort_order, name COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(Read(reader));
        return result;
    }

    /// <summary>بروفايل واحد بمعرّفه، أو null.</summary>
    public ServerProfileRow? Get(string id)
    {
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, host, port, username, auth_kind, secret_cipher, key_passphrase_cipher, " +
            "color, notes, last_connected, sort_order FROM server_profiles WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>يحذف بروفايلاً (no-op إن لم يوجد).</summary>
    public void Delete(string id)
    {
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM server_profiles WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>يحدّث ختم «آخر اتّصال» (unix-ms) لبروفايل.</summary>
    public void UpdateLastConnected(string id, long unixMs)
    {
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE server_profiles SET last_connected = $ts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$ts", unixMs);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, ServerProfileRow r)
    {
        command.Parameters.AddWithValue("$id", r.Id);
        command.Parameters.AddWithValue("$name", r.Name);
        command.Parameters.AddWithValue("$host", r.Host);
        command.Parameters.AddWithValue("$port", r.Port);
        command.Parameters.AddWithValue("$username", r.Username);
        command.Parameters.AddWithValue("$auth", r.AuthKind);
        command.Parameters.AddWithValue("$secret", (object?)r.SecretCipher ?? DBNull.Value);
        command.Parameters.AddWithValue("$keypass", (object?)r.KeyPassphraseCipher ?? DBNull.Value);
        command.Parameters.AddWithValue("$color", (object?)r.Color ?? DBNull.Value);
        command.Parameters.AddWithValue("$notes", (object?)r.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$last", (object?)r.LastConnectedUnixMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$sort", r.SortOrder);
    }

    private static ServerProfileRow Read(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt32(3),
        reader.GetString(4),
        reader.GetInt32(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetInt64(10),
        reader.GetInt32(11));
}
