using System;
using System.Windows;
using System.Windows.Controls;
using TerminalLauncher.Services;
using TerminalLauncher.Services.Ai;
using TerminalLauncher.Terminal;
using TerminalLauncher.Theme;

namespace TerminalLauncher.Controls;

/// <summary>
/// ربط لوحة مساعد الـAI بالتبويب وأفعال السياق. اللوحة <b>مملوكة للتبويب</b>: تُهيَّأ عند أوّل
/// فتح، وتُغلَق ويُلغى بثّها عند إغلاقه.
/// </summary>
public partial class TerminalTabView
{
    private AppSettings? _aiAppSettings;
    private AiKeyStore? _aiKeyStore;
    private AiContextBuilder? _aiContext;
    private Action? _aiSaveSettings;
    private Action? _aiOpenSettings;
    private Action<string>? _aiAllowToken;
    private AiLearningService? _aiLearning;
    private Func<AiProfile>? _aiProfile;
    private bool _aiPanelReady;

    /// <summary>بصمات الأخطاء التي عُرضت لها رقاقة في هذه الجلسة — منع تكرار الإزعاج.</summary>
    private readonly System.Collections.Generic.HashSet<string> _seenErrorChips = new(StringComparer.Ordinal);

    /// <summary>عدّاد التجاهلات المتتالية للرقاقة (ثلاثة ⇒ تفعيل الوضع الهادئ).</summary>
    private int _errorChipDismissals;

    /// <summary>
    /// يمرّر ما تحتاجه اللوحة من النافذة الرئيسة. لا يبني اللوحة بعد: البناء كسول عند أوّل فتح كي
    /// لا يدفع كلّ تبويب ثمن ميزة قد لا يستعملها.
    /// </summary>
    /// <param name="settings">الإعدادات الحيّة.</param>
    /// <param name="saveSettings">حفظ الإعدادات.</param>
    /// <param name="openAiSettings">يفتح شاشة إعدادات الـAI.</param>
    /// <param name="redactor">مُنقّح الأسرار المشترك (يعرف مفاتيح المستخدم وقائمة «ليس سرّاً»).</param>
    /// <param name="allowToken">يحفظ بصمة رمز أقرّ المستخدم أنّه ليس سرّاً.</param>
    public void AttachAi(
        AppSettings settings,
        Action saveSettings,
        Action openAiSettings,
        SecretRedactor redactor,
        Action<string> allowToken,
        AiLearningService learning,
        Func<AiProfile> profile)
    {
        _aiAppSettings = settings;
        _aiSaveSettings = saveSettings;
        _aiOpenSettings = openAiSettings;
        _aiAllowToken = allowToken;
        _aiLearning = learning;
        _aiProfile = profile;
        _aiKeyStore = new AiKeyStore(() => settings.Ai, saveSettings);
        _aiContext = new AiContextBuilder(redactor, () => settings.Ai.ContextCharLimit);
    }

    private void AiToggleButton_Click(object sender, RoutedEventArgs e)
        => SetAiPanelVisible(AiToggleButton.IsChecked == true);

