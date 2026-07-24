using System;
using System.Windows;
using System.Windows.Controls;
using TerminalLauncher.Services;
using TerminalLauncher.Services.Ai;
using TerminalLauncher.Theme;

namespace TerminalLauncher.Controls;

/// <summary>
/// ربط لوحة مساعد الـAI بالتبويب. اللوحة <b>مملوكة للتبويب</b>: تُهيَّأ عند أوّل فتح، وتُغلَق
/// ويُلغى بثّها عند إغلاق التبويب.
/// </summary>
public partial class TerminalTabView
{
    private AppSettings? _aiAppSettings;
    private AiKeyStore? _aiKeyStore;
    private Action? _aiSaveSettings;
    private Action? _aiOpenSettings;
    private bool _aiPanelReady;

    /// <summary>
    /// يمرّر ما تحتاجه اللوحة من النافذة الرئيسة. يُستدعى عند إنشاء التبويب؛ لا يبني اللوحة بعد
    /// (البناء كسول عند أوّل فتح كي لا يدفع كلّ تبويب ثمن ميزة قد لا يستعملها).
    /// </summary>
    /// <param name="settings">الإعدادات الحيّة.</param>
    /// <param name="saveSettings">حفظ الإعدادات.</param>
    /// <param name="openAiSettings">يفتح شاشة إعدادات الـAI.</param>
    public void AttachAi(AppSettings settings, Action saveSettings, Action openAiSettings)
    {
        _aiAppSettings = settings;
        _aiSaveSettings = saveSettings;
        _aiOpenSettings = openAiSettings;
        _aiKeyStore = new AiKeyStore(() => settings.Ai, saveSettings);
    }

    private void AiToggleButton_Click(object sender, RoutedEventArgs e)
    {
        bool show = AiToggleButton.IsChecked == true;
        if (show) EnsureAiPanel();

        AiSidePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) AiSidePanel.Focus();
    }

    /// <summary>يهيّئ اللوحة عند أوّل فتح فقط.</summary>
    private void EnsureAiPanel()
    {
        if (_aiPanelReady || _aiAppSettings is null || _aiKeyStore is null) return;

        AiSidePanel.Configure(_aiAppSettings.Ai, _aiKeyStore, _aiSaveSettings ?? (() => { }));
        AiSidePanel.SettingsRequested += () => _aiOpenSettings?.Invoke();
        _aiPanelReady = true;
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
