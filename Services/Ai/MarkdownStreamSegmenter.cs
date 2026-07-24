using System;
using System.Collections.Generic;
using System.Text;

namespace TerminalLauncher.Services.Ai;

/// <summary>نوع المقطع المعروض.</summary>
public enum AiSegmentKind
{
    /// <summary>نصّ عاديّ (يتبع اتّجاه الواجهة).</summary>
    Text,

    /// <summary>كتلة كود مسيَّجة (خطّ أحاديّ + زرّ نسخ + LTR مفروض).</summary>
    Code,
}

/// <summary>مقطع قابل للإلحاق. الملحق الوحيد أثناء البثّ هو المقطع الأخير.</summary>
public sealed class AiSegment
{
    /// <summary>نوع المقطع.</summary>
    public AiSegmentKind Kind { get; }

    /// <summary>لغة الكتلة كما وردت بعد السياج (قد تكون فارغة).</summary>
    public string Language { get; }

    /// <summary>النصّ المتراكم (أسطر مكتملة فقط).</summary>
    public StringBuilder Text { get; } = new();

    internal AiSegment(AiSegmentKind kind, string language)
    {
        Kind = kind;
        Language = language ?? "";
    }
}

/// <summary>
/// ماسح تزايديّ يقسّم ردّ البثّ إلى مقاطع نصّ وكتل كود مسيَّجة (```).
///
/// <para><b>لماذا تزايديّ:</b> إعادة تحليل الرسالة كاملة عند كلّ نبضة تفريغ تجعل الكلفة تربيعيّة
/// مع الإجابات الطويلة (كلّ 80ms نُحلّل كلّ ما وصل). هذا الماسح يحتفظ بحالته (داخل سياج/خارجه +
/// ذيل سطر ناقص) ويكمل من آخر موضع، فالكلفة خطّيّة.</para>
///
/// <para><b>السياج قد يصل مقسوماً</b> بين دلتين (<c>``</c> ثمّ <c>`bash\n</c>)، ولذلك لا نقرّر شيئاً
/// قبل اكتمال السطر: الذيل الناقص يُحتجَز، وإن بدأ بعلامة اقتباس خلفيّة لا يُعرض أصلاً كي لا يومض
/// سياج نصّاً ثمّ يختفي.</para>
///
/// <para><b>فتح تفاؤليّ:</b> بمجرّد اكتمال سطر سياج افتتاحيّ يُفتح مقطع كود ويُصيَّر ما بعده كوداً
/// فوراً — لا ننتظر السياج الختاميّ. وعند انتهاء البثّ أو إلغائه تُغلَق أيّ كتلة مفتوحة بصريّاً.</para>
/// </summary>
public sealed class MarkdownStreamSegmenter
{
    private readonly List<AiSegment> _segments = new();
    private readonly StringBuilder _pending = new();
    private AiSegment? _current;
    private bool _inCode;

    /// <summary>المقاطع بترتيب ظهورها.</summary>
    public IReadOnlyList<AiSegment> Segments => _segments;

    /// <summary>هل هناك كتلة كود مفتوحة الآن؟</summary>
    public bool InCodeBlock => _inCode;

    /// <summary>
    /// الذيل الناقص المعروض. يُعرض ملحقاً بالمقطع الأخير كي لا يتأخّر النصّ سطراً كاملاً خلف
    /// البثّ — إلّا إن بدا بداية سياج، فيُحتجَز حتى يتّضح.
    /// </summary>
    public string PendingText => LooksLikeFenceStart(_pending) ? string.Empty : _pending.ToString();

    /// <summary>يضيف ما تجمّع في نبضة التفريغ. يُستدعى من خيط الواجهة كلّ ~80ms لا لكلّ دلتا.</summary>
    public void Append(string? chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;

        _pending.Append(chunk);

        while (true)
        {
            int newline = IndexOfNewline(_pending, out int skip);
            if (newline < 0) break;

            string line = _pending.ToString(0, newline);
            _pending.Remove(0, newline + skip);
            ConsumeLine(line);
        }
    }

