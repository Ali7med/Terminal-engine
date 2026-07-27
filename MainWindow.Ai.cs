using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TerminalLauncher.Models;
using TerminalLauncher.Services;
using TerminalLauncher.Services.Ai;

namespace TerminalLauncher;

/// <summary>
/// الجزء الخاصّ بإعدادات الذكاء الاصطناعيّ من النافذة الرئيسة: اختيار المزوّد والنموذج، إدخال
/// المفتاح (مُعمّى بـDPAPI)، واختبار الاتّصال. مفصول في ملفّ جزئيّ كي لا يتضخّم
/// <c>MainWindow.xaml.cs</c>.
/// </summary>
public partial class MainWindow
{
    private AiKeyStore? _aiKeys;
    private SecretRedactor? _aiRedactor;
    private global::Terminal.Storage.AiKnowledgeStore? _aiKnowledge;
    private AiLearningService? _aiLearning;
    private AiProfileBuilder? _aiProfileBuilder;
    private CommandCatalogBridge? _aiCatalogBridge;
    private ConversationStore? _aiConversations;
    private CancellationTokenSource? _aiProbeCts;
    private IReadOnlyList<AiModelInfo> _aiModels = Array.Empty<AiModelInfo>();
    private bool _aiSuppressModelFilter;

    /// <summary>حارس يمنع معالجات التغيير من الكتابة أثناء ملء الحقول برمجيّاً.</summary>
    private bool _aiSyncing;

    /// <summary>مخزن مفاتيح الـAI (يُنشأ عند أوّل طلب).</summary>
    private AiKeyStore AiKeys => _aiKeys ??= new AiKeyStore(() => _settings.Ai, SaveSettings);

    /// <summary>
    /// قاعدة المعرفة المحلّيّة. الحجب مُمرَّر للبانِي فيصير «لا سرّ يلمس القرص» شرطاً بنيويّاً لا
    /// تعليقاً في التوثيق.
    /// </summary>
    private global::Terminal.Storage.AiKnowledgeStore AiKnowledge =>
        _aiKnowledge ??= new global::Terminal.Storage.AiKnowledgeStore(
            new global::Terminal.Storage.AppDatabase(), AiRedactor.RedactText);

    /// <summary>
    /// المُنقّح المشترك: يعرف مفاتيح المستخدم المخزَّنة (فتُحجب لو ظهرت في خرج التيرمنال نفسه)
    /// وقائمة «ليس سرّاً» المحفوظة.
    /// </summary>
    private SecretRedactor AiRedactor => _aiRedactor ??= new SecretRedactor(
        storedKeys: () => AiKeys.AllPlainKeys(),
        allowedHashes: () => _aiKnowledge?.AllowedTokenHashes() ?? Array.Empty<string>());

    /// <summary>
    /// خدمة التعلّم: تلتقط على خيط خلفيّ وتستدعي محلّيّاً. مشتركة بين كلّ التبويبات — القاعدة
    /// واحدة والكاتب واحد.
    /// </summary>
    private AiLearningService AiLearning => _aiLearning ??= new AiLearningService(
        () => AiKnowledge, () => _settings.Ai.LearningEnabled);

    /// <summary>
    /// بانِي «ملفّ معرفة المستخدم». البناء يجري عند الخمول لا في مسار الإرسال: تلخيص متزامن قبل
    /// كلّ رسالة يضيف تأخيراً محسوساً بلا مقابل.
    /// </summary>
    private AiProfileBuilder AiProfiles => _aiProfileBuilder ??= new AiProfileBuilder(() => AiKnowledge);

    /// <summary>
    /// جسر «كتالوج الأوامر»: يطابق مرشّحي الحفظ مع الكتالوج الحاليّ بنفس المُطبِّع، فلا يُقترَح ما
    /// يملكه المستخدم أصلاً.
    /// </summary>
    private CommandCatalogBridge AiCatalog => _aiCatalogBridge ??= new CommandCatalogBridge(
        () => AiKnowledge, () => _entries, () => _settings.Ai.LearningEnabled);

