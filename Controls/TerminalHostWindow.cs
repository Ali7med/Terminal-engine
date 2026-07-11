using System;
using System.Windows;
using System.Windows.Controls;

namespace TerminalLauncher.Controls;

/// <summary>
/// نافذة مستقلّة تستضيف عرض تيرمنال مفصولاً عن النافذة الرئيسة (يُفتح عبر زرّ «فصل» في هدر التيرمنال).
/// تحافظ على الجلسة الحيّة نفسها (العرض يُعاد استضافته فقط، بلا إعادة تشغيل الصدفة)، وتُنهيها عند الإغلاق.
/// </summary>
public sealed class TerminalHostWindow : Window
{
    private readonly TerminalTabView _view;

    public TerminalHostWindow(TerminalTabView view, string title)
    {
        _view = view;
        Title = string.IsNullOrWhiteSpace(title) ? "تيرمنال" : title;
        Width = 900;
        Height = 560;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = Services.Loc.Flow;
        SetResourceReference(BackgroundProperty, "Brush.Bg");
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri("pack://application:,,,/Assets/AppIcon/app.ico", UriKind.Absolute));

        // العرض المفصول يملأ النافذة؛ زرّ ✕ الخاصّ به يغلق هذه النافذة.
        var host = new Grid();
        host.Children.Add(view);
        Content = host;
        view.CloseRequested += OnViewCloseRequested;
    }

    private void OnViewCloseRequested(TerminalTabView _) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _view.CloseRequested -= OnViewCloseRequested;
        _view.CloseSession(deleteHistory: true);   // إغلاق المستخدم للنافذة المستقلّة يمسح تاريخ جلستها
        base.OnClosed(e);
    }
}
