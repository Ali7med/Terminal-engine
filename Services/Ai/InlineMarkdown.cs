using System;
using System.Collections.Generic;
using System.Text;

namespace TerminalLauncher.Services.Ai;

/// <summary>نوع مقطع مضمّن بعد التحليل.</summary>
public enum InlineKind
{
    /// <summary>نصّ عاديّ.</summary>
    Plain,

    /// <summary>عريض (**نصّ**).</summary>
    Bold,

    /// <summary>مائل (*نصّ*).</summary>
    Italic,

    /// <summary>كود مضمّن (`نصّ`) — يُعرض بخطّ أحاديّ واتّجاه LTR.</summary>
    Code,
}

/// <summary>مقطع مضمّن: نصّه ونوعه.</summary>
/// <param name="Kind">نوع التنسيق.</param>
/// <param name="Text">النصّ الظاهر (بلا علامات Markdown).</param>
public readonly record struct InlineSpan(InlineKind Kind, string Text);

/// <summary>سطر مُحلَّل: مقاطعه المضمّنة ومستوى العنوان/القائمة.</summary>
/// <param name="Spans">مقاطع السطر.</param>
/// <param name="HeadingLevel">مستوى العنوان (0 = ليس عنواناً).</param>
/// <param name="BulletDepth">عمق نقطة القائمة (0 = ليس عنصر قائمة).</param>
public sealed record MarkdownLine(IReadOnlyList<InlineSpan> Spans, int HeadingLevel, int BulletDepth);

/// <summary>
/// محلّل Markdown مضمّن خفيف — لتصيير ردود الدردشة. يغطّي ما يظهر فعلاً في إجابات المساعد:
/// العريض والمائل والكود المضمّن والعناوين وقوائم النقاط. لا يحلّل كتل الكود المسيَّجة (يتولّاها
/// <see cref="MarkdownStreamSegmenter"/> قبله) ولا الجداول ولا الروابط المعقّدة.
///
/// <para><b>لماذا محلّل خاصّ لا مكتبة:</b> المشروع يتجنّب SDKات ثقيلة، والتغطية المطلوبة صغيرة
/// ومحدّدة. محلّل نقيّ هنا أخفّ وأقبل للاختبار من جرّ مكتبة Markdown كاملة.</para>
/// </summary>
public static class InlineMarkdown
{
    /// <summary>يحلّل نصّاً متعدّد الأسطر إلى أسطر مُنسَّقة.</summary>
    public static IReadOnlyList<MarkdownLine> Parse(string text)
    {
        var lines = new List<MarkdownLine>();
        if (string.IsNullOrEmpty(text)) return lines;

        foreach (string raw in text.Split('\n'))
            lines.Add(ParseLine(raw));

        return lines;
    }

    private static MarkdownLine ParseLine(string raw)
    {
        string line = raw.TrimEnd('\r');

        int heading = 0;
        int bullet = 0;

        // عنوان: # … ###### في بداية السطر.
        int hashStart = CountLeading(line, '#');
        if (hashStart is > 0 and <= 6 && hashStart < line.Length && line[hashStart] == ' ')
        {
            heading = hashStart;
            line = line[(hashStart + 1)..];
        }
        else
        {
            // نقطة قائمة: مسافات بادئة ثمّ - أو * أو + متبوعة بمسافة.
            int indent = CountLeading(line, ' ');
            string afterIndent = line[indent..];
            if (afterIndent.Length >= 2 && (afterIndent[0] is '-' or '*' or '+') && afterIndent[1] == ' ')
            {
                bullet = 1 + indent / 2;   // كلّ مسافتين مستوى تداخل
                line = afterIndent[2..];
            }
        }

        return new MarkdownLine(ParseInlines(line), heading, bullet);
    }

    /// <summary>يحلّل مقاطع سطر واحد: عريض/مائل/كود مضمّن + نصّ عاديّ.</summary>
    private static IReadOnlyList<InlineSpan> ParseInlines(string line)
    {
        var spans = new List<InlineSpan>();
        var plain = new StringBuilder();

        void FlushPlain()
        {
            if (plain.Length > 0)
            {
                spans.Add(new InlineSpan(InlineKind.Plain, plain.ToString()));
                plain.Clear();
            }
        }

        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];

            // كود مضمّن: `...` — يُؤخذ حرفيّاً بلا تفسير ما بداخله.
            if (c == '`')
            {
                int end = line.IndexOf('`', i + 1);
                if (end > i)
                {
                    FlushPlain();
                    spans.Add(new InlineSpan(InlineKind.Code, line[(i + 1)..end]));
                    i = end + 1;
                    continue;
                }
            }

            // عريض: **...** أو __...__.
            if ((c == '*' || c == '_') && i + 1 < line.Length && line[i + 1] == c)
            {
                int end = FindClosing(line, i + 2, $"{c}{c}");
                if (end > 0)
                {
                    FlushPlain();
                    spans.Add(new InlineSpan(InlineKind.Bold, line[(i + 2)..end]));
                    i = end + 2;
                    continue;
                }
            }

            // مائل: *...* أو _..._ (علامة واحدة).
            if (c == '*' || c == '_')
            {
                int end = FindClosing(line, i + 1, c.ToString());
                if (end > i + 1)   // غير فارغ
                {
                    FlushPlain();
                    spans.Add(new InlineSpan(InlineKind.Italic, line[(i + 1)..end]));
                    i = end + 1;
                    continue;
                }
            }

            plain.Append(c);
            i++;
        }

        FlushPlain();
        if (spans.Count == 0) spans.Add(new InlineSpan(InlineKind.Plain, ""));
        return spans;
    }

    private static int FindClosing(string line, int from, string token)
    {
        int idx = line.IndexOf(token, from, StringComparison.Ordinal);
        return idx;
    }

    private static int CountLeading(string s, char c)
    {
        int n = 0;
        while (n < s.Length && s[n] == c) n++;
        return n;
    }
}
