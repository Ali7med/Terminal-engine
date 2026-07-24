using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private CancellationTokenSource? _aiProbeCts;

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
            if (AiProviderCombo.ItemsSource is null)
                AiProviderCombo.ItemsSource = AiProviderCatalog.All;

            AiSettings ai = _settings.Ai;
            AiProviderCombo.SelectedValue = ai.ProviderId;

            AiModelCombo.Text = AiProviderFactory.ResolveModel(ai);
            AiBaseUrlBox.Text = ai.BaseUrlOverride;
            AiLearningCheck.IsChecked = ai.LearningEnabled;
            AiAmbientCheck.IsChecked = ai.AmbientContextEnabled;
            AiPreviewCheck.IsChecked = ai.AlwaysPreview;
            AiQuietCheck.IsChecked = ai.QuietMode;

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
        AiProviderDescriptor? descriptor = AiProviderCatalog.Find(_settings.Ai.ProviderId);
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
        AiProviderDescriptor? descriptor = AiProviderCatalog.Find(_settings.Ai.ProviderId);
        if (descriptor is null) return;

        AiRefreshModelsBtn.IsEnabled = false;
        try
        {
            IAiProvider provider = AiProviderFactory.CreateFor(
                descriptor, AiKeys.Get(descriptor.Id), _settings.Ai.BaseUrlOverride);

            IReadOnlyList<string> models = await provider.ListModelsAsync(CancellationToken.None).ConfigureAwait(true);

            string current = AiModelCombo.Text;
            AiModelCombo.ItemsSource = models;
            AiModelCombo.Text = current; // الجلب لا يغيّر اختيار المستخدم
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

    /// <summary>يفتح الإعدادات على فئة الـAI مباشرةً (من لوحة الدردشة).</summary>
    public void OpenAiSettings()
    {
        ToggleSettings(true);
        NavAi.IsChecked = true;
        SyncAiUi();
    }
}