    /// <summary>
    /// مخزن المحادثات — opt-in. الحفظ لا يجري ما لم يفعّله المستخدم؛ والتنقيح مُمرَّر للبانِي فلا
    /// تُكتب دردشة بلا حجب أسرارها.
    /// </summary>
    private ConversationStore AiConversations => _aiConversations ??= new ConversationStore(
        AiRedactor.RedactText, () => _settings.Ai.SaveConversations);

    /// <summary>
    /// يفحص إن ظهر أمر متكرّر يستحقّ اقتراح حفظه في الكتالوج، ويعرضه في اللوحة. يُنادى بعد التقاط
    /// كتلة ناجحة — العرض في اللوحة لا نافذة مقاطِعة.
    /// </summary>
    /// <returns>الاقتراح إن وُجد (وسُجِّل عرضه)، وإلّا null.</returns>
    private CatalogSuggestion? PollCatalogSuggestion()
    {
        CatalogSuggestion? suggestion = AiCatalog.NextSuggestion();
        if (suggestion is not null) AiCatalog.MarkShown(suggestion);
        return suggestion;
    }

    /// <summary>يحفظ اقتراح الكتالوج في الأوامر المحفوظة (قبول المستخدم).</summary>
    private void AcceptCatalogSuggestion(CatalogSuggestion suggestion, string name)
    {
        CommandEntry entry = AiCatalog.Accept(suggestion, name);
        _entries.Add(entry);
        _store.Save(_entries);
    }

    /// <summary>
    /// الملفّ المعروض والمحقون معاً — <b>مصدر واحد</b>. عرضٌ من مسار موازٍ كان سينحرف عمّا يُرسَل
    /// فعلاً، فيصير «هذا ما نعرفه عنك» ادّعاءً لا يمكن التحقّق منه.
    /// </summary>
    private AiProfile CurrentAiProfile => AiProfiles.Current;

