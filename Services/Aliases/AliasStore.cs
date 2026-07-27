using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TerminalLauncher.Services.Aliases;

/// <summary>
/// تخزين الأسماء المستعارة في <c>%AppData%\HeliumRedTools\TerminalLauncher\aliases.json</c>.
///
/// <para>الكتابة <b>ذرّيّة</b> (ملفّ مؤقّت ثمّ استبدال): انقطاع أثناء الحفظ لا يجوز أن يترك المستخدم
/// بملفّ نصفه قديم ونصفه جديد فيفقد كلّ أسمائه.</para>
/// </summary>
public sealed class AliasStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();
    private List<CommandAlias>? _cache;

    public AliasStore(string? path = null) => _path = path ?? DefaultPath;

    /// <summary>
    /// المخزن المشترك للتطبيق. الأسماء المستعارة ملفٌّ واحد ومجموعة واحدة، فنسخٌ متعدّدة تعني
    /// مخبّآت تتباعد: تحرير في نافذة لا يراه تبويب آخر حتى إعادة التشغيل.
    /// </summary>
    public static AliasStore Shared { get; } = new();

    /// <summary>موقع الملفّ الافتراضيّ.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher", "aliases.json");

    /// <summary>كلّ الأسماء المستعارة (مخبَّأة بعد أوّل قراءة).</summary>
    public IReadOnlyList<CommandAlias> All()
    {
        lock (_lock)
        {
            _cache ??= Read();
            return _cache;
        }
    }

    /// <summary>
    /// يجد اسماً مستعاراً مفعَّلاً بكلمته، مع مراعاة الصدفة المستهدفة. يعيد null إن لا مطابقة —
    /// وعندها يمضي السطر إلى الصدفة كما هو.
    /// </summary>
    public CommandAlias? Find(string? word, string? shell)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;

        foreach (CommandAlias alias in All())
        {
            if (!alias.Enabled) continue;
            if (!string.Equals(alias.Name, word, StringComparison.OrdinalIgnoreCase)) continue;

            if (alias.Shell.Length > 0
                && (shell is null || shell.IndexOf(alias.Shell, StringComparison.OrdinalIgnoreCase) < 0))
                continue;

            return alias;
        }

        return null;
    }

    /// <summary>يضيف أو يستبدل اسماً مستعاراً بمعرّفه ثمّ يحفظ.</summary>
    public void Save(CommandAlias alias)
    {
        lock (_lock)
        {
            _cache ??= Read();
            int index = _cache.FindIndex(a => a.Id == alias.Id);

            if (index >= 0) _cache[index] = alias;
            else _cache.Add(alias);

            Write(_cache);
        }
    }

    /// <summary>يحذف اسماً مستعاراً بمعرّفه.</summary>
    public void Delete(string id)
    {
        lock (_lock)
        {
            _cache ??= Read();
            if (_cache.RemoveAll(a => a.Id == id) > 0) Write(_cache);
        }
    }

    private List<CommandAlias> Read()
    {
        try
        {
            if (!File.Exists(_path)) return new List<CommandAlias>();

            List<CommandAlias>? loaded = JsonSerializer.Deserialize<List<CommandAlias>>(
                File.ReadAllText(_path), Json);

            // اسم بلا كلمة أو بلا أوامر لا يمكن استدعاؤه — نتجاهله بدل عرض صفّ ميّت.
            return loaded?.Where(a => a.Name.Length > 0 && a.Commands.Count > 0).ToList()
                ?? new List<CommandAlias>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<CommandAlias>();   // ملفّ تالف لا يُسقط التطبيق
        }
    }

    private void Write(List<CommandAlias> aliases)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);

            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(aliases, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // الحفظ فشل — القائمة في الذاكرة تبقى صالحة لهذه الجلسة.
        }
    }
}
