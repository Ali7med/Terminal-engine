using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Terminal.Storage;

namespace TerminalLauncher.Services.Ai;

/// <summary>ملفّ معرفة المستخدم المبنيّ: نصّه وإصداره ووقت بنائه.</summary>
/// <param name="Text">النصّ المحقون في البادئة الثابتة (فارغ = لا بيانات كافية بعد).</param>
/// <param name="Version">رقم إصدار متزايد — يميّز بناءً عن آخر عند قياس الأثر.</param>
/// <param name="BuiltAt">وقت البناء.</param>
/// <param name="ApproxTokens">تقدير تقريبيّ لحجمه بالتوكنز.</param>
public sealed record AiProfile(string Text, int Version, DateTimeOffset BuiltAt, int ApproxTokens)
{
    /// <summary>ملفّ فارغ (لا بيانات بعد).</summary>
    public static AiProfile Empty { get; } = new("", 0, DateTimeOffset.MinValue, 0);

    /// <summary>هل فيه ما يستحقّ الحقن؟</summary>
    public bool HasContent => Text.Length > 0;
}

/// <summary>
/// يلخّص قاعدة المعرفة إلى «ملفّ معرفة المستخدم» مضغوط يُحقن في بادئة البرومبت الثابتة.
///
/// <para><b>لماذا ملخّص لا سجلّ خام:</b> إرسال مئات الأوامر مع كلّ طلب فاتورة توكنز مفتوحة
/// واصطدام بنافذة السياق. الملخّص يعطي النموذج ما يفيده فعلاً — أدواتك وصدفتك وأخطاؤك المتكرّرة
/// — في مساحة ثابتة صغيرة.</para>
///
/// <para><b>سقف صارم:</b> <see cref="MaxTokens"/> تقريباً. بلا سقف يتضخّم الملخّص حتى يبتلع
/// الميزانية التي وُجد ليحفظها.</para>
///
/// <para><b>موضعه في البادئة الثابتة مقصود:</b> يسبق السياق المتغيّر فيستفيد من التخزين المؤقّت
/// للبرومبت عند المزوّدين الذين يدعمونه.</para>
/// </summary>
public sealed class AiProfileBuilder
{
    /// <summary>سقف حجم الملفّ تقريباً (توكنز).</summary>
    public const int MaxTokens = 1500;

    /// <summary>تقدير خشن: 4 محارف ≈ توكن. يكفي لحارس سقف، ولا يحتاج مُرمِّزاً حقيقيّاً.</summary>
    private const int CharsPerToken = 4;

    /// <summary>أقلّ عدد تشغيلات قبل اعتبار الأمر جزءاً من عادة المستخدم.</summary>
    private const int MinRunsToInclude = 3;

    private readonly Func<AiKnowledgeStore> _store;
    private int _version;

    public AiProfileBuilder(Func<AiKnowledgeStore> store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>آخر ملفّ مبنيّ (مخبَّأ). هو نفسه ما يُعرض في «ذاكرة التطبيق» حرفيّاً.</summary>
    public AiProfile Current { get; private set; } = AiProfile.Empty;

    /// <summary>
    /// يبني الملفّ من القاعدة. يُنادى على خيط خلفيّ عند خمول التطبيق — لا عند الإقلاع ولا في
    /// مسار الإرسال (بناء متزامن قبل كلّ رسالة يضيف تأخيراً محسوساً بلا مقابل).
    /// </summary>
    public AiProfile Build()
    {
        try
        {
            AiKnowledgeStore store = _store();
            IReadOnlyList<CommandStat> commands = store.TopCommands(limit: 60);
            IReadOnlyList<ErrorPattern> errors = store.RecentErrors(limit: 20);

            string text = Compose(commands, errors);
            Current = text.Length == 0
                ? AiProfile.Empty
                : new AiProfile(text, ++_version, DateTimeOffset.UtcNow, text.Length / CharsPerToken);
        }
        catch (Exception)
        {
            // القاعدة مقفلة/تالفة: نُبقي آخر ملفّ صالح بدل إسقاط الميزة.
        }

        return Current;
    }

    private static string Compose(IReadOnlyList<CommandStat> commands, IReadOnlyList<ErrorPattern> errors)
    {
        List<CommandStat> useful = commands
            .Where(c => !c.IsBanned && c.RunCount >= MinRunsToInclude)
            .ToList();

        if (useful.Count == 0) return "";

        var sb = new StringBuilder();
        sb.Append("## What I know about this user's terminal habits\n");
        sb.Append("(Derived locally from their own usage. Use it to tailor commands and examples.)\n\n");

        // الصدفات المستعملة: تجعل النموذج يقترح صياغة صحيحة بدل خليط PowerShell/bash.
        string[] shells = useful
            .Where(c => !string.IsNullOrEmpty(c.Shell))
            .GroupBy(c => c.Shell!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(c => c.RunCount))
            .Select(g => g.Key)
            .Take(3)
            .ToArray();

        if (shells.Length > 0)
            sb.Append("Shells they use: ").Append(string.Join(", ", shells)).Append('\n');

        // الأدوات المستعملة (أوّل كلمة من كلّ قالب) — أكثف إشارة في أقلّ مساحة.
        string[] tools = useful
            .Select(c => FirstWord(c.Template))
            .Where(w => w.Length > 0)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(15)
            .ToArray();

        if (tools.Length > 0)
            sb.Append("Tools they use most: ").Append(string.Join(", ", tools)).Append("\n\n");

        sb.Append("Frequent command shapes (placeholders replace variable parts):\n");
        AppendWithinBudget(sb, useful);

        List<ErrorPattern> solved = errors.Where(e => !string.IsNullOrWhiteSpace(e.Solution)).Take(6).ToList();
        if (solved.Count > 0)
        {
            sb.Append("\nErrors they hit before, and the fix they accepted:\n");
            foreach (ErrorPattern error in solved)
            {
                if (OverBudget(sb)) break;
                sb.Append("- ").Append(Shorten(error.Sample, 90))
                  .Append(" → ").Append(Shorten(error.Solution!, 90)).Append('\n');
            }
        }

        return Trim(sb.ToString());
    }

    private static void AppendWithinBudget(StringBuilder sb, List<CommandStat> commands)
    {
        foreach (CommandStat stat in commands.Take(30))
        {
            if (OverBudget(sb)) return;
            sb.Append("- ").Append(Shorten(stat.Template, 100))
              .Append(" (").Append(stat.RunCount.ToString(CultureInfo.InvariantCulture)).Append("×");

            if (stat.FailCount > 0)
                sb.Append(", fails ").Append(stat.FailCount.ToString(CultureInfo.InvariantCulture)).Append('×');

            sb.Append(")\n");
        }
    }

    private static bool OverBudget(StringBuilder sb) => sb.Length >= MaxTokens * CharsPerToken;

    private static string Trim(string text)
    {
        int limit = MaxTokens * CharsPerToken;
        return text.Length <= limit ? text : text[..limit];
    }

    private static string FirstWord(string template)
    {
        int space = template.IndexOf(' ');
        string word = space < 0 ? template : template[..space];
        return word.StartsWith('<') ? "" : word;   // قالب يبدأ بمتغيّر: لا اسم أداة فيه
    }

    private static string Shorten(string text, int max)
    {
        string single = text.Replace('\n', ' ').Trim();
        return single.Length <= max ? single : single[..max] + "…";
    }
}
