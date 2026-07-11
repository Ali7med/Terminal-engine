using System;
using System.Text.RegularExpressions;

namespace TerminalLauncher.Services;

/// <summary>نوع الهدف القابل للنقر المكتشَف في سطر التيرمنال.</summary>
public enum LinkTargetKind
{
    /// <summary>عنوان ويب (http/https).</summary>
    Url,
    /// <summary>مسار في نظام الملفّات (ملفّ أو مجلّد)، مع رقم سطر اختياريّ.</summary>
    Path,
}

/// <summary>
/// هدف قابل للنقر مكتشَف تحت المؤشّر: نوعه، نصّه الخام، ومداه العموديّ في السطر
/// (<see cref="StartCol"/> شامل، <see cref="EndCol"/> حصريّ) لأغراض التسطير/التمييز.
/// </summary>
public readonly struct LinkTarget
{
    public LinkTarget(LinkTargetKind kind, string value, int startCol, int endCol, int? line = null)
    {
        Kind = kind;
        Value = value;
        StartCol = startCol;
        EndCol = endCol;
        Line = line;
    }

    /// <summary>نوع الهدف (عنوان ويب أم مسار).</summary>
    public LinkTargetKind Kind { get; }

    /// <summary>النصّ الخام للهدف كما ظهر في السطر (بلا لاحقة <c>:line</c>).</summary>
    public string Value { get; }

    /// <summary>أوّل عمود يغطّيه الهدف (شامل، بفهرس محرف السلسلة).</summary>
    public int StartCol { get; }

    /// <summary>العمود التالي لآخر عمود يغطّيه الهدف (حصريّ، بفهرس محرف السلسلة).</summary>
    public int EndCol { get; }

    /// <summary>رقم السطر المستخرَج من لاحقة <c>:line</c> (إن وُجد) — لفتح المحرّر عليه.</summary>
    public int? Line { get; }
}

/// <summary>
/// وحدة كشف نقيّة (بلا حالة/واجهة) تستخرج الهدف القابل للنقر تحت عمود مؤشّر
/// من <b>سطر واحد</b> من نصّ التيرمنال (لا كامل الـbuffer). تُكتشَف: عناوين الويب
/// (http/https)، مسارات ويندوز المطلقة (<c>C:\...</c>)، المسارات النسبيّة (<c>.\x</c>, <c>./x</c>,
/// <c>app/Http/Controller.php</c>)، ومسارات بأرقام أسطر (<c>file.ext:42</c>).
/// المنطق منفصل عن الفتح ليكون قابلاً للاختبار وحده.
/// </summary>
public static class LinkDetector
{
    // عنوان ويب صريح. نوقف عند المحارف التي لا تكون عادةً جزءاً من الرابط.
    private static readonly Regex UrlRx = new(
        @"https?://[^\s""'<>()\[\]{}]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // مسار ويندوز مطلق: حرف قرص ثمّ :\ ثمّ مقاطع (بلا فراغ/محارف اقتباس)، مع لاحقة :line اختياريّة.
    private static readonly Regex WinAbsRx = new(
        @"[A-Za-z]:[\\/][^\s""'<>|?*]+?(?::(?<line>\d+))?(?=[\s""'<>|?*]|$)",
        RegexOptions.Compiled);

    // مسار نسبيّ/يونكسيّ: يبدأ بـ ./ أو .\ أو ../ أو مقطع فيه شرطة مائلة (segment/segment...)،
    // مع امتداد أو بدونه، ولاحقة :line اختياريّة. يلتقط مسارات أخطاء Laravel مثل app/Http/X.php:25.
    private static readonly Regex RelPathRx = new(
        @"(?:\.{1,2}[\\/])?[\w.\-]+(?:[\\/][\w.\-]+)+(?::(?<line>\d+))?",
        RegexOptions.Compiled);

    /// <summary>
    /// يكتشف الهدف القابل للنقر الذي يغطّي العمود <paramref name="col"/> في <paramref name="line"/>.
    /// الأولويّة: عنوان ويب ثمّ مسار ويندوز مطلق ثمّ مسار نسبيّ. يعيد <c>null</c> إن لا هدف تحت العمود.
    /// </summary>
    /// <param name="line">نصّ السطر المسطّح (بلا أنماط).</param>
    /// <param name="col">فهرس المحرف (عمود) الذي نقر عليه المستخدم داخل <paramref name="line"/>.</param>
    public static LinkTarget? Detect(string? line, int col)
    {
        if (string.IsNullOrEmpty(line)) return null;
        if (col < 0) col = 0;
        if (col > line.Length) col = line.Length;

        // 1) عنوان ويب صريح (الأولويّة العليا).
        if (MatchCovering(UrlRx, line, col) is { } url)
            return new LinkTarget(LinkTargetKind.Url, url.Value, url.Index, url.Index + url.Length);

        // 2) مسار ويندوز مطلق.
        if (MatchCovering(WinAbsRx, line, col) is { } win)
            return MakePathTarget(win);

        // 3) مسار نسبيّ/يونكسيّ (قد يحمل :line).
        if (MatchCovering(RelPathRx, line, col) is { } rel)
            return MakePathTarget(rel);

        return null;
    }

    /// <summary>يبني هدف مسار من مطابقة، فاصلاً لاحقة <c>:line</c> عن قيمة المسار.</summary>
    private static LinkTarget MakePathTarget(Match m)
    {
        int? lineNo = null;
        var g = m.Groups["line"];
        string value = m.Value;
        int endCol = m.Index + m.Length;
        if (g.Success && int.TryParse(g.Value, out int n))
        {
            lineNo = n;
            // نقتطع لاحقة ":<digits>" من القيمة كي تبقى مساراً نقيّاً.
            int cut = value.LastIndexOf(':');
            if (cut > 1) value = value.Substring(0, cut);   // >1 كي لا نقطع نقطتَي حرف القرص "C:"
        }
        return new LinkTarget(LinkTargetKind.Path, value, m.Index, endCol, lineNo);
    }

    /// <summary>أوّل مطابقة تغطّي العمود المعطى (Index ≤ col ≤ Index+Length)، أو <c>null</c>.</summary>
    private static Match? MatchCovering(Regex rx, string line, int col)
    {
        foreach (Match m in rx.Matches(line))
        {
            if (m.Length == 0) continue;
            if (col >= m.Index && col <= m.Index + m.Length)
                return m;
        }
        return null;
    }
}