    /// <summary>ينهي البثّ: يُفرِّغ الذيل الناقص ويُغلق أيّ كتلة كود مفتوحة بصريّاً.</summary>
    public void Complete()
    {
        if (_pending.Length > 0)
        {
            string tail = _pending.ToString();
            _pending.Clear();
            if (!IsFence(tail, out _)) AppendToCurrent(tail, newLineBefore: true);
        }
        _inCode = false;
    }

    /// <summary>يعيد المحادثة إلى الصفر (محادثة جديدة).</summary>
    public void Reset()
    {
        _segments.Clear();
        _pending.Clear();
        _current = null;
        _inCode = false;
    }

    /// <summary>كامل النصّ الخام كما وصل — لزرّ «نسخ المحادثة».</summary>
    public string RawText()
    {
        var sb = new StringBuilder();
        foreach (AiSegment segment in _segments)
        {
            if (segment.Kind == AiSegmentKind.Code)
            {
                sb.Append("```").Append(segment.Language).Append('\n');
                sb.Append(segment.Text);
                if (segment.Text.Length > 0 && segment.Text[^1] != '\n') sb.Append('\n');
                sb.Append("```\n");
            }
            else
            {
                sb.Append(segment.Text);
                if (segment.Text.Length > 0 && segment.Text[^1] != '\n') sb.Append('\n');
            }
        }
        sb.Append(_pending);
        return sb.ToString();
    }

    private void ConsumeLine(string line)
    {
        if (IsFence(line, out string language))
        {
            if (_inCode)
            {
                _inCode = false;
                _current = null; // السياج الختاميّ يُنهي الكتلة؛ ما بعده نصّ جديد
            }
            else
            {
                _inCode = true;
                _current = new AiSegment(AiSegmentKind.Code, language);
                _segments.Add(_current);
            }
            return;
        }

        AppendToCurrent(line, newLineBefore: true);
    }

    private void AppendToCurrent(string text, bool newLineBefore)
    {
        AiSegmentKind kind = _inCode ? AiSegmentKind.Code : AiSegmentKind.Text;

        if (_current is null || _current.Kind != kind)
        {
            _current = new AiSegment(kind, "");
            _segments.Add(_current);
        }
        else if (newLineBefore && _current.Text.Length > 0)
        {
            _current.Text.Append('\n');
        }

        _current.Text.Append(text);
    }

    /// <summary>سطر سياج = ثلاث علامات اقتباس خلفيّة فأكثر، وما بعدها اسم اللغة (اختياريّ).</summary>
    private static bool IsFence(string line, out string language)
    {
        language = "";
        ReadOnlySpan<char> span = line.AsSpan().Trim();
        if (span.Length < 3 || span[0] != '`' || span[1] != '`' || span[2] != '`') return false;

        int i = 3;
        while (i < span.Length && span[i] == '`') i++;
        language = span[i..].Trim().ToString();
        return true;
    }

    /// <summary>هل الذيل الناقص قد يكون بداية سياج؟ (نحتجزه حتى يكتمل السطر.)</summary>
    private static bool LooksLikeFenceStart(StringBuilder pending)
    {
        for (int i = 0; i < pending.Length; i++)
        {
            char c = pending[i];
            if (c == ' ' || c == '\t') continue;
            return c == '`';
        }
        return false;
    }

    /// <summary>موضع أوّل نهاية سطر مع طولها (يدعم \n و\r\n).</summary>
    private static int IndexOfNewline(StringBuilder buffer, out int skip)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == '\n')
            {
                skip = 1;
                return i;
            }
            if (buffer[i] == '\r')
            {
                bool crlf = i + 1 < buffer.Length && buffer[i + 1] == '\n';
                if (!crlf && i == buffer.Length - 1) break; // قد يصل \n في الدفعة التالية
                skip = crlf ? 2 : 1;
                return i;
            }
        }
        skip = 0;
        return -1;
    }
}
