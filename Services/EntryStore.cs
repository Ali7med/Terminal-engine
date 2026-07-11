using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>
/// حفظ/تحميل الباثات والأوامر في ملف JSON تحت %AppData%.
/// </summary>
public sealed class EntryStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher");

    private static readonly string FilePath = Path.Combine(Dir, "entries.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public string StorePath => FilePath;

    public List<CommandEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<CommandEntry>>(json, Options) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void Save(IEnumerable<CommandEntry> entries)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(entries, Options);
        File.WriteAllText(FilePath, json);
    }
}
