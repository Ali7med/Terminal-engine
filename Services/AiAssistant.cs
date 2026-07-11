using System.Threading;
using System.Threading.Tasks;

namespace TerminalLauncher.Services;

/// <summary>سياق يُمرَّر لمساعد الـ AI: نصّ الأمر ومخرجاته ورمز خروجه.</summary>
public sealed class AiBlockContext
{
    /// <summary>نصّ الأمر المُنفَّذ (قد يكون فارغاً إن لم يُعرَف).</summary>
    public string CommandText { get; init; } = "";

    /// <summary>مخرجات الكتلة (قد تكون طويلة — للخدمة أن تقتطع).</summary>
    public string Output { get; init; } = "";

    /// <summary>رمز الخروج إن توفّر.</summary>
    public int? ExitCode { get; init; }
}

/// <summary>
/// واجهة مساعد الـ AI السياقيّ: يشرح كتلة (أمر + مخرجات) عند الطلب.
/// </summary>
public interface IAiAssistant
{
    /// <summary>هل الخدمة مُفعَّلة فعلاً (خلف علَم الإعدادات)؟</summary>
    bool IsEnabled { get; }

    /// <summary>يقترح شرحاً/تلخيصاً للكتلة. عند التعطيل يعيد رسالة إرشاديّة (لا استدعاء خارجيّ).</summary>
    Task<string> SuggestAsync(AiBlockContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// تنفيذ افتراضيّ <b>مُعطَّل (stub)</b> لمساعد الـ AI: لا يستدعي أيّ API خارجيّ ولا يخزّن أسراراً.
/// عند التعطيل يعيد رسالة تشرح أنّ الميزة اختياريّة وكيفيّة تفعيلها لاحقاً. مصمّم بواجهة نظيفة
/// (<see cref="IAiAssistant"/>) كي يُستبدَل بتنفيذ حقيقيّ لاحقاً دون تغيير المستدعين.
/// </summary>
public sealed class AiAssistant : IAiAssistant
{
    private readonly System.Func<bool> _isEnabled;

    /// <param name="isEnabled">دالّة تقرأ علَم <c>AppSettings.AiAssistantEnabled</c> حيّاً.</param>
    public AiAssistant(System.Func<bool> isEnabled) => _isEnabled = isEnabled;

    public bool IsEnabled => _isEnabled();

    public Task<string> SuggestAsync(AiBlockContext context, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.FromResult(
                "مساعد الـ AI غير مُفعَّل.\n" +
                "هذه ميزة اختياريّة. لتفعيلها لاحقاً: فعّل الخيار في الإعدادات " +
                "(AiAssistantEnabled) بعد ربط مزوّد فعليّ. لا يُستدعى أيّ API خارجيّ حاليّاً.");
        }

        // نقطة الوصل لتنفيذ حقيقيّ مستقبلاً (يُبنى في مرحلة لاحقة، ليس الآن).
        return Task.FromResult(
            "تكامل مساعد الـ AI غير مبنيّ بعد. الواجهة جاهزة للوصل بمزوّد لاحقاً.");
    }
}
