using System;
using System.Threading.Tasks;
using Terminal.Storage;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// واجهة التطبيق إلى قاعدة المعرفة المحلّيّة: تلتقط ما يفعله المستخدم وتستدعي ما تعلّمته.
///
/// <para><b>لا يعطّل الواجهة:</b> كلّ كتابة تجري على خيط خلفيّ. الالتقاط يحدث في حلقة تحديث
/// التيرمنال (كلّ 40ms)، وكتابة SQLite متزامنة هناك تعني تلعثماً محسوساً.</para>
///
/// <para><b>خلف علَم صريح:</b> إطفاء «التعلّم من استعمالي» يوقف كلّ كتابة فوراً — لا التقاط
/// صامت.</para>
/// </summary>
public sealed class AiLearningService
{
    private readonly Func<AiKnowledgeStore> _store;
    private readonly Func<bool> _enabled;

    /// <param name="store">مصنع قاعدة المعرفة (كسول: لا تُفتح القاعدة لمن لا يستعمل الميزة).</param>
    /// <param name="enabled">يقرأ علَم التعلّم حيّاً.</param>
    public AiLearningService(Func<AiKnowledgeStore> store, Func<bool> enabled)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
    }

    /// <summary>هل التسجيل مفعَّل الآن؟</summary>
    public bool IsEnabled => _enabled();

    /// <summary>
    /// يسجّل كتلة أمر مكتملة: القالب وعدّاداته، وبصمة الخطأ إن فشل.
    /// <paramref name="exitCode"/> فارغ حين لا يُعرف (بلا تكامل صدفة) — يُحصى التشغيل بلا حكم.
    /// </summary>
    public void RecordCommand(string command, string? shell, string? cwd, int? exitCode, string? firstErrorLine)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(command)) return;

        bool? succeeded = exitCode is null ? null : exitCode == 0;
        RunSafely(store =>
        {
            store.RecordCommand(command, shell, cwd, succeeded);
            if (succeeded == false && !string.IsNullOrWhiteSpace(firstErrorLine))
                store.RecordError(exitCode, firstErrorLine!);
        });
    }

    /// <summary>يسجّل حدثاً خفيفاً يتوسّطه التطبيق نفسه (تشغيل من الكتالوج، فعل دردشة، …).</summary>
    public void RecordEvent(string kind, string? detail = null)
    {
        if (!IsEnabled) return;
        RunSafely(store => store.RecordEvent(kind, detail));
    }

    /// <summary>
    /// يستدعي حلّاً سابقاً مقبولاً لنفس بصمة الخطأ. <b>محلّيّ بالكامل</b>: يعمل بلا اتّصال وبصفر
    /// كلفة API — وهو أوضح برهان على أنّ التطبيق تعلّم شيئاً فعلاً.
    /// يُنادى على خيط الواجهة (قراءة سريعة على فهرس)، ويعيد null بصمت عند أيّ تعذّر.
    /// </summary>
    public ErrorPattern? RecallSolution(int? exitCode, string? firstErrorLine)
    {
        if (string.IsNullOrWhiteSpace(firstErrorLine)) return null;

        try
        {
            string fingerprint = CommandTemplate.ErrorFingerprint(exitCode, firstErrorLine);
            ErrorPattern? pattern = _store().FindError(fingerprint);
            return string.IsNullOrWhiteSpace(pattern?.Solution) ? null : pattern;
        }
        catch (Exception)
        {
            return null; // قاعدة مقفلة/تالفة: الاستدعاء ميزة مساعدة لا يجوز أن تُسقط شيئاً
        }
    }

    /// <summary>يربط حلّاً قبِله المستخدم ببصمة خطأ — هذا ما يُعرض لاحقاً بلا نداء API.</summary>
    public void RememberSolution(int? exitCode, string? firstErrorLine, string solution)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(firstErrorLine) || string.IsNullOrWhiteSpace(solution)) return;

        string fingerprint = CommandTemplate.ErrorFingerprint(exitCode, firstErrorLine);
        RunSafely(store => store.SetErrorSolution(fingerprint, solution));
    }

    /// <summary>يحفظ بصمة رمز أقرّ المستخدم أنّه ليس سرّاً.</summary>
    public void AllowToken(string token) => RunSafely(store => store.AllowToken(token));

    /// <summary>تقليم دوريّ (احتفاظ الأحداث + سقف الحجم). يُنادى عند الخمول لا عند الإقلاع.</summary>
    public void MaintainInBackground() => RunSafely(store => store.Maintain());

    /// <summary>
    /// ينفّذ كتابة على خيط خلفيّ ويبتلع أخطاء التخزين. قاعدة المعرفة مساعدة: فشل الكتابة فيها
    /// يجب ألّا يقطع على المستخدم عمله في التيرمنال.
    /// </summary>
    private void RunSafely(Action<AiKnowledgeStore> work)
    {
        _ = Task.Run(() =>
        {
            try { work(_store()); }
            catch (Exception) { /* التخزين مساعد — لا يُسقط جلسة المستخدم */ }
        });
    }

    /// <summary>
    /// يستخرج أوّل سطر يبدو خطأً من مخرجات كتلة فاشلة — أساس البصمة. يفضّل الأسطر التي تحمل
    /// كلمة دالّة، وإلّا فآخر سطر غير فارغ (رسالة الفشل عادةً في الذيل).
    /// </summary>
    public static string? FirstErrorLine(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.Contains("error", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("fatal", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("exception", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("denied", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("failed", StringComparison.OrdinalIgnoreCase))
                return trimmed;
        }

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return null;
    }
}