    /// <summary>
    /// يعيد بناء الملفّ ويقلّم القاعدة على خيط خلفيّ. يُنادى عند الخمول (بعد إغلاق الإعدادات أو
    /// فتح ذاكرة التطبيق) لا عند الإقلاع.
    /// </summary>
    private void RefreshAiProfileInBackground()
    {
        if (!_settings.Ai.LearningEnabled) return;

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                AiProfiles.Build();
                AiKnowledge.Maintain();
            }
            catch (Exception) { /* قاعدة المعرفة مساعدة — لا تُسقط شيئاً */ }
        });
    }

    /// <summary>يحفظ بصمة رمز أقرّ المستخدم أنّه ليس سرّاً (البصمة لا الرمز).</summary>
    private void AiAllowToken(string token)
    {
        try { AiKnowledge.AllowToken(token); }
        catch (Microsoft.Data.Sqlite.SqliteException) { /* تعذّر الحفظ — يبقى الحجب فعّالاً */ }
    }

    /// <summary>يملأ حقول فئة الـAI من الإعدادات المحفوظة.</summary>
    private void SyncAiUi()
    {
        if (AiProviderCombo is null) return;

        _aiSyncing = true;
        try
        {
            // عناصر عرض (اسم فقط) — المدمجة ثمّ «مزوّد مخصّص» مترجَماً. تُعاد كلّ مرّة كي يتبع
            // اسم المخصّص اللغة الحاليّة.
            var choices = new System.Collections.Generic.List<AiProviderChoice>();
            foreach (AiProviderDescriptor d in AiProviderCatalog.All)
                choices.Add(new AiProviderChoice(d.Id, d.DisplayName));
            choices.Add(new AiProviderChoice(AiProviderCatalog.CustomId, Loc.T("ai.set.customProvider")));
            AiProviderCombo.ItemsSource = choices;

            AiSettings ai = _settings.Ai;
            AiProviderCombo.SelectedValue = ai.ProviderId;

            AiModelCombo.Text = AiProviderFactory.ResolveModel(ai);
            AiBaseUrlBox.Text = ai.BaseUrlOverride;
            AiLearningCheck.IsChecked = ai.LearningEnabled;
            AiAmbientCheck.IsChecked = ai.AmbientContextEnabled;
            AiPreviewCheck.IsChecked = ai.AlwaysPreview;
            AiQuietCheck.IsChecked = ai.QuietMode;
            AiSaveChatsCheck.IsChecked = ai.SaveConversations;
            AiAutoRunCheck.IsChecked = ai.AutoRunAiCommand;
            // التعيين يُقسَر إلى أقرب علامة (IsSnapToTickEnabled) ويُحصَر بين الحدّين، وValueChanged
            // مكتوم أثناء المزامنة — فنقرأ القيمة الفعليّة بعد التعيين ونعيدها إلى الإعدادات، وإلّا
            // عرض المنزلق رقماً وأرسل التطبيق آخر. والعرض يُكتب صراحةً لأنّ تعيين قيمة مطابقة لا
            // يُطلق الحدث أصلاً.
            AiTempSlider.Value = ai.Temperature;
            AiMaxTokensSlider.Value = ai.MaxTokens;
            AiCtxLimitSlider.Value = ai.ContextCharLimit;
            AiPanelWidthSlider.Value = ai.PanelWidth;
            AiChatFontSlider.Value = ai.ChatFontSize;

            ai.Temperature = AiTempSlider.Value;
            ai.MaxTokens = (int)AiMaxTokensSlider.Value;
            ai.ContextCharLimit = (int)AiCtxLimitSlider.Value;

            ShowAiTemp(ai.Temperature);
            ShowAiMaxTokens(ai.MaxTokens);
            ShowAiCtxLimit(ai.ContextCharLimit);

            AiPromptBox.Text = ai.SystemPromptExtra;
            AiModelCountText.Text = "";   // العدّاد يظهر بعد «تحديث القائمة»

            AiKeyBox.Clear();
            UpdateAiKeyState();
        }
        finally
        {
            _aiSyncing = false;
        }
    }

    /// <summary>
    /// يعرض حالة المفتاح على <b>هذا الجهاز</b>. التمييز بين «لا مفتاح» و«مفتاح مُعمّى على جهاز
    /// آخر» مقصود: الثانية ليست عطلاً بل نتيجة طبيعيّة لربط DPAPI بالحساب والجهاز، وعرضها
    /// كـ«مفتاح خاطئ» يرسل المستخدم لمطاردة مشكلة غير موجودة عند المزوّد.
    /// </summary>
    private void UpdateAiKeyState()
    {
        AiProviderDescriptor? descriptor = AiProviderCatalog.Find(_settings.Ai.ProviderId);
        if (descriptor is null) return;

        bool needsKey = !descriptor.Capabilities.KeyOptional;
        AiKeyBox.IsEnabled = needsKey;
        AiGetKeyBtn.IsEnabled = descriptor.KeysUrl.Length > 0;

        string text;
        Brush brush = (Brush)FindResource("Brush.TextMuted");

        if (!needsKey)
        {
            text = Loc.T("ai.set.noKeyNeeded");
        }
        else
        {
            switch (AiKeys.StateOf(descriptor.Id))
            {
                case AiKeyState.Present:
                    text = Loc.T("ai.set.keyStored");
                    brush = (Brush)FindResource("Brush.Success");
                    break;
                case AiKeyState.NeedsReentry:
                    text = Loc.T("ai.set.keyReentry");
                    brush = (Brush)FindResource("Brush.Danger");
                    break;
                default:
                    text = Loc.T("ai.set.keyMissing");
                    break;
            }
        }

        AiKeyStateText.Text = text;
        AiKeyStateText.Foreground = brush;
    }

    private void AiProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_aiSyncing || AiProviderCombo.SelectedValue is not string id) return;

        _settings.Ai.ProviderId = id;
        // النموذج والعنوان يخصّان المزوّد السابق — تصفيرهما يمنع إرسال معرّف نموذج لا يعرفه الجديد.
        _settings.Ai.Model = "";
        _settings.Ai.BaseUrlOverride = "";
        SaveSettings();

        _aiSyncing = true;
        try
        {
            AiModelCombo.ItemsSource = null;
            AiModelCombo.Text = AiProviderFactory.ResolveModel(_settings.Ai);
            AiBaseUrlBox.Text = "";
            AiKeyBox.Clear();
            AiTestResultText.Text = "";
            _aiModels = Array.Empty<AiModelInfo>();   // نماذج المزوّد السابق لم تعد تعني الجديد
            AiModelCountText.Text = "";
        }
        finally
        {
            _aiSyncing = false;
        }

        UpdateAiKeyState();
    }

    private void AiModel_Changed(object sender, RoutedEventArgs e)
    {
        if (_aiSyncing) return;
        _settings.Ai.Model = AiModelCombo.Text?.Trim() ?? "";
        SaveSettings();
    }

    private void AiBaseUrl_Changed(object sender, RoutedEventArgs e)
    {
        if (_aiSyncing) return;
        _settings.Ai.BaseUrlOverride = AiBaseUrlBox.Text?.Trim() ?? "";
        SaveSettings();
    }

    private void AiKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_aiSyncing) return;

        string entered = AiKeyBox.Password;
        if (entered.Length == 0) return; // المسح لا يحذف المفتاح المحفوظ؛ الحذف فعل صريح

        AiKeys.Set(_settings.Ai.ProviderId, entered);
        UpdateAiKeyState();
    }

    private void AiToggles_Changed(object sender, RoutedEventArgs e)
    {
        if (_aiSyncing) return;

        AiSettings ai = _settings.Ai;
        ai.LearningEnabled = AiLearningCheck.IsChecked == true;
        ai.AmbientContextEnabled = AiAmbientCheck.IsChecked == true;
        ai.AlwaysPreview = AiPreviewCheck.IsChecked == true;
        ai.QuietMode = AiQuietCheck.IsChecked == true;
        ai.SaveConversations = AiSaveChatsCheck.IsChecked == true;
        ai.AutoRunAiCommand = AiAutoRunCheck.IsChecked == true;
        SaveSettings();
    }

    private void AiGetKey_Click(object sender, RoutedEventArgs e)
    {
        AiProviderDescriptor? descriptor = AiProviderCatalog.Find(_settings.Ai.ProviderId);
        if (descriptor is not null && descriptor.KeysUrl.Length > 0)
            LinkOpener.OpenExplicit(descriptor.KeysUrl);
    }

    /// <summary>
    /// اختبار الاتّصال: نداء رخيص يُنهي التخمين. لمزوّد بلا مفتاح (Ollama) يعني «هل الخدمة تعمل».
    /// </summary>
    private async void AiTest_Click(object sender, RoutedEventArgs e)
    {
        AiProviderDescriptor? descriptor = AiProviderFactory.DescriptorFor(_settings.Ai);
        if (descriptor is null) return;

        _aiProbeCts?.Cancel();
        _aiProbeCts = new CancellationTokenSource();
        CancellationToken token = _aiProbeCts.Token;

        AiTestBtn.IsEnabled = false;
        AiTestResultText.Foreground = (Brush)FindResource("Brush.TextMuted");
        AiTestResultText.Text = Loc.T("ai.set.testing");

        try
        {
            IAiProvider provider = AiProviderFactory.CreateFor(
                descriptor, AiKeys.Get(descriptor.Id), _settings.Ai.BaseUrlOverride);

            AiProbeResult result = await provider.TestConnectionAsync(token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            AiTestResultText.Text = result.Detail;
            AiTestResultText.Foreground = (Brush)FindResource(result.Ok ? "Brush.Success" : "Brush.Danger");
        }
        finally
        {
            if (!token.IsCancellationRequested) AiTestBtn.IsEnabled = true;
        }
    }

    /// <summary>يجلب النماذج المتاحة فعلاً من المزوّد — هي مصدر الحقيقة لا الافتراضيّ المدمج.</summary>
    private async void AiRefreshModels_Click(object sender, RoutedEventArgs e)
    {
        AiProviderDescriptor? descriptor = AiProviderFactory.DescriptorFor(_settings.Ai);
        if (descriptor is null) return;

        AiRefreshModelsBtn.IsEnabled = false;
        try
        {
            IAiProvider provider = AiProviderFactory.CreateFor(
                descriptor, AiKeys.Get(descriptor.Id), _settings.Ai.BaseUrlOverride);

            _aiModels = await provider.ListModelsDetailedAsync(CancellationToken.None).ConfigureAwait(true);
            ApplyModelFilter();
        }
        catch (AiException ex)
        {
            AiErrorView view = AiErrorPresenter.Present(ex);
            AiTestResultText.Text = view.Message;
            AiTestResultText.Foreground = (Brush)FindResource("Brush.Danger");
        }
        finally
        {
            AiRefreshModelsBtn.IsEnabled = true;
        }
    }

    /// <summary>يعيد تطبيق فلتر «المجّاني فقط» على النماذج المجلوبة، ويحدّث العدّاد.</summary>
    private void AiFreeOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_aiSyncing) return;
        ApplyModelFilter();
    }

    /// <summary>
    /// يملأ قائمة النماذج حسب فلتر «المجّاني فقط» ويعرض عدّاداً (الإجمالي · المجّاني). حين لا يعطي
    /// المزوّد تسعيراً (معظم المنصّات) تُعرَض ملاحظة بدل عدّ مجّانيّ مضلّل.
    /// </summary>
    private void ApplyModelFilter()
    {
        if (_aiModels.Count == 0)
        {
            AiModelCountText.Text = "";
            return;
        }

        bool freeOnly = AiFreeOnlyCheck.IsChecked == true;
        int freeCount = _aiModels.Count(m => m.IsFree == true);
        bool hasPricing = _aiModels.Any(m => m.IsFree.HasValue);

        IEnumerable<AiModelInfo> shown = freeOnly ? _aiModels.Where(m => m.IsFree == true) : _aiModels;
        var ids = shown.Select(m => m.Id).ToList();

        string current = AiModelCombo.Text;
        _aiSuppressModelFilter = true;
        try
        {
            AiModelCombo.ItemsSource = ids;
            AiModelCombo.Items.Filter = null;
            AiModelCombo.Text = current;   // الجلب/الفلترة لا يغيّران اختيار المستخدم
        }
        finally
        {
            _aiSuppressModelFilter = false;
        }

        AiModelCountText.Text = hasPricing
            ? string.Format(Loc.T("ai.set.modelCount"), _aiModels.Count, freeCount)
            : string.Format(Loc.T("ai.set.modelCountNoPricing"), _aiModels.Count);

        AiModelCountText.Foreground = (Brush)FindResource(
            freeOnly && freeCount == 0 ? "Brush.Danger" : "Brush.TextMuted");
    }

    /// <summary>
    /// بحث ذكيّ في قائمة النماذج: أثناء الكتابة تُفلتَر المنسدلة إلى ما يحتوي النصّ (تطابق جزئيّ
    /// غير حسّاس لحالة الأحرف)، فيسهل إيجاد نموذج بين مئات النماذج. لا يغيّر النصّ المكتوب.
    /// </summary>
    private void AiModelText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_aiSyncing || _aiSuppressModelFilter) return;
        if (AiModelCombo.ItemsSource is null) return;

        string text = AiModelCombo.Text ?? "";
        AiModelCombo.Items.Filter = text.Length == 0
            ? null
            : o => o is string s && s.Contains(text, StringComparison.OrdinalIgnoreCase);

        // افتح المنسدلة أثناء البحث ما لم يكن النصّ مطابقاً تماماً لعنصر واحد (أي اختار المستخدم).
        if (text.Length > 0 && AiModelCombo.Items.Count > 0 && !AiModelCombo.Items.Contains(text))
            AiModelCombo.IsDropDownOpen = true;
    }

    /// <summary>
    /// فتح المنسدلة يرفع فلتر البحث متى كان النصّ هو النموذج المختار فعلاً — وإلّا رأى
    /// المستخدم قائمة من عنصر واحد وظنّ أنّ بقيّة النماذج اختفت.
    /// </summary>
    private void AiModelCombo_DropDownOpened(object? sender, EventArgs e)
    {
        if (AiModelCombo.ItemsSource is null || AiModelCombo.Items.Filter is null) return;

        string text = AiModelCombo.Text ?? "";
        if (text.Length == 0 || _aiModels.Any(m => string.Equals(m.Id, text, StringComparison.Ordinal)))
        {
            _aiSuppressModelFilter = true;
            try { AiModelCombo.Items.Filter = null; }
            finally { _aiSuppressModelFilter = false; }
        }
    }

    /// <summary>
    /// اختيار نموذج من القائمة يُحفَظ فوراً — انتظار خروج التركيز يجعل الاختيار يبدو مُهمَلاً
    /// إن أغلق المستخدم الإعدادات مباشرةً بعده.
    /// </summary>
    private void AiModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_aiSyncing || _aiSuppressModelFilter) return;
        if (AiModelCombo.SelectedItem is not string id) return;

        _settings.Ai.Model = id;
        SaveSettings();
    }

    // النصّ المرافق لكلّ منزلق يُكتب من دالّة مستقلّة لا من داخل المعالج وحده: تعيين قيمة تساوي
    // الحاليّة لا يُطلق ValueChanged، فكان العدّاد يبقى فارغاً كلّما طابقت الإعدادات وضع المنزلق
    // الابتدائيّ — وهو حال التنصيب الجديد بالضبط (MaxTokens = 0 = أدنى المنزلق).
    private void ShowAiTemp(double value)
        => AiTempValue.Text = value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private void ShowAiMaxTokens(int value)
        => AiMaxTokensValue.Text = value == 0
            ? Loc.T("ai.set.auto")
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void ShowAiCtxLimit(int value)
        => AiCtxLimitValue.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void AiTemp_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // أثناء InitializeComponent تُضبَط Minimum/Maximum فيُقسَر Value ويُطلَق هذا المعالج قبل
        // وجود الحقول المسمّاة وقبل تعيين الإعدادات — فالحارس على العنصر نفسه لا على علَم المزامنة.
        if (AiTempValue is null) return;

        ShowAiTemp(e.NewValue);
        if (_aiSyncing) return;

        _settings.Ai.Temperature = e.NewValue;
        SaveSettings();
    }

    private void AiMaxTokens_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AiMaxTokensValue is null) return;   // قبل اكتمال بناء الواجهة — انظر AiTemp_Changed

        int value = (int)e.NewValue;
        ShowAiMaxTokens(value);
        if (_aiSyncing) return;

        _settings.Ai.MaxTokens = value;
        SaveSettings();
    }

    private void AiCtxLimit_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AiCtxLimitValue is null) return;   // قبل اكتمال بناء الواجهة — انظر AiTemp_Changed

        int value = (int)e.NewValue;
        ShowAiCtxLimit(value);
        if (_aiSyncing) return;

        _settings.Ai.ContextCharLimit = value;
        SaveSettings();
    }

    private void AiPrompt_Changed(object sender, RoutedEventArgs e) => CommitAiPrompt();

    private void AiPanelWidth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AiPanelWidthValue is null) return;   // قبل اكتمال بناء الواجهة — انظر AiTemp_Changed

        int value = (int)e.NewValue;
        AiPanelWidthValue.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_aiSyncing) return;

        _settings.Ai.PanelWidth = value;
        SaveSettings();
        PushAiPanelMetrics();
    }

    private void AiChatFont_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AiChatFontValue is null) return;

        AiChatFontValue.Text = e.NewValue.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        if (_aiSyncing) return;

        _settings.Ai.ChatFontSize = e.NewValue;
        SaveSettings();
        PushAiPanelMetrics();
    }

    /// <summary>
    /// يمرّر قياسات اللوحة إلى كلّ التبويبات المفتوحة. منزلق لا يُرى أثره حتّى إعادة التشغيل
    /// يبدو معطّلاً.
    /// </summary>
    private void PushAiPanelMetrics()
    {
        foreach (object? item in TerminalTabs.Items)
            if (item is System.Windows.Controls.TabItem { Content: Controls.TerminalPaneContainer container })
                foreach (Controls.TerminalTabView view in container.AllViews)
                    view.ApplyAiPanelMetrics(_settings.Ai.PanelWidth, _settings.Ai.ChatFontSize);
    }

    /// <summary>
    /// يثبّت «التعليمات المخصّصة». <c>LostFocus</c> وحده لا يكفي: إغلاق الإعدادات بـEsc أو بنقرة
    /// على الحجاب قد لا ينقل التركيز خارج الحقل، فتضيع فقرةٌ كتبها المستخدم بلا أثر — لذا يُنادى
    /// من مسار الإغلاق أيضاً. المقارنة تمنع حفظاً بلا تغيير.
    /// </summary>
    private void CommitAiPrompt()
    {
        if (_aiSyncing || AiPromptBox is null) return;

        string text = AiPromptBox.Text?.Trim() ?? "";
        if (text == _settings.Ai.SystemPromptExtra) return;

        _settings.Ai.SystemPromptExtra = text;
        SaveSettings();
    }

    /// <summary>
    /// يفتح «ذاكرة التطبيق»: ما تعلّمه التطبيق معروضاً وقابلاً للحذف والتعطيل. الشفافيّة هنا ليست
    /// عبئاً بل شرط قبول الالتقاط أصلاً.
    /// </summary>
    private void AiMemoryMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshAiProfileInBackground();   // اعرض أحدث ما استُنتج، لا لقطة قديمة
            Views.AiMemoryWindow.ShowFor(
                this, AiKnowledge, _settings, SaveSettings, () => CurrentAiProfile.Text,
                () => AiConversations.Clear(), AiConversations);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            Views.AppDialog.Alert(this, Loc.T("ai.mem.title"), ex.Message);
        }
    }

    /// <summary>
    /// يفتح معالج تكامل الصدفة (OSC 133). يستنتج الصدفة من التبويب النشط إن أمكن، وإلّا يبدأ
    /// بـPowerShell (الأشيع على ويندوز).
    /// </summary>
    private void ShellIntegrationMenu_Click(object sender, RoutedEventArgs e)
        => OpenShellIntegrationForActiveTab();

    /// <summary>يفتح المعالج للصدفة النشطة — يُستدعى من القائمة ومن زرّ CTA في لوحة الدردشة.</summary>
    private void OpenShellIntegrationForActiveTab()
    {
        Services.Ai.IntegrationShell shell =
            Services.Ai.ShellIntegrationScripts.Detect(ActiveShellName())
            ?? Services.Ai.IntegrationShell.PowerShell;

        Views.ShellIntegrationWindow.ShowFor(this, shell);
    }

    /// <summary>اسم صدفة التبويب النشط إن عُرف — لاستنتاج البروفايل الصحيح في المعالج.</summary>
    private string? ActiveShellName()
    {
        if (TerminalTabs.SelectedItem is System.Windows.Controls.TabItem { Content: Controls.TerminalPaneContainer c })
            return c.ActiveView?.CurrentShellDisplayName;
        return null;
    }

    /// <summary>يفتح الإعدادات على فئة الـAI مباشرةً (من لوحة الدردشة).</summary>
    public void OpenAiSettings()
    {
        ToggleSettings(true);
        NavAi.IsChecked = true;
        SyncAiUi();
    }
}
