using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// قارئ Server-Sent Events <b>متسامح</b>. المنصّات «المتوافقة مع OpenAI» تختلف في تفاصيل البثّ:
/// بعضها يرسل تعليقات (<c>:</c>) نبضاتِ إبقاءٍ حيّ، وبعضها لا يُنهي بـ<c>[DONE]</c> إطلاقاً،
/// وبعضها يضيف حقولاً (<c>event:</c>، <c>id:</c>) لا تعنينا. لذلك: نتجاهل كلّ ما لا نفهمه بدل
/// الانهيار، ولا نفترض وجود <c>[DONE]</c> — انتهاء التيّار هو نهاية الردّ.
///
/// <para>الخمول: قراءة كلّ سطر محدودة بـ<c>idleTimeout</c>. مهلة <see cref="System.Net.Http.HttpClient"/>
/// الكلّيّة لا تصلح للبثّ (تقطع ردّاً طويلاً سليماً)، وتعطيلها يترك البثّ معلّقاً للأبد عند موت
/// الاتّصال صامتاً — فالمهلة الصحيحة هي «بلا بايتات لمدّة كذا».</para>
/// </summary>
internal static class SseReader
{
    /// <summary>
    /// يقرأ التيّار ويُنتج حمولة كلّ حقل <c>data:</c> (بعد وصل الأسطر المتعدّدة بسطر جديد).
    /// يتوقّف عند <c>[DONE]</c> أو عند نهاية التيّار — أيّهما أوّلاً.
    /// </summary>
    /// <param name="stream">تيّار الاستجابة.</param>
    /// <param name="idleTimeout">أقصى مدّة بلا أيّ بايتات قبل اعتبار الاتّصال ميتاً.</param>
    /// <param name="onIdleTimeout">يُستدعى عند انتهاء مهلة الخمول كي يرمي المستدعي خطأه المُطبَّع.</param>
    /// <param name="ct">إلغاء المستخدم.</param>
    public static async IAsyncEnumerable<string> ReadAsync(
        Stream stream,
        TimeSpan idleTimeout,
        Action onIdleTimeout,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var data = new StringBuilder();

        while (true)
        {
            string? line = await ReadLineOrTimeoutAsync(reader, idleTimeout, onIdleTimeout, ct).ConfigureAwait(false);

            // نهاية التيّار: نُخرج ما تبقّى في العازل (بعض المنصّات لا تُنهي بسطر فارغ).
            if (line is null)
            {
                if (data.Length > 0) yield return data.ToString();
                yield break;
            }

            // سطر فارغ = نهاية حدث؛ نُخرج ما تجمّع.
            if (line.Length == 0)
            {
                if (data.Length == 0) continue;
                string payload = data.ToString();
                data.Clear();
                if (IsDone(payload)) yield break;
                yield return payload;
                continue;
            }

            // تعليق/نبضة إبقاء حيّ.
            if (line[0] == ':') continue;

            int colon = line.IndexOf(':');
            string field = colon < 0 ? line : line[..colon];
            if (!string.Equals(field, "data", StringComparison.Ordinal))
                continue; // event / id / retry — لا تعنينا

            // حسب معيار SSE: تُحذف مسافة واحدة بعد النقطتين إن وُجدت.
            string value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];

            if (data.Length > 0) data.Append('\n');
            data.Append(value);
        }
    }

    /// <summary>
    /// يقرأ سطراً بمهلة خمول. عند انتهاء المهلة (لا عند إلغاء المستخدم) يُستدعى
    /// <paramref name="onIdleTimeout"/> الذي يرمي خطأً مُطبَّعاً.
    /// </summary>
    private static async Task<string?> ReadLineOrTimeoutAsync(
        StreamReader reader,
        TimeSpan idleTimeout,
        Action onIdleTimeout,
        CancellationToken ct)
    {
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idleCts.CancelAfter(idleTimeout);
        try
        {
            return await reader.ReadLineAsync(idleCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            onIdleTimeout(); // يرمي AiException(Timeout)
            throw;           // احتياط: لو لم يرمِ المُفوَّض
        }
    }

    /// <summary>هل الحمولة علامة نهاية؟ (لا نفترض وجودها، لكن نحترمها إن وصلت.)</summary>
    private static bool IsDone(string payload)
        => payload.AsSpan().Trim().SequenceEqual("[DONE]");
}
