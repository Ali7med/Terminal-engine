using System.Windows;
using System.Windows.Controls;

namespace TerminalLauncher.Views;

/// <summary>
/// نافذة مضيفة شفّافة بلا إطار تُكدّس بطاقات الإشعارات أسفل يمين مساحة العمل (RTL). لا تنشط ولا
/// تظهر في شريط المهام، Topmost فوق كل النوافذ. يديرها <see cref="Services.NotificationService"/>.
/// </summary>
public sealed class ToastHostWindow : Window
{
    /// <summary>حاوية تكديس البطاقات (الأحدث أسفل).</summary>
    public StackPanel HostPanel { get; }

    public ToastHostWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = true;
        SizeToContent = SizeToContent.Manual;

        var area = SystemParameters.WorkArea;
        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;

        HostPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 20, 20),
        };
        Content = HostPanel;
    }
}
