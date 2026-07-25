using System;
using System.Windows;
using TerminalLauncher.Services;
using TerminalLauncher.Services.Ai;

namespace TerminalLauncher.Views;

/// <summary>
/// معالج حقن خطافات تكامل الصدفة (OSC 133). <b>يعرض الـdiff الحرفيّ</b> لما سيُضاف قبل أيّ
/// كتابة، ويخيّر المستخدم بين الحقن التلقائيّ (بنسخة احتياطية وتأكيد) و«انسخه بنفسك».
///
/// <para>هذا هو ما ينقص المحرّك: المحرّك يحلّل OSC 133 أصلاً، لكن الأصداف لا تطبع علاماته إلّا
/// بعد إضافة هذه الخطافات لبروفايلها.</para>
/// </summary>
public partial class ShellIntegrationWindow : Window
{
    private readonly ShellIntegrationHook _hook;

    private ShellIntegrationWindow(ShellIntegrationHook hook)
    {
        _hook = hook;
        InitializeComponent();
        FlowDirection = Loc.Flow;
        Populate();
    }

    /// <summary>يفتح المعالج لصدفة محدَّدة فوق مالكه.</summary>
    public static void ShowFor(Window? owner, IntegrationShell shell)
    {
        var window = new ShellIntegrationWindow(ShellIntegrationScripts.For(shell)) { Owner = owner };
        window.ShowDialog();
    }

    private void Populate()
    {
        Title = Loc.T("ai.osc.title");
        TitleText.Text = Loc.T("ai.osc.title");
        ExplainText.Text = Loc.T("ai.osc.explain");
        CopyBtn.Content = Loc.T("ai.osc.copy");
        CloseBtn.Content = Loc.T("ai.prev.cancel");
        PathText.Text = _hook.ProfilePath;
        DiffBox.Text = _hook.Hook;

        RefreshStatus();
    }

    private void RefreshStatus()
    {
        IntegrationStatus status = ShellIntegrationInstaller.StatusOf(_hook);

        (string message, bool canInstall) = status switch
        {
            IntegrationStatus.Installed => (Loc.T("ai.osc.installed"), false),
            IntegrationStatus.ProfileMissing => (Loc.T("ai.osc.willCreate"), true),
            _ => (Loc.T("ai.osc.notInstalled"), true),
        };

        StatusText.Text = message;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(
            status == IntegrationStatus.Installed ? "Brush.Success" : "Brush.TextMuted");

        InstallBtn.Content = Loc.T("ai.osc.install");
        InstallBtn.IsEnabled = canInstall;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_hook.Hook);
            StatusText.Text = Loc.T("ai.osc.copied");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Success");
        }
        catch (System.Runtime.InteropServices.COMException) { /* الحافظة مقفلة */ }
    }

    /// <summary>يحقن بعد تأكيد صريح — الحقن يعدّل ملفّاً خارج التطبيق، فلا يجري بضغطة واحدة.</summary>
    private void Install_Click(object sender, RoutedEventArgs e)
    {
        string? choice = AppDialog.Confirm(
            this, Loc.T("ai.osc.title"), Loc.T("ai.osc.confirm"),
            (Loc.T("ai.osc.install"), "install", DialogButtonKind.Accent),
            (Loc.T("ai.prev.cancel"), "cancel", DialogButtonKind.Neutral));

        if (choice != "install") return;

        IntegrationResult result = ShellIntegrationInstaller.Install(_hook);

        if (result.Ok)
        {
            string msg = Loc.T("ai.osc.done");
            if (result.BackupPath is not null)
                msg += "\n" + string.Format(Loc.T("ai.osc.backup"), result.BackupPath);
            msg += "\n" + Loc.T("ai.osc.restart");

            StatusText.Text = msg;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Success");
            InstallBtn.IsEnabled = false;
        }
        else
        {
            StatusText.Text = result.Message;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
