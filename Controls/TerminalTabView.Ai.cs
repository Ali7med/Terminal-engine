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
    private Action? _aiOpenShellIntegration;
    private Func<CatalogSuggestion?>? _aiPollCatalog;
    private Action<string>? _aiSaveConversation;
    private ConversationStore? _aiConversationStore;
    private Action<CatalogSuggestion, string>? _aiAcceptCatalog;
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
        Func<AiProfile> profile,
        Action openShellIntegration,
        Func<CatalogSuggestion?> pollCatalog,
        Action<CatalogSuggestion, string> acceptCatalog,
        Action<string> saveConversation,
        ConversationStore? conversations = null)
    {
        _aiAppSettings = settings;
        _aiConversationStore = conversations;
        _aiSaveSettings = saveSettings;
        _aiOpenSettings = openAiSettings;
        _aiAllowToken = allowToken;
        _aiLearning = learning;
        _aiProfile = profile;
        _aiOpenShellIntegration = openShellIntegration;
        _aiPollCatalog = pollCatalog;
        _aiAcceptCatalog = acceptCatalog;
        _aiSaveConversation = saveConversation;
        _aiKeyStore = new AiKeyStore(() => settings.Ai, saveSettings);
        _aiContext = new AiContextBuilder(redactor, () => settings.Ai.ContextCharLimit);

        InitComposerAi();
    }

    private void AiToggleButton_Click(object sender, RoutedEventArgs e)
        => SetAiPanelVisible(AiToggleButton.IsChecked == true);

    /// <summary>يُظهر/يُخفي لوحة الذكاء — نقطة دخول عامّة لاختصار النافذة (Ctrl+P افتراضاً).</summary>
    public void ToggleAiPanel() => SetAiPanelVisible(AiSidePanel.Visibility != Visibility.Visible);

    /// <summary>
    /// عرض اللوحة وحجم نصّ دردشتها من الإعدادات. العرض يُطبَّق دائماً (العنصر موجود ولو مطويّاً)،
    /// وحجم النصّ حين تكون اللوحة مبنيّة — واللوحة غير المبنيّة تأخذه عند أوّل فتح.
    /// </summary>
    public void ApplyAiPanelMetrics(double width, double chatFontSize)
    {
        if (width > 0) AiSidePanel.Width = width;
        if (_aiPanelReady) AiSidePanel.ApplyChatFontSize(chatFontSize);
    }

    private void SetAiPanelVisible(bool show)
    {
        if (show) EnsureAiPanel();
        AiToggleButton.IsChecked = show;
        AiSidePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        // فتحُ اللوحة نيّةُ سؤال: المؤشّر يذهب إلى صندوق السؤال فوراً بدل أن يطلب نقرة زائدة.
        if (show) AiSidePanel.FocusInput();
        else Renderer.Focus();
    }

    /// <summary>يهيّئ اللوحة عند أوّل فتح فقط.</summary>
    private void EnsureAiPanel()
    {
        if (_aiPanelReady || _aiAppSettings is null || _aiKeyStore is null) return;

        AiSidePanel.Configure(
            _aiAppSettings.Ai, _aiKeyStore, _aiSaveSettings ?? (() => { }),
            _aiProfile, _aiSaveConversation, _aiConversationStore);
        AiSidePanel.SettingsRequested += () => _aiOpenSettings?.Invoke();
        AiSidePanel.AllowToken += token => _aiAllowToken?.Invoke(token);
        AiSidePanel.ReplyCompleted += OnInlineReplyCompleted;
        AiSidePanel.ReplyFailed += OnInlineReplyFailed;
        AiSidePanel.RunCodeRequested += RunCodeFromPanel;
        AiSidePanel.ApplyChatFontSize(_aiAppSettings.Ai.ChatFontSize);
        AiSidePanel.InsertCodeRequested += code => EditAiCommand(FirstCommandLine(code).Line);
        AiInline.OpenChatRequested += () => SetAiPanelVisible(true);
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
        else if (!failed) MaybeSuggestCatalog();
    }

    /// <summary>
    /// يعرض اقتراح حفظ أمر متكرّر في الكتالوج — إن ظهر مرشّح ولم تُفتح اللوحة بإزعاج. العرض في
    /// اللوحة (إن كانت مفتوحة) لا نافذة مقاطِعة؛ اقتراح واحد لكلّ قالب، والرفض دائم.
    /// </summary>
    private void MaybeSuggestCatalog()
    {
        if (_aiPollCatalog is null || !_aiPanelReady || AiSidePanel.Visibility != Visibility.Visible) return;

        CatalogSuggestion? suggestion = _aiPollCatalog();
        if (suggestion is null) return;

        AiSidePanel.ShowCatalogSuggestion(
            suggestion,
            onAccept: (s, name) => _aiAcceptCatalog?.Invoke(s, name));
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
            // تدهور رشيق: الميزة تحتاج OSC 133 غير المثبَّت — نعرض سببها وزرّ تفعيله بدل صمت.
            AiSidePanel.ShowNotice(
                Loc.T("ai.ctx.noFailed"),
                Loc.T("ai.osc.cta"),
                () => _aiOpenShellIntegration?.Invoke());
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

    /// <summary>اسم الصدفة الحاليّة للعرض — يستعمله معالج تكامل الصدفة لاستنتاج البروفايل.</summary>
    public string? CurrentShellDisplayName => CurrentShellName();

    /// <summary>يُغلق اللوحة ويُلغي أيّ بثّ — يُستدعى من مسار إغلاق التبويب.</summary>
    public void ShutDownAi()
    {
        if (_aiPanelReady) AiSidePanel.ShutDown();

        // فكّ الاشتراك في الأحداث الساكنة: هي ثابتة طوال عمر التطبيق، والتبويب لا — بقاؤها
        // مشتركةً يُبقي التبويب المُغلَق حيّاً في الذاكرة. يُنادى من CloseSession فيغطّي كلّ
        // مسارات الإغلاق (إغلاق تاب · إغلاق ما بعده · إغلاق التطبيق).
        if (_composerAiReady)
        {
            Loc.Changed -= ApplyComposerAiLanguage;
            WelcomeDismissedGlobally -= HideWelcomeCard;
            _composerAiReady = false;
        }
    }

    // ===== وضع الذكاء داخل صندوق الأوامر =====

    /// <summary>هل صار المبدّل جاهزاً؟ (يمنع الاشتراك المزدوج في حدث اللغة.)</summary>
    private bool _composerAiReady;

    /// <summary>
    /// وضع <b>هذا التبويب</b>. المبدّل ورمز الموجّه والنصّ الإرشاديّ عناصرُ هذا التبويب وحده، فربط
    /// السلوك بإعدادٍ مشترك كان يجعل تبويباً يُرسل إلى المساعد بينما مبدّله ما زال يقول «أمر» —
    /// أي يبتلع أمر صدفة ويحوّله سؤالاً. الإعداد المحفوظ يبقى مصدر الوضع الابتدائيّ لا أكثر.
    /// </summary>
    private bool _composerAiMode;

    /// <summary>هل الصندوق في وضع الذكاء الآن؟</summary>
    private bool ComposerAiMode => _composerAiMode;

    /// <summary>
    /// يهيّئ مبدّل الوضع وبطاقة الترحيب. يُنادى من <see cref="AttachAi"/>: قبله لا إعدادات معروفة،
    /// فكانت النصوص ستُكتب بلغة الافتراض لا بلغة المستخدم.
    /// </summary>
    private void InitComposerAi()
    {
        if (_composerAiReady) return;
        _composerAiReady = true;

        ApplyComposerAiLanguage();
        SetComposerAiMode(_aiAppSettings?.Ai.ComposerAiMode == true, persist: false);
        Loc.Changed += ApplyComposerAiLanguage;
        WelcomeDismissedGlobally += HideWelcomeCard;
        ShowWelcomeCardIfNeeded();
    }

    /// <summary>نصوص المبدّل والنصّ الإرشاديّ وبطاقة الترحيب — تتبع اللغة حيّاً.</summary>
    private void ApplyComposerAiLanguage()
    {
        ComposerModeCmdText.Text = Loc.T("ai.cmp.modeCommand");
        ComposerModeAiText.Text = Loc.T("ai.cmp.modeAi");
        ComposerModeSwitch.ToolTip = Loc.T("ai.cmp.switchTip");
        ComposerModelChip.ToolTip = Loc.T("ai.cmp.model");
        WelcomeTitle.Text = Loc.T("ai.welcome.title");
        WelcomeDismissText.Text = Loc.T("ai.welcome.dismiss");
        UpdateComposerPlaceholder(ComposerInput.Text);

        // بطاقة مصروفة = صفوف لا يراها أحد؛ إعادة بنائها مع كلّ تغيّر لغة في كلّ تبويب عملٌ ضائع.
        if (WelcomeCard.Visibility == Visibility.Visible) BuildWelcomeRows();
    }

    /// <summary>
    /// يبدّل وضع الصندوق ويعكسه في الرمز والنصّ الإرشاديّ والمبدّل. <paramref name="persist"/>
    /// معطَّل عند التهيئة كي لا تُكتب الإعدادات لمجرّد قراءتها.
    /// </summary>
    private void SetComposerAiMode(bool ai, bool persist = true)
    {
        _composerAiMode = ai;

        // الإعداد المحفوظ = الوضع الابتدائيّ للتبويبات القادمة، لا مفتاح مشترك يقلب المفتوحة منها.
        if (persist && _aiAppSettings is not null && _aiAppSettings.Ai.ComposerAiMode != ai)
        {
            _aiAppSettings.Ai.ComposerAiMode = ai;
            _aiSaveSettings?.Invoke();
        }

        ComposerModeCmdBtn.IsChecked = !ai;
        ComposerModeAiBtn.IsChecked = ai;
        ComposerGlyph.Text = ai ? "✨" : "❯";
        UpdateComposerPlaceholder(ComposerInput.Text);

        // وضع الذكاء لا اقتراحات أوامر فيه — إخفاؤها فوراً كي لا تبقى قائمة أوامر معلّقة فوق سؤال.
        if (ai) { HideSuggestions(); ClearGhost(); }

        // رقاقة النموذج لا معنى لها في وضع الأمر، وإخفاؤها يعيد المساحة للتيرمنال.
        ComposerModelChip.Visibility = ai ? Visibility.Visible : Visibility.Collapsed;
        if (ai) SyncComposerModelText();
        else ModelPickerPopup.IsOpen = false;
    }

    // ===== رقاقة النموذج ولوحة اختياره =====
    //
    // الرقاقة تجاور المسار: «أين أنا» و«من سيجيبني» سؤالان متجاوران، وجوابهما يجب أن يكون كذلك.
    // واللوحة تحلّ محلّ منسدلة: مئات النماذج داخل ComboBox في شريط ضيّق تمطّه وتقتطع الأسماء،
    // بينما اللوحة تعطي القائمة مساحتها وتضيف بحثاً ومرشّح «المجّانيّة» وخطوة اعتماد صريحة.

    /// <summary>صفّ في لوحة النماذج: المعرّف وشارة «مجّانيّ» إن كان المزوّد يبلّغ بذلك.</summary>
    private sealed record ModelRow(string Id, string Badge);

    private System.Collections.Generic.IReadOnlyList<AiModelInfo> _composerModels =
        System.Array.Empty<AiModelInfo>();
    private bool _composerModelsLoaded;

    /// <summary>يعرض النموذج الفعّال حاليّاً على الرقاقة.</summary>
    private void SyncComposerModelText()
    {
        if (!_aiPanelReady) EnsureAiPanel();
        if (!_aiPanelReady) return;

        string model = AiSidePanel.CurrentModel();
        ComposerModelText.Text = model.Length > 0 ? model : Loc.T("ai.cmp.noModel");
    }

    private async void ComposerModelChip_Click(object sender, RoutedEventArgs e)
    {
        if (!_aiPanelReady) EnsureAiPanel();
        if (!_aiPanelReady) return;

        // الاتّجاه صراحةً: محتوى الـPopup في شجرة منفصلة لا يرث اتّجاه الواجهة.
        ModelPickerRoot.FlowDirection = Loc.Flow;
        ModelPickerTitle.Text = Loc.T("ai.cmp.pickTitle");
        ModelPickerFreeOnly.Content = Loc.T("ai.set.freeOnly");
        ModelPickerApply.Content = Loc.T("ai.cmp.pickApply");
        ModelPickerCancel.Content = Loc.T("ui.cancel");
        Theme.Placeholder.SetText(ModelPickerSearch, Loc.T("ai.cmp.pickSearch"));
        ModelPickerPopup.IsOpen = true;

        if (!_composerModelsLoaded)
        {
            ModelPickerStatus.Text = Loc.T("ai.cmp.pickLoading");
            _composerModels = await AiSidePanel.LoadModelsAsync();
            _composerModelsLoaded = _composerModels.Count > 0;
        }

        ApplyModelFilter();
        _ = Dispatcher.BeginInvoke(new System.Action(() => ModelPickerSearch.Focus()),
                                   System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>يطبّق البحث ومرشّح المجّانيّة، ويُبقي المحدَّد على النموذج الفعّال.</summary>
    private void ApplyModelFilter()
    {
        string term = (ModelPickerSearch.Text ?? "").Trim();
        bool freeOnly = ModelPickerFreeOnly.IsChecked == true;

        var rows = new System.Collections.Generic.List<ModelRow>();
        foreach (AiModelInfo model in _composerModels)
        {
            if (freeOnly && model.IsFree != true) continue;
            if (term.Length > 0 && model.Id.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            rows.Add(new ModelRow(model.Id, model.IsFree == true ? Loc.T("ai.cmp.freeBadge") : ""));
        }

        ModelPickerList.ItemsSource = rows;

        string current = AiSidePanel.CurrentModel();
        foreach (ModelRow row in rows)
            if (row.Id == current) { ModelPickerList.SelectedItem = row; break; }

        // الفراغ له سببان: القائمة لم تصل (مفتاح ناقص أو شبكة)، أو المرشّح ضيّق. لا يُخلَطان.
        ModelPickerStatus.Text = _composerModels.Count == 0
            ? Loc.T("ai.cmp.pickNone")
            : rows.Count == 0 ? Loc.T("ai.cmp.pickNoMatch")
            : string.Format(Loc.T("ai.cmp.pickCount"), rows.Count, _composerModels.Count);
    }

    private void ModelPickerSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ModelPickerList is not null) ApplyModelFilter();
    }

    private void ModelPickerFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (ModelPickerList is not null) ApplyModelFilter();
    }

    private void ModelPickerList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ConfirmModelPick();

    private void ModelPickerApply_Click(object sender, RoutedEventArgs e) => ConfirmModelPick();

    private void ModelPickerCancel_Click(object sender, RoutedEventArgs e)
    {
        ModelPickerPopup.IsOpen = false;
        ComposerInput.Focus();
    }

    /// <summary>
    /// يعتمد المحدَّد: يصير <b>الافتراضيّ المحفوظ</b> للمساعد وليس لهذه الجلسة وحدها — لأنّ
    /// «اعتماد» في لوحةٍ مقصودةٍ يعني «هذا ما أريده من الآن»، لا تجربةً تُنسى عند إعادة التشغيل.
    /// </summary>
    private void ConfirmModelPick()
    {
        if (ModelPickerList.SelectedItem is not ModelRow row) return;

        if (_aiAppSettings is not null)
        {
            _aiAppSettings.Ai.Model = row.Id;
            _aiSaveSettings?.Invoke();
        }

        AiSidePanel.SetSessionModel(row.Id);
        SyncComposerModelText();

        ModelPickerPopup.IsOpen = false;
        ComposerInput.Focus();
    }

    /// <summary>النصّ الإرشاديّ يظهر ما دام الصندوق فارغاً، ونصّه يتبع الوضع.</summary>
    private void UpdateComposerPlaceholder(string text)
    {
        if (ComposerPlaceholder is null) return;
        ComposerPlaceholder.Text = Loc.T(ComposerAiMode ? "ai.cmp.hintAi" : "ai.cmp.hintCommand");
        ComposerPlaceholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ComposerModeCmd_Click(object sender, RoutedEventArgs e)
    {
        SetComposerAiMode(false);
        ComposerInput.Focus();
    }

    private void ComposerModeAi_Click(object sender, RoutedEventArgs e)
    {
        SetComposerAiMode(true);
        ComposerInput.Focus();
    }

    /// <summary>
    /// يُرسل ما في الصندوق إلى المساعد بدل الصدفة، مرفقاً بسياق هذا التبويب: الصدفة ومجلد العمل
    /// وآخر الأوامر. <b>مخرجات الشاشة لا تُرسَل</b> ما لم يفعّل المستخدم «إرسال سياق التبويب» —
    /// «يعرف أين أنت» لا يستلزم تصدير ما على شاشتك.
    /// </summary>
    private void SubmitComposerToAi(string text)
    {
        string question = text.Trim();
        if (question.Length == 0)
        {
            ComposerInput.Clear();
            ClearGhost();
            HideSuggestions();
            return;
        }

        // الحارس قبل التفريغ لا بعده: خروجٌ مبكّر بعد Clear كان يمحو سؤالاً لم يُرسَل، بلا رسالة
        // ولا طريقة لاسترجاعه.
        if (_aiContext is null || _aiAppSettings is null) return;

        ComposerInput.Clear();
        ClearGhost();
        HideSuggestions();

        // الجلسة تُبنى وتُسجّل فيها المحادثة — <b>بلا فتح اللوحة أبداً</b>. من سأل من التيرمنال
        // يتوقّع الجواب في التيرمنال، لا أن يُنقَل إلى شاشة أخرى.
        EnsureAiPanel();

        AiContextSnippet snippet = _aiAppSettings.Ai.AmbientContextEnabled
            ? _aiContext.FromAmbient(_lastSnapshot, CurrentShellName(), WorkingDirectory)
            : _aiContext.FromEnvironment(
                CurrentShellName(), WorkingDirectory, RecentCommandsForAi(), LastFailureForAi());

        string payload = AiContextBuilder.Compose(ComposerDirective + "\n\n" + question, snippet);

        // حُجب سرّ فعلاً ⇒ موافقة صريحة — لكنّ مكانها هذا الشريط لا فتح اللوحة. أمّا المعاينة
        // الروتينيّة (AlwaysPreview) فلا تنطبق على هذا المسار: طلبُ التنفيذ المباشر يعني ألّا
        // يقف في الطريق إلّا ما يستحقّ الوقوف فعلاً.
        if (snippet.ForcePreview)
        {
            AiInline.ShowConfirm(
                question,
                string.Format(Loc.T("ai.prev.redacted"), snippet.Redacted.Count),
                onSend: () => SendComposerAsk(question, payload),
                onCancel: () => AiInline.Hide());
            return;
        }

        SendComposerAsk(question, payload);
    }

    /// <summary>
    /// توجيه مختصر يرافق كلّ طلب من الصندوق: جملة واحدة ثمّ أمر واحد قابل للتنفيذ، أو
    /// <b>سؤال توضيحيّ واحد بلا كود</b> إن كان الطلب غامضاً. بلا هذا التوجيه يردّ النموذج
    /// بفقرات شرح أو يخمّن أمراً فيه موضع فارغ — وهذا المسار ينفّذ ما يعود، فالتخمين فيه أغلى.
    /// </summary>
    private static string ComposerDirective =>
        "You are answering from inside a terminal input box, and your command may be executed immediately. " +
        "Reply with AT MOST one short sentence, then exactly one runnable command in a fenced code block, " +
        "written for the shell named in the context below. " +
        "If the request is ambiguous, or you need information the context does not give you, DO NOT guess: " +
        "reply with a single short clarifying question and NO code block at all. " +
        "Never emit placeholders such as <name> or your-repo inside a command.";

    /// <summary>يُرسل ويبدأ عرض «يفكّر» المتحرّك.</summary>
    private void SendComposerAsk(string question, string payload)
    {
        _awaitingInlineReply = true;
        AiInline.ShowThinking(question);
        AiSidePanel.AskDirect(question, payload);
    }

    /// <summary>
    /// آخر أمر فاشل مختصراً (الأمر + رمز الخروج + أوّل سطر خطأ). «أين أنت» يشمل ما تعطّل للتوّ،
    /// وهو عادةً أهمّ سطر على الشاشة — سطر واحد لا مخرجات كاملة. null إن لم يفشل شيء.
    /// </summary>
    private string? LastFailureForAi()
    {
        ScreenSnapshot? snapshot = _lastSnapshot;
        if (snapshot is null) return null;

        for (int i = snapshot.Blocks.Count - 1; i >= 0; i--)
        {
            BlockSnapshot block = snapshot.Blocks[i];
            if (block.State != BlockState.Failed || block.EndLine == long.MaxValue) continue;

            string? error = AiLearningService.FirstErrorLine(BlockOutputText(block));
            return block.CommandText + " (exit " + block.ExitCode + ")"
                 + (error is null ? "" : " → " + error);
        }

        return null;
    }

    /// <summary>هل ننتظر ردّاً بدأ من صندوق الأوامر؟ (ردود اللوحة نفسها لا تخصّ الشريط.)</summary>
    private bool _awaitingInlineReply;

    /// <summary>
    /// يعرض الردّ في الشريط ويقرّر مصير الأمر المستخرَج. ثلاث حالات لا يُنفّذ فيها شيء
    /// تلقائيّاً: <b>أمر خطر</b> (مهما كان الإعداد)، أو <b>كتلة متعدّدة الأسطر</b> (سكربت لا أمر)،
    /// أو <b>إطفاء التنفيذ التلقائيّ</b>.
    /// </summary>
    private void OnInlineReplyCompleted(AiReplyParts parts)
    {
        if (!_awaitingInlineReply) return;
        _awaitingInlineReply = false;

        AiInline.ShowAnswer(parts.Text);

        (string first, bool multiLine) = FirstCommandLine(parts.Command);
        string command = RiskyCommandDetector.SanitizeForInsert(first);
        if (command.Length == 0)
        {
            // بلا أمر = النموذج يسأل أو يشرح. نُبقي وضع الذكاء ونعيد التركيز للصندوق كي تكمل
            // المحادثة في مكانها — الجلسة تحتفظ بالتاريخ فيصل جوابك مربوطاً بسؤاله.
            AiInline.ShowAwaitingAnswer();
            SetComposerAiMode(true, persist: false);
            ComposerInput.Focus();
            return;
        }

        if (RiskyCommandDetector.IsRisky(command))
        {
            AiInline.ShowRisky(command, () => RunAiCommand(command), () => EditAiCommand(command));
            return;
        }

        if (multiLine || _aiAppSettings?.Ai.AutoRunAiCommand != true)
        {
            AiInline.ShowSuggestion(command, () => RunAiCommand(command), () => EditAiCommand(command));
            return;
        }

        RunAiCommand(command);
        AiInline.ShowRan(command);
    }

    private void OnInlineReplyFailed(AiErrorView view)
    {
        if (!_awaitingInlineReply) return;
        _awaitingInlineReply = false;

        AiInline.ShowFailure(
            view.Message,
            view.Action == AiErrorAction.OpenSettings ? view.ActionLabel : null,
            () => _aiOpenSettings?.Invoke());
    }

    /// <summary>
    /// أوّل سطر أمر فعليّ من كتلة الكود (تُتخطّى الفوارغ والتعليقات)، مع علَم «الكتلة أطول
    /// من سطر» — وهو وحده يكفي لمنع التنفيذ التلقائيّ.
    /// </summary>
    private static (string Line, bool MultiLine) FirstCommandLine(string block)
    {
        var lines = new System.Collections.Generic.List<string>();
        foreach (string raw in block.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            lines.Add(line);
        }

        return lines.Count == 0 ? ("", false) : (lines[0], lines.Count > 1);
    }

    /// <summary>
    /// ينفّذ أمراً اقترحه المساعد في الصدفة مباشرةً. لا يمرّ بصندوق التأليف كي لا يبدّل وضعه
    /// ولا يمسح ما كتبه المستخدم فيه بينما كان الردّ يصل.
    /// </summary>
    private void RunAiCommand(string command)
    {
        string safe = RiskyCommandDetector.SanitizeForInsert(command);
        if (safe.Length == 0) return;

        RecordHistory(safe);
        lock (_screenLock) _coreScreen?.BeginHeuristicCommand(safe);
        MarkCommandStarted();
        Send(safe + _newline);
        ClearInputTracking();
        Renderer.ScrollOffset = 0;   // القفز للقاع كي تُرى المخرجات
    }

    /// <summary>يضع الأمر في الصندوق بوضع «أمر» ليراجعه المستخدم ويشغّله بنفسه.</summary>
    private void EditAiCommand(string command)
    {
        // تبديلٌ آليّ لا اختيارٌ من المستخدم: لا يُكتب في الإعدادات (انظر SetComposerAiMode).
        SetComposerAiMode(false, persist: false);
        ComposerInput.Text = RiskyCommandDetector.SanitizeForInsert(command);
        ComposerInput.CaretIndex = ComposerInput.Text.Length;
        ComposerInput.Focus();
    }

    /// <summary>
    /// «أرسل ونفّذ» من كتلة كود في لوحة الدردشة. يمرّ بنفس حارس الأوامر الخطرة الذي يمرّ به مسار
    /// الصندوق — زرٌّ في اللوحة لا يجوز أن يكون طريقاً جانبيّاً يتجاوز ما يقف أمامه في الطريق الرئيس.
    /// الخطر يُدرَج في الصندوق مع تنبيه، ولا يُنفَّذ.
    /// </summary>
    private void RunCodeFromPanel(string code)
    {
        string command = RiskyCommandDetector.SanitizeForInsert(FirstCommandLine(code).Line);
        if (command.Length == 0) return;

        if (RiskyCommandDetector.IsRisky(command))
        {
            EditAiCommand(command);
            NotificationService.Secondary(Loc.T("ai.ctx.risky"), NotificationType.Warning);
            return;
        }

        RunAiCommand(command);
    }

    /// <summary>آخر ثمانية أوامر من الجلسة — تكفي ليعرف المساعد ما كنت تفعل بلا تصدير الشاشة.</summary>
    private System.Collections.Generic.IReadOnlyList<string> RecentCommandsForAi()
    {
        const int max = 8;
        int count = Math.Min(max, _sessionCommands.Count);
        return count == 0
            ? Array.Empty<string>()
            : _sessionCommands.GetRange(_sessionCommands.Count - count, count);
    }

    // ===== بطاقة «جلسة تيرمنال جديدة» =====

    /// <summary>
    /// تظهر فوق الصندوق في التبويبات الجديدة حتى يصرفها المستخدم مرّة واحدة — ثمّ لا تعود أبداً.
    /// الاختصارات مكتوبة لا مخفيّة: مبدّل الوضع بلا دليل على وجوده ميزةٌ لا يجدها أحد.
    /// </summary>
    private void ShowWelcomeCardIfNeeded()
    {
        if (_aiAppSettings is null || _aiAppSettings.Ai.WelcomeCardDismissed) return;
        BuildWelcomeRows();
        WelcomeCard.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// «لا تُظهر ثانيةً» قرار عامّ لا قرار تبويب: الإعداد مشترك، أمّا البطاقة فعنصر في كلّ تبويب
    /// على حدة — فبلا بثّ تبقى معلّقة في بقيّة التبويبات المفتوحة ويصرفها المستخدم مرّةً لكلّ منها.
    /// </summary>
    private static event Action? WelcomeDismissedGlobally;

    private void HideWelcomeCard() => WelcomeCard.Visibility = Visibility.Collapsed;

    private void WelcomeDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_aiAppSettings is not null)
        {
            _aiAppSettings.Ai.WelcomeCardDismissed = true;
            _aiSaveSettings?.Invoke();
        }

        WelcomeDismissedGlobally?.Invoke();   // يشمل هذا التبويب أيضاً — فهو مشترك في الحدث
    }

    /// <summary>يبني صفوف الاختصارات (حبّة مفتاح + وصف) — تُعاد مع كلّ تغيّر لغة.</summary>
    private void BuildWelcomeRows()
    {
        if (WelcomeRows is null) return;

        WelcomeRows.Children.Clear();
        WelcomeRows.Children.Add(WelcomeRow("Ctrl+I", Loc.T("ai.welcome.aiMode")));
        WelcomeRows.Children.Add(WelcomeRow("↑ ↓", Loc.T("ai.welcome.history")));
        WelcomeRows.Children.Add(WelcomeRow("Tab", Loc.T("ai.welcome.complete")));
        WelcomeRows.Children.Add(WelcomeRow("Ctrl+F", Loc.T("ai.welcome.search")));
    }

    private static UIElement WelcomeRow(string keys, string description)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };

        var cap = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 3),
            MinWidth = 58,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cap.SetResourceReference(Border.BackgroundProperty, "Brush.KeyCap");

        // أحجام النصّ من موارد التطبيق لا أرقاماً ثابتة — فتتبع منزلق «حجم نصّ الواجهة» حيّاً.
        var keyText = new TextBlock
        {
            Text = keys,
            TextAlignment = TextAlignment.Center,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
        };
        keyText.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        keyText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        cap.Child = keyText;

        var desc = new TextBlock
        {
            Text = description,
            VerticalAlignment = VerticalAlignment.Center,
        };
        desc.SetResourceReference(TextBlock.FontSizeProperty, "Size.Ui");
        desc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        row.Children.Add(cap);
        row.Children.Add(desc);
        return row;
    }
}
