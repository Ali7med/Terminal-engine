using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>حالة البروفايلات المخصّصة + معرّف البروفايل الافتراضيّ (تُحفَظ في JSON).</summary>
public sealed class ProfilesData
{
    /// <summary>البروفايلات المخصّصة التي أضافها المستخدم (مثل "php artisan serve").</summary>
    public List<ShellProfile> CustomProfiles { get; set; } = new();

    /// <summary>معرّف البروفايل الافتراضيّ (يُفتَح به التاب الجديد)؛ null = أوّل بروفايل متاح.</summary>
    public string? DefaultProfileId { get; set; }
}

/// <summary>
/// حفظ/تحميل البروفايلات المخصّصة والبروفايل الافتراضيّ في JSON تحت %AppData% (T-101.3).
/// يحتذي <see cref="EntryStore"/>/<see cref="SettingsStore"/> في المسار وصيغة التسلسل.
/// </summary>
public sealed class ProfileStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher");

    private static readonly string FilePath = Path.Combine(Dir, "profiles.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ProfilesData Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<ProfilesData>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void Save(ProfilesData data)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Options));
        }
        catch
        {
            // تجاهل أخطاء الحفظ الطارئة.
        }
    }
}
