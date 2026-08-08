using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>غلاف ملفّ التصدير — نسخةٌ صريحة تسمح بتغيير الصيغة لاحقاً بلا كسر ملفّات الناس.</summary>
public sealed class DictionaryExport
{
    public int Version { get; set; } = 1;
    public string ExportedUtc { get; set; } = "";
    public List<DictionaryCommand> Commands { get; set; } = new();
}

/// <summary>نتيجة استيراد: كم أُضيف وكم حُدِّث وكم تُخطّي.</summary>
public readonly record struct ImportResult(int Added, int Updated, int Skipped)
{
    public int Total => Added + Updated + Skipped;
}

/// <summary>
/// قاموس الأوامر مخزَّناً في <c>dictionary.json</c> تحت <c>%AppData%</c>، مع تصديرٍ واستيراد.
///
/// <para><b>لماذا ملفّ مستقلّ لا جدولٌ في قاعدة البيانات:</b> الغرض المعلَن نقلُ الأوامر بين
/// الأجهزة ومشاركتها، فالملفّ النصّيّ المقروء هو الصيغة التي تُرسَل وتُراجَع وتُوضَع في مستودع.</para>
/// </summary>
public sealed class CommandDictionaryStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher");

    private static readonly string FilePath = Path.Combine(Dir, "dictionary.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // الأوامر تحوي أقواساً واقتباساً ومحارف عربيّة — بلا هذا تخرج مهرَّبةً فلا تُقرأ ولا تُراجَع.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private List<DictionaryCommand> _items = new();

    public string StorePath => FilePath;

    /// <summary>المدخلات كما هي في الذاكرة (مرتّبة كما حُفظت).</summary>
    public IReadOnlyList<DictionaryCommand> All => _items;

    public CommandDictionaryStore() => Load();

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { _items = new List<DictionaryCommand>(); return; }
            string json = File.ReadAllText(FilePath);
            _items = JsonSerializer.Deserialize<List<DictionaryCommand>>(json) ?? new List<DictionaryCommand>();
        }
        catch
        {
            // ملفّ تالف يجب ألّا يمنع التشغيل — نبدأ فارغين ولا نكتب فوقه حتّى يحفظ المستخدم شيئاً.
            _items = new List<DictionaryCommand>();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items, Options));
        }
        catch (Exception ex)
        {
            CrashReporter.Log(ex, "CommandDictionaryStore.Save");
        }
    }

    /// <summary>يضيف مدخلة جديدة ويحفظ.</summary>
    public void Add(DictionaryCommand item)
    {
        _items.Add(item);
        Save();
    }

    /// <summary>يستبدل مدخلةً بمعرّفها (أو يضيفها إن لم تكن موجودة) ويحفظ.</summary>
    public void Update(DictionaryCommand item)
    {
        int i = _items.FindIndex(x => x.Id == item.Id);
        if (i >= 0) _items[i] = item;
        else _items.Add(item);
        Save();
    }

    public void Remove(string id)
    {
        _items.RemoveAll(x => x.Id == id);
        Save();
    }

    /// <summary>يسجّل استعمالاً: يرفع العدّاد ويختم الوقت — فيتصدّر المتكرّرُ النتائجَ المتساوية.</summary>
    public void NoteUsed(string id)
    {
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item is null) return;
        item.UseCount++;
        item.LastUsedUtc = DateTime.UtcNow.ToString("O");
        Save();
    }

    /// <summary>
    /// يبحث بالمطابقة الضبابيّة ويعيد المدخلات مرتّبةً بالدرجة ثمّ بتكرار الاستعمال.
    /// استعلامٌ فارغ يعيد الكلّ مرتّباً بالأكثر استعمالاً.
    /// </summary>
    public List<DictionaryCommand> Search(string query)
    {
        query = (query ?? "").Trim();

        if (query.Length == 0)
            return _items
                .OrderByDescending(i => i.UseCount)
                .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        var scored = new List<(DictionaryCommand Item, int Score)>();
        foreach (var item in _items)
        {
            int s = FuzzyMatch.ScoreEntry(query, item.Title,
                string.Join(" ", string.Join(" ", item.Tags), item.Description, item.Command));
            if (s != FuzzyMatch.NoMatch) scored.Add((item, s));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.UseCount)
            .Select(x => x.Item)
            .ToList();
    }

    // ===== التصدير والاستيراد =====

    public void ExportTo(string path)
    {
        var payload = new DictionaryExport
        {
            Version = 1,
            ExportedUtc = DateTime.UtcNow.ToString("O"),
            Commands = _items.Select(i => i.Clone()).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, Options));
    }

    /// <summary>
    /// يستورد ملفّاً مُصدَّراً (أو قائمةً خاماً) ويدمجه: المعرّف الموجود يُحدَّث، والجديد يُضاف،
    /// والمطابقُ حرفيّاً في العنوان والأمر يُتخطّى.
    ///
    /// <para>الدمج لا الاستبدال: الاستيراد فعلُ إضافةٍ في ذهن المستخدم، ومسحُ قاموسه كلّه لأنّه فتح
    /// ملفّاً من زميلٍ خسارةٌ لا رجعة فيها.</para>
    /// </summary>
    public ImportResult ImportFrom(string path)
    {
        string json = File.ReadAllText(path);
        List<DictionaryCommand> incoming;

        try
        {
            incoming = JsonSerializer.Deserialize<DictionaryExport>(json)?.Commands
                       ?? new List<DictionaryCommand>();
        }
        catch (JsonException)
        {
            incoming = new List<DictionaryCommand>();
        }

        // احتياط: ملفّ يحمل القائمة مباشرةً بلا غلاف (تصديرٌ يدويّ أو نسخةٌ أقدم).
        if (incoming.Count == 0)
            incoming = JsonSerializer.Deserialize<List<DictionaryCommand>>(json) ?? new List<DictionaryCommand>();

        int added = 0, updated = 0, skipped = 0;
        foreach (var item in incoming)
        {
            if (string.IsNullOrWhiteSpace(item.Command) && string.IsNullOrWhiteSpace(item.Title)) { skipped++; continue; }
            if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");

            int byId = _items.FindIndex(x => x.Id == item.Id);
            if (byId >= 0) { _items[byId] = item; updated++; continue; }

            bool duplicate = _items.Any(x =>
                string.Equals(x.Title.Trim(), item.Title.Trim(), StringComparison.CurrentCultureIgnoreCase)
                && string.Equals(x.Command.Trim(), item.Command.Trim(), StringComparison.Ordinal));
            if (duplicate) { skipped++; continue; }

            _items.Add(item);
            added++;
        }

        Save();
        return new ImportResult(added, updated, skipped);
    }
}
