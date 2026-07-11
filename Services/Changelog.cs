using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TerminalLauncher.Services;

/// <summary>قسم واحد داخل نسخة (HIGHLIGHTS/NEW/IMPROVED/FIXED) مع بنوده الحرفية.</summary>
public sealed class ChangelogSection
{
    /// <summary>مفتاح القسم كما ورد في الملف (NEW | IMPROVED | FIXED | HIGHLIGHTS) — يُستعمل لجلب التسمية المترجمة.</summary>
    public string Key { get; init; } = "";
    public List<string> Items { get; } = new();
}

/// <summary>مدخلة نسخة واحدة (رقم + تاريخ + أقسام).</summary>
public sealed class ChangelogEntry
{
    public string Version { get; init; } = "";
    public string Date { get; init; } = "";
    public List<ChangelogSection> Sections { get; } = new();
}

/// <summary>
/// مُحلِّل CHANGELOG.md (بنية بسيطة قابلة للتحليل آلياً):
///   <c>## [x.y.z] - YYYY-MM-DD</c> ← ترويسة نسخة
///   <c>### NEW | IMPROVED | FIXED | HIGHLIGHTS</c> ← قسم
///   <c>- بند</c> ← عنصر (يُعرض حرفياً)
/// المصدر الوحيد ملف <c>CHANGELOG.md</c> المُدمَج كمورد؛ مع احتياط القراءة من القرص أثناء التطوير.
/// </summary>
public static class Changelog
{
    private static readonly Regex VersionRx =
        new(@"^##\s*\[?(?<ver>[0-9A-Za-z.\-]+)\]?\s*-\s*(?<date>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex SectionRx =
        new(@"^###\s*(?<name>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex BulletRx =
        new(@"^\s*[-*]\s+(?<text>.+?)\s*$", RegexOptions.Compiled);

    /// <summary>يحلّل النص الخام إلى قائمة نسخ مرتّبة بترتيب ظهورها (الأحدث أولاً في الملف).</summary>
    public static List<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        if (string.IsNullOrWhiteSpace(markdown)) return entries;

        ChangelogEntry? current = null;
        ChangelogSection? section = null;

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var mv = VersionRx.Match(raw);
            if (mv.Success)
            {
                current = new ChangelogEntry
                {
                    Version = mv.Groups["ver"].Value.Trim(),
                    Date = mv.Groups["date"].Value.Trim(),
                };
                entries.Add(current);
                section = null;
                continue;
            }

            if (current == null) continue; // تجاهل المقدّمة قبل أوّل نسخة

            var ms = SectionRx.Match(raw);
            if (ms.Success)
            {
                section = new ChangelogSection { Key = ms.Groups["name"].Value.Trim() };
                current.Sections.Add(section);
                continue;
            }

            var mb = BulletRx.Match(raw);
            if (mb.Success && section != null)
                section.Items.Add(mb.Groups["text"].Value.Trim());
        }

        // نُبقي الأقسام غير الفارغة فقط.
        foreach (var e in entries)
            e.Sections.RemoveAll(s => s.Items.Count == 0);

        return entries;
    }

    /// <summary>يقرأ النص الخام من المورد المُدمَج، مع احتياط ملفّ CHANGELOG.md على القرص.</summary>
    public static string ReadRaw()
    {
        // 1) المورد المُدمَج (LogicalName مضبوط في csproj: TerminalLauncher.CHANGELOG.md).
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("TerminalLauncher.CHANGELOG.md");
            if (s != null)
            {
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }
        }
        catch { /* نُكمل للاحتياط */ }

        // 2) احتياط: ملف CHANGELOG.md بجوار المشروع/الحلّ (بيئة التطوير) — نصعد الأدلّة.
        try
        {
            string? dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string p = Path.Combine(dir, "CHANGELOG.md");
                if (File.Exists(p)) return File.ReadAllText(p);
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
        }
        catch { /* لا شيء */ }

        return "";
    }

    /// <summary>كل النسخ محلّلةً من المصدر (الأحدث أولاً).</summary>
    public static List<ChangelogEntry> Load() => Parse(ReadRaw());

    /// <summary>مدخلة نسخة بعينها (أو null) — تُستعمل لعرض «ما الجديد» لنسخة محدّدة.</summary>
    public static ChangelogEntry? Entry(string version) =>
        Load().FirstOrDefault(e => e.Version == version);
}
