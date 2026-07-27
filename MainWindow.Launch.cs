using System;
using System.Windows;
using System.Windows.Controls;
using TerminalLauncher.Services;

namespace TerminalLauncher;

/// <summary>
/// «فتحه من ويندوز»: تسجيل كلمة قصيرة تفتح التطبيق من شريط عنوان المستكشف أو حوار «تشغيل»،
/// وفتحُ تيرمنال في المجلد الذي أُطلِق منه.
/// </summary>
public partial class MainWindow
{
    /// <summary>يملأ حقل الكلمة ويعرض حالة التسجيل — يُستدعى مع مزامنة لوحة الإعدادات.</summary>
    private void SyncLaunchUi()
    {
        if (LaunchKeywordBox is null) return;

        string keyword = _settings.LaunchKeyword.Length > 0
            ? _settings.LaunchKeyword
            : ExplorerLaunch.DefaultKeyword;

        if (LaunchKeywordBox.Text != keyword) LaunchKeywordBox.Text = keyword;
        UpdateLaunchStatus();
    }

    /// <summary>
    /// الحالة تُقرأ من سجلّ ويندوز لا من الإعدادات: المستخدم قد يزيل المفتاح بنفسه أو ينقل ملفّات
    /// التطبيق، فتصير الإعدادات تقول «مسجَّلة» وهي ليست كذلك.
    /// </summary>
    private void UpdateLaunchStatus()
    {
        if (LaunchStatusText is null) return;

        string keyword = LaunchKeywordBox.Text.Trim();
        bool on = ExplorerLaunch.IsRegistered(keyword);

        LaunchStatusText.Text = on ? string.Format(Loc.T("launch.on"), keyword) : Loc.T("launch.off");
        LaunchStatusText.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            on ? "Brush.Accent" : "Brush.TextMuted");

        LaunchUnregisterBtn.IsEnabled = on;
    }

    private void LaunchKeyword_TextChanged(object sender, TextChangedEventArgs e) => UpdateLaunchStatus();

    private void LaunchRegister_Click(object sender, RoutedEventArgs e)
    {
        string keyword = LaunchKeywordBox.Text.Trim();

        // تغيير الكلمة يترك القديمة مسجَّلة لولا هذا — فتتراكم كلمات تفتح التطبيق بلا أن يعرف أحد.
        string previous = _settings.LaunchKeyword;
        if (previous.Length > 0 && !string.Equals(previous, keyword, StringComparison.OrdinalIgnoreCase))
            ExplorerLaunch.Unregister(previous);

        string? error = ExplorerLaunch.Register(keyword);
        if (error is not null)
        {
            NotificationService.Warning(Loc.T("launch.title"), string.Format(Loc.T("launch.errFail"), error));
            UpdateLaunchStatus();
            return;
        }

        _settings.LaunchKeyword = keyword;
        SaveSettings();
        UpdateLaunchStatus();
        NotificationService.Success(Loc.T("launch.title"), string.Format(Loc.T("launch.on"), keyword));
    }

    private void LaunchUnregister_Click(object sender, RoutedEventArgs e)
    {
        ExplorerLaunch.Unregister(LaunchKeywordBox.Text.Trim());
        _settings.LaunchKeyword = "";
        SaveSettings();
        UpdateLaunchStatus();
    }

    /// <summary>
    /// يفتح تيرمنالاً في المجلد الذي أُطلِق منه التطبيق — وهو ما يجعل كتابة الكلمة في شريط عنوان
    /// المستكشف مكافئةً لكتابة <c>cmd</c>.
    ///
    /// <para>يُستدعى بعد استعادة الجلسة: التبويبات المستعادة تبقى، ويُضاف تبويب المجلد الجديد
    /// فيصير هو النشط — فالمستخدم طلب <b>هذا</b> المجلد الآن.</para>
    /// </summary>
    private void OpenLaunchFolderTerminal()
    {
        string? folder = ExplorerLaunch.LaunchFolder();
        if (folder is null) return;

        OpenTerminalForProfile(Terminal.ShellCatalog.DefaultKey, folder);
    }
}
