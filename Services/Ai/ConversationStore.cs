using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TerminalLauncher.Services.Ai;

/// <summary>محادثة محفوظة: عنوانها ووقتها ونصّها المُنقَّح.</summary>
/// <param name="Id">معرّف الملفّ.</param>
/// <param name="Title">أوّل سؤال (مقتطعاً) كعنوان.</param>
/// <param name="SavedAt">وقت الحفظ.</param>
/// <param name="Transcript">نصّ المحادثة بعد التنقيح.</param>
public sealed record SavedConversation(string Id, string Title, DateTimeOffset SavedAt, string Transcript);

/// <summary>
/// حفظ المحادثات على القرص — <b>opt-in فقط</b>. لا يُكتب شيء ما لم يُفعّل المستخدم
/// <see cref="AiSettings.SaveConversations"/> صراحةً (معطَّل افتراضاً).
///
/// <para><b>الخصوصيّة أوّلاً:</b> كلّ محادثة تمرّ عبر مُنقّح الأسرار قبل الكتابة — الحفظ لا يجوز
/// أن يكون ثغرة تسرّب ما تحرسه بقيّة الطبقة. والملفّات محلّيّة لهذا الجهاز، وتشملها «مسح كل شيء».</para>
/// </summary>
public sealed class ConversationStore
{
    private const int MaxConversations = 200;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _dir;
    private readonly Func<string, string> _redact;
    private readonly Func<bool> _enabled;
    private readonly object _lock = new();

    /// <param name="redact">مُنقّح الأسرار — إلزاميّ، يُطبَّق قبل كلّ كتابة.</param>
    /// <param name="enabled">علَم الحفظ (opt-in).</param>
    /// <param name="directory">مجلد التخزين (افتراضيّه ضمن بيانات التطبيق).</param>
    public ConversationStore(Func<string, string> redact, Func<bool> enabled, string? directory = null)
    {
        _redact = redact ?? throw new ArgumentNullException(nameof(redact));
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _dir = directory ?? DefaultDir;
    }

    /// <summary>موقع التخزين الافتراضيّ: <c>%AppData%\HeliumRedTools\TerminalLauncher\ai-chats</c>.</summary>
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher", "ai-chats");

    /// <summary>
    /// يحفظ محادثة إن كان الحفظ مفعَّلاً. لا يفعل شيئاً حين معطَّل — هذا ما يجعل «معطَّل افتراضاً»
    /// ضمانةً لا وعداً. يعيد المعرّف عند الحفظ، وإلّا null.
    /// </summary>
    public string? Save(string transcript)
    {
        if (!_enabled() || string.IsNullOrWhiteSpace(transcript)) return null;

        string safe = _redact(transcript);
        string id = NewId();
        var record = new SavedConversation(id, TitleOf(safe), DateTimeOffset.UtcNow, safe);

        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(PathFor(id), JsonSerializer.Serialize(record, Json));
                Trim();
                return id;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null; // الحفظ ميزة مساعدة — لا يُسقط الجلسة
            }
        }
    }

    /// <summary>يعيد المحادثات المحفوظة (الأحدث أوّلاً). فارغة إن لا مجلد أو تعذّرت القراءة.</summary>
    public IReadOnlyList<SavedConversation> All()
    {
        var result = new List<SavedConversation>();
        lock (_lock)
        {
            if (!Directory.Exists(_dir)) return result;

            foreach (string file in Directory.EnumerateFiles(_dir, "*.json"))
            {
                try
                {
                    SavedConversation? record = JsonSerializer.Deserialize<SavedConversation>(File.ReadAllText(file), Json);
                    if (record is not null) result.Add(record);
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    // ملفّ تالف/مقفل — نتخطّاه بلا إسقاط البقيّة.
                }
            }
        }
        return result.OrderByDescending(c => c.SavedAt).ToList();
    }

    /// <summary>يحذف محادثة واحدة.</summary>
    public void Delete(string id)
    {
        lock (_lock)
        {
            try { File.Delete(PathFor(id)); }
            catch (IOException) { /* غير موجود/مقفل */ }
        }
    }

    /// <summary>يمسح كلّ المحادثات المحفوظة (جزء من «مسح كل شيء»).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            try
            {
                if (Directory.Exists(_dir))
                    foreach (string file in Directory.EnumerateFiles(_dir, "*.json"))
                        File.Delete(file);
            }
            catch (IOException) { /* أفضل جهد */ }
        }
    }

    /// <summary>يبقي أحدث <see cref="MaxConversations"/> ويحذف الأقدم.</summary>
    private void Trim()
    {
        var files = Directory.EnumerateFiles(_dir, "*.json")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(MaxConversations)
            .ToList();

        foreach (FileInfo old in files)
        {
            try { old.Delete(); } catch (IOException) { /* أفضل جهد */ }
        }
    }

    private static string TitleOf(string transcript)
    {
        string firstLine = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        firstLine = firstLine.Trim();
        return firstLine.Length <= 60 ? firstLine : firstLine[..60] + "…";
    }

    private string PathFor(string id) => Path.Combine(_dir, id + ".json");

    // معرّف زمنيّ الترتيب بلا Guid: يبقى القرص مرتّباً زمنيّاً بصريّاً.
    private static string NewId() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("D13");
}