    private void SetAiPanelVisible(bool show)
    {
        if (show) EnsureAiPanel();
        AiToggleButton.IsChecked = show;
        AiSidePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>يهيّئ اللوحة عند أوّل فتح فقط.</summary>
    private void EnsureAiPanel()
    {
        if (_aiPanelReady || _aiAppSettings is null || _aiKeyStore is null) return;

        AiSidePanel.Configure(_aiAppSettings.Ai, _aiKeyStore, _aiSaveSettings ?? (() => { }), _aiProfile);
        AiSidePanel.SettingsRequested += () => _aiOpenSettings?.Invoke();
        AiSidePanel.AllowToken += token => _aiAllowToken?.Invoke(token);
        _aiPanelReady = true;
    }

    // ===== الالتقاط والاستدعاء المحلّيّ =====

    /// <summary>
    /// يسجّل كتلة أمر مكتملة في قاعدة المعرفة، ويعرض رقاقة بعد الفشل.
    /// <para>لا يُنادى إلّا مرّة لكلّ كتلة (المستدعي يمنع التكرار بـ<c>_lastRecordedBlockCommand</c>)
    /// — حلقة التحديث ترى الكتلة المكتملة آلاف المرّات بعدها.</para>
    /// </summary>
    private void AiCaptureBlock(BlockSnapshot block, string command)
    {
        if (_aiLearning is null) return;

        bool failed = block.State == BlockState.Failed;
        string? errorLine = failed ? AiLearningService.FirstErrorLine(BlockOutputText(block)) : null;

        _aiLearning.RecordCommand(command, CurrentShellName(), WorkingDirectory, block.ExitCode, errorLine);

        if (failed && errorLine is not null) ShowErrorChip(block, errorLine);
    }

    /// <summary>
    /// رقاقة «اشرح هذا الخطأ؟» بعد أمر فاشل — تُعرض <b>مرّة لكلّ بصمة خطأ في الجلسة</b> فلا تتحوّل
    /// إلى إزعاج متكرّر. إن كان للبصمة حلّ محفوظ سابقاً تعرضه الرقاقة أوّلاً: استدعاء محلّيّ بصفر
    /// كلفة وبلا اتّصال.
    /// </summary>
    private void ShowErrorChip(BlockSnapshot block, string errorLine)
    {
        if (_aiAppSettings?.Ai.QuietMode == true) return;

        string fingerprint = global::Terminal.Storage.CommandTemplate.ErrorFingerprint(block.ExitCode, errorLine);
        if (!_seenErrorChips.Add(fingerprint)) return;   // بصمة عُرضت في هذه الجلسة

        global::Terminal.Storage.ErrorPattern? known = _aiLearning?.RecallSolution(block.ExitCode, errorLine);

        AiErrorChip.Show(
            known?.Solution is { Length: > 0 } solution ? solution : null,
            onExplain: () => AiHandleLastFailure(asFix: false),
            onDismiss: OnErrorChipDismissed,
            onInsert: command => AiInsertCommand(command));
    }

    /// <summary>
    /// ثلاثة تجاهلات متتالية = إشارة كافية: نقترح «الوضع الهادئ» بدل انتظار أن يبحث المستخدم
    /// عن مفتاح إطفائه في الإعدادات.
    /// </summary>
    private void OnErrorChipDismissed()
    {
        if (_aiAppSettings is null) return;
        if (++_errorChipDismissals < 3) return;

        _errorChipDismissals = 0;
        _aiAppSettings.Ai.QuietMode = true;
        _aiSaveSettings?.Invoke();
    }

    // ===== أفعال السياق =====

    /// <summary>
    /// «اشرح هذا»: يرسل النصّ المحدَّد. الفعل نفسه موافقة على مقتطفه المستهدف وحده — المستخدم
    /// حدّد النصّ فقرأه قبل أن يرسله، فلا يحتاج تفعيل «السياق المحيط».
    /// </summary>
    public void AiExplainSelection(string? selectedText)
    {
        if (_aiContext is null) return;

        string text = selectedText ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;

        SetAiPanelVisible(true);
        AiContextSnippet snippet = _aiContext.FromSelection(text, CurrentShellName(), WorkingDirectory);
        AiSidePanel.AskWithContext(Loc.T("ai.ctx.askExplain"), snippet);
    }

    /// <summary>
    /// «اشرح/أصلح آخر أمر فاشل»: مقتطفه محدود بحدود كتلة OSC 133 لا بآخر N سطر من الشاشة.
    /// بلا تكامل صدفة لا وجود موثوقاً لـ«آخر أمر فاشل»، فيُخبَر المستخدم بذلك بدل صمت.
    /// </summary>
    /// <param name="asFix">true = اطلب أمر إصلاح، false = اطلب شرحاً.</param>
    public void AiHandleLastFailure(bool asFix)
    {
        if (_aiContext is null) return;

        SetAiPanelVisible(true);

        AiContextSnippet? snippet = _aiContext.FromLastFailedCommand(
            _lastSnapshot, CurrentShellName(), WorkingDirectory);

        if (snippet is null)
        {
            AiSidePanel.ShowNotice(Loc.T("ai.ctx.noFailed"));
            return;
        }

        AiSidePanel.AskWithContext(Loc.T(asFix ? "ai.ctx.askFix" : "ai.ctx.askExplain"), snippet);
    }

    /// <summary>هل يوجد أمر فاشل الآن؟ (لتفعيل عناصر القائمة.)</summary>
    public bool HasFailedCommand => AiContextBuilder.HasFailedCommand(_lastSnapshot);

    /// <summary>
    /// يُدرج أمراً مقترَحاً في سطر الإدخال — <b>بلا تنفيذ</b>. سطر واحد بلا محرف سطر جديد أبداً:
    /// وجوده في اللصق يعني تنفيذاً فوريّاً، وهذه الأداة لا تنفّذ اقتراحاً تلقائيّاً في أيّ حال.
    /// </summary>
    public void AiInsertCommand(string? command)
    {
        string safe = RiskyCommandDetector.SanitizeForInsert(command);
        if (safe.Length == 0) return;

        Send(safe);
        FocusTerminal();
    }

    /// <summary>اسم الصدفة الحاليّة إن عُرفت (يرافق السياق كي يقترح النموذج صياغة صحيحة).</summary>
    private string? CurrentShellName()
    {
        object? selected = ShellCombo?.SelectedItem;
        return selected switch
        {
            Models.ShellProfile profile => profile.Name,
            null => null,
            _ => selected.ToString(),
        };
    }

    /// <summary>
    /// هل هناك ردّ قيد الاستقبال؟ يستعمله مغلِق التبويب للتحذير قبل الإغلاق — إغلاق صامت يُلغي
    /// ردّاً انتظره المستخدم يبدو عطلاً لا قراراً.
    /// </summary>
    public bool HasStreamingAiReply => _aiPanelReady && AiSidePanel.IsStreaming;

    /// <summary>نصّ التحذير عند إغلاق تبويب ببثّ جارٍ.</summary>
    public static string AiCloseWarning => Loc.T("ai.panel.closeWarn");

    /// <summary>يُغلق اللوحة ويُلغي أيّ بثّ — يُستدعى من مسار إغلاق التبويب.</summary>
    public void ShutDownAi()
    {
        if (_aiPanelReady) AiSidePanel.ShutDown();
    }
}
