using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>
/// حفظ/تحميل تعريفات التاكات (اسم + لون) في ملفّ JSON تحت %AppData% — بموازاة <see cref="ProjectStore"/>.
/// المشاريع تشير للتاكات بالاسم عبر <c>Project.Tags</c>.
/// </summary>
public sealed class TagStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher");

    private static readonly string FilePath = Path.Combine(Dir, "tags.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<Tag> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<Tag>>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch { return new(); }
    }

    public void Save(IEnumerable<Tag> tags)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(tags, Options));
    }
}
