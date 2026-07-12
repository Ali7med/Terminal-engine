using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>
/// حفظ/تحميل تعريفات المشاريع/التصنيفات (اسم + لون) في ملف JSON تحت %AppData% — بموازاة
/// <see cref="EntryStore"/>. الأوامر تشير للمشاريع بالاسم عبر <c>CommandEntry.Tags</c>.
/// </summary>
public sealed class ProjectStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher");

    private static readonly string FilePath = Path.Combine(Dir, "projects.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<Project> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<Project>>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch { return new(); }
    }

    public void Save(IEnumerable<Project> projects)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(projects, Options));
    }
}
