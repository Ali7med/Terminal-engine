using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Terminal.Storage;

/// <summary>
/// يخزّن أسماءً مخصّصةً لحاويات Docker، مفتاحها (معرّف بروفايل الخادم + مفتاح الحاوية = اسمها الحقيقيّ)
/// كي لا تتداخل حاويات خوادم مختلفة. جدول <c>container_names</c> في SQLite على نمط بقيّة المتاجر.
/// </summary>
public sealed class ContainerNameStore
{
    private readonly AppDatabase _db;

    public ContainerNameStore(AppDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _db.Execute(
            "CREATE TABLE IF NOT EXISTS container_names (" +
            "profile_id TEXT NOT NULL, " +
            "container_key TEXT NOT NULL, " +
            "custom_name TEXT NOT NULL, " +
            "PRIMARY KEY (profile_id, container_key))");
    }

    /// <summary>كلّ الأسماء المخصّصة لخادم: مفتاح الحاوية → الاسم المخصّص.</summary>
    public IReadOnlyDictionary<string, string> GetForProfile(string profileId)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT container_key, custom_name FROM container_names WHERE profile_id = $p;";
        command.Parameters.AddWithValue("$p", profileId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    /// <summary>كلّ الأسماء المخصّصة (لكلّ الخوادم) — لبحث الخوادم دون اتّصال.</summary>
    public IReadOnlyList<(string ProfileId, string ContainerKey, string CustomName)> GetAll()
    {
        var result = new List<(string, string, string)>();
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT profile_id, container_key, custom_name FROM container_names;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    /// <summary>يضبط اسماً مخصّصاً (upsert)؛ اسم فارغ يحذف المدخلة.</summary>
    public void Set(string profileId, string containerKey, string? customName)
    {
        if (string.IsNullOrWhiteSpace(customName)) { Remove(profileId, containerKey); return; }
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO container_names (profile_id, container_key, custom_name) VALUES ($p, $k, $n) " +
            "ON CONFLICT(profile_id, container_key) DO UPDATE SET custom_name = $n;";
        command.Parameters.AddWithValue("$p", profileId);
        command.Parameters.AddWithValue("$k", containerKey);
        command.Parameters.AddWithValue("$n", customName.Trim());
        command.ExecuteNonQuery();
    }

    /// <summary>يحذف الاسم المخصّص (no-op إن لم يوجد).</summary>
    public void Remove(string profileId, string containerKey)
    {
        using var connection = _db.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM container_names WHERE profile_id = $p AND container_key = $k;";
        command.Parameters.AddWithValue("$p", profileId);
        command.Parameters.AddWithValue("$k", containerKey);
        command.ExecuteNonQuery();
    }
}
