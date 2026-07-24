using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// واجهة مزوّد الذكاء الاصطناعيّ. تنفيذان فقط يغطّيان كلّ المنصّات المدعومة:
/// <see cref="OpenAiCompatProvider"/> (كلّ المنصّات المتوافقة مع OpenAI — بما فيها Kimi وZ.ai
/// وGemini عبر نقطته المتوافقة وOllama المحلّيّ) و<see cref="AnthropicProvider"/> (Messages API أصليّ).
/// <para>كلّ الأخطاء تُرمى كـ<see cref="AiException"/> مُصنَّفة — لا استثناءات شبكة خام تصل للواجهة.</para>
/// </summary>
public interface IAiProvider
{
    /// <summary>الاسم المعروض للمزوّد (يظهر في رسائل الأخطاء وترويسة اللوحة).</summary>
    string DisplayName { get; }

    /// <summary>
    /// يبثّ الردّ مقطعاً مقطعاً. الاستدعاء كسول: لا يُرسَل شيء قبل أوّل تعداد.
    /// يرمي <see cref="AiException"/> عند أيّ فشل، بما فيه انتهاء مهلة الخمول بين المقاطع.
    /// </summary>
    IAsyncEnumerable<AiDelta> ChatStreamAsync(
        IReadOnlyList<AiMessage> messages,
        AiChatOptions options,
        CancellationToken ct);

    /// <summary>يجلب معرّفات النماذج المتاحة. يعيد قائمة فارغة إن لم يدعم المزوّد ذلك.</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);

    /// <summary>
    /// نداء رخيص يتحقّق من الوصول والمصادقة معاً. لمزوّد بلا مفتاح (Ollama) يعني «هل الخدمة تعمل».
    /// لا يرمي أبداً — يعيد نتيجة مُصنَّفة كي تعرضها الإعدادات فوراً.
    /// </summary>
    Task<AiProbeResult> TestConnectionAsync(CancellationToken ct);
}
