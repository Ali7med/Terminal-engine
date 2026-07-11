using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using TerminalLauncher.Views;

namespace TerminalLauncher.Services;

/// <summary>نوع الإشعار — يحدّد اللون والأيقونة.</summary>
public enum NotificationType { Info, Success, Warning, Error }

/// <summary>
/// مقبض إشعار تقدّم (تحميل مثلاً): يُحدَّث ثم يُنهى بنجاح/فشل. راجع <see cref="NotificationService.Progress"/>.
/// </summary>
public sealed class ProgressNotification
{
    private readonly Action<double, string> _update;
    private readonly Action<string, string, bool> _finish;
    internal ProgressNotification(Action<double, string> update, Action<string, string, bool> finish)
    { _update = update; _finish = finish; }

    /// <summary>يحدّث النسبة (0..1) والسطر الفرعيّ (السرعة/المتبقّي).</summary>
    public void Report(double fraction, string detail) => _update(fraction, detail);
    /// <summary>ينهي البطاقة كنجاح.</summary>
    public void Done(string title, string message) => _finish(title, message, true);
    /// <summary>ينهي البطاقة كخطأ.</summary>
    public void Fail(string title, string message) => _finish(title, message, false);
}

/// <summary>
/// نظام إشعارات موحّد لـ TerminalLauncher (Toast) يظهر فوق النوافذ، يتبع الثيم وبنمط RTL — مطابق
/// لأسلوب «نوتفكيشن هليوم». أنماط: <see cref="Primary"/> (بطاقة أيقونة+عنوان+نص+شريط تقدّم)،
/// <see cref="Secondary"/> (حبّة سطر واحد)، و<see cref="Progress"/> (بطاقة تقدّم حيّة).
/// </summary>
public static class NotificationService
{
    private const int MaxToasts = 5;
    private static readonly FontFamily Mdl2 = new("Segoe MDL2 Assets");
    private static ToastHostWindow? _host;
    private static bool _hostClosing;

    // ===================== الواجهة العامّة =====================

    public static void Primary(string title, string message, NotificationType type = NotificationType.Info, double seconds = 5)
        => Dispatch(() => { var (r, p, c) = BuildPrimary(title, message, type); Add(r, p, c, TimeSpan.FromSeconds(seconds)); });

    public static void Secondary(string message, NotificationType type = NotificationType.Info, double seconds = 2.5)
        => Dispatch(() => { var (r, _, c) = BuildSecondary(message, type); Add(r, null, c, TimeSpan.FromSeconds(seconds)); });

    public static void Success(string title, string message) => Primary(title, message, NotificationType.Success);
    public static void Error(string title, string message) => Primary(title, message, NotificationType.Error);
    public static void Warning(string title, string message) => Primary(title, message, NotificationType.Warning);
    public static void Info(string title, string message) => Primary(title, message, NotificationType.Info);

    /// <summary>
    /// بطاقة تقدّم حيّة (لا تُغلق تلقائياً): عنوان + سطر فرعيّ + شريط تقدّم + زرّ إلغاء/إغلاق (X).
    /// <paramref name="onCancel"/> يُستدعى عند ضغط X (لإلغاء التحميل مثلاً). يُعيد مقبضاً للتحديث/الإنهاء.
    /// </summary>
    public static ProgressNotification Progress(string title, string detail = "", Action? onCancel = null)
    {
        Border card = null!; Border bar = null!; TextBlock detailTb = null!;
        bool finished = false;

        Dispatch(() =>
        {
            UIElement close;
            (card, bar, detailTb, close) = BuildProgress(title, detail);
            close.MouseLeftButtonUp += (_, _) =>
            {
                if (finished) return;
                finished = true;
                RemovePersistent(card);
                onCancel?.Invoke();
            };
            AddPersistent(card);
        });

        void Update(double f, string d) => Dispatch(() =>
        {
            bar.Tag = f;
            if (bar.Parent is Grid track)
                bar.Width = Math.Max(0, Math.Min(1, f)) * track.ActualWidth;
            detailTb.Text = d;
        });

        void Finish(string t, string m, bool ok) => Dispatch(() =>
        {
            if (finished) return;
            finished = true;
            RemovePersistent(card);
            Primary(t, m, ok ? NotificationType.Success : NotificationType.Error);
        });

        return new ProgressNotification(Update, Finish);
    }

    // ===================== الإدارة =====================

    private static void Dispatch(Action action)
    {
        var app = Application.Current;
        if (app == null) return;
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }

    private static ToastHostWindow EnsureHost()
    {
        if (_host == null || _hostClosing)
        {
            var created = new ToastHostWindow();
            _host = created;
            _hostClosing = false;
            created.Closed += (_, _) => { if (ReferenceEquals(_host, created)) { _host = null; _hostClosing = false; } };
            created.Show();
        }
        return _host;
    }

    private static void AddPersistent(Border toast)
    {
        var panel = EnsureHost().HostPanel;
        panel.Children.Add(toast);
        while (panel.Children.Count > MaxToasts) panel.Children.RemoveAt(0);
        AnimateIn(toast);
    }

    private static void RemovePersistent(Border toast)
    {
        if (_host == null) return;
        var host = _host;
        var panel = host.HostPanel;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) =>
        {
            panel.Children.Remove(toast);
            if (panel.Children.Count == 0)
            {
                if (ReferenceEquals(_host, host)) _hostClosing = true;
                try { host.Close(); } catch { }
            }
        };
        toast.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void AnimateIn(Border toast)
    {
        toast.Opacity = 0;
        var tt = new TranslateTransform(0, 16);
        toast.RenderTransform = tt;
        toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static void Add(Border toast, Border? progress, UIElement close, TimeSpan duration)
    {
        var host = EnsureHost();
        var panel = host.HostPanel;
        panel.Children.Add(toast);
        while (panel.Children.Count > MaxToasts) panel.Children.RemoveAt(0);
        AnimateIn(toast);

        if (progress != null)
        {
            var scale = new ScaleTransform(1, 1);
            progress.RenderTransformOrigin = new Point(1, 0);
            progress.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, duration));
        }

        bool dismissed = false;
        var timer = new DispatcherTimer { Interval = duration };
        void Dismiss()
        {
            if (dismissed) return;
            dismissed = true;
            timer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (_, _) =>
            {
                panel.Children.Remove(toast);
                if (panel.Children.Count == 0)
                {
                    if (ReferenceEquals(_host, host)) _hostClosing = true;
                    try { host.Close(); } catch { }
                }
            };
            toast.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        timer.Tick += (_, _) => Dismiss();
        timer.Start();
        close.MouseLeftButtonUp += (_, _) => Dismiss();
    }

    // ===================== البناء =====================

    private static (Border root, Border? progress, UIElement close) BuildPrimary(string title, string message, NotificationType type)
    {
        var (strong, soft, glyph) = Palette(type);

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconChip = IconChip(glyph, strong, soft, 38, 19, 18);
        iconChip.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(iconChip, 0);

        var texts = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14,
            Foreground = Res("Brush.Text"), TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(message))
            texts.Children.Add(new TextBlock
            {
                Text = message, FontSize = 12.5, Foreground = Res("Brush.TextMuted"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            });
        Grid.SetColumn(texts, 1);

        var close = CloseGlyph();
        close.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(close, 2);

        grid.Children.Add(iconChip);
        grid.Children.Add(texts);
        grid.Children.Add(close);

        var progress = new Border
        {
            Height = 3, CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0), Background = strong,
        };

        var holder = new Grid();
        holder.Children.Add(grid);
        holder.Children.Add(progress);
        holder.SizeChanged += (_, ev) => holder.Clip = new RectangleGeometry(new Rect(ev.NewSize), 13, 13);

        var root = Card();
        root.Width = 360;
        root.Child = holder;
        return (root, progress, close);
    }

    private static (Border root, Border? progress, UIElement close) BuildSecondary(string message, NotificationType type)
    {
        var (strong, soft, glyph) = Palette(type);

        var grid = new Grid { Margin = new Thickness(10, 9, 13, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconChip = IconChip(glyph, strong, soft, 30, 9, 15);
        Grid.SetColumn(iconChip, 0);

        var text = new TextBlock
        {
            Text = message, FontSize = 13, FontWeight = FontWeights.Medium,
            Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Margin = new Thickness(11, 0, 0, 0),
        };
        Grid.SetColumn(text, 1);

        var close = CloseGlyph();
        close.VerticalAlignment = VerticalAlignment.Center;
        close.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(close, 2);

        grid.Children.Add(iconChip);
        grid.Children.Add(text);
        grid.Children.Add(close);

        var root = Card();
        root.MinWidth = 240;
        root.Child = grid;
        return (root, null, close);
    }

    private static (Border card, Border bar, TextBlock detail, UIElement close) BuildProgress(string title, string detail)
    {
        var (strong, soft, _) = Palette(NotificationType.Info);

        var stack = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

        // صفّ العنوان + زرّ الإلغاء/الإغلاق
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleTb = new TextBlock
        {
            Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14,
            Foreground = Res("Brush.Text"), TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(titleTb, 0);
        var close = CloseGlyph();
        close.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(close, 1);
        head.Children.Add(titleTb);
        head.Children.Add(close);

        var detailTb = new TextBlock
        {
            Text = detail, FontSize = 12, Foreground = Res("Brush.TextMuted"),
            Margin = new Thickness(0, 3, 0, 8), FlowDirection = FlowDirection.LeftToRight,
        };

        var track = new Grid { Height = 6 };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = soft });
        var bar = new Border
        {
            CornerRadius = new CornerRadius(3), Background = strong,
            HorizontalAlignment = HorizontalAlignment.Left, Width = 0,
        };
        track.Children.Add(bar);
        track.SizeChanged += (_, _) =>
        {
            if (bar.Tag is double f) bar.Width = Math.Max(0, Math.Min(1, f)) * track.ActualWidth;
        };

        stack.Children.Add(head);
        stack.Children.Add(detailTb);
        stack.Children.Add(track);

        var root = Card();
        root.Width = 360;
        root.Child = stack;
        return (root, bar, detailTb, close);
    }

    private static Border IconChip(string glyph, Brush strong, Brush soft, double size, double radius, double fontSize) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(radius), Background = soft,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = glyph, FontFamily = Mdl2, FontSize = fontSize, Foreground = strong,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static Border Card() => new()
    {
        Background = Res("Brush.Surface"),
        BorderBrush = Res("Brush.Border"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Margin = new Thickness(0, 10, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        FlowDirection = FlowDirection.RightToLeft,
        Effect = new DropShadowEffect { Color = Color.FromArgb(0x55, 0, 0, 0), BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5 },
    };

    private static TextBlock CloseGlyph() => new()
    {
        Text = "", FontFamily = Mdl2, FontSize = 12,
        Foreground = Res("Brush.TextMuted"), Cursor = Cursors.Hand,
    };

    private static (Brush strong, Brush soft, string glyph) Palette(NotificationType type)
    {
        (Color c, string glyph) = type switch
        {
            NotificationType.Success => (Color.FromRgb(0x9E, 0xCE, 0x6A), ""),   // ✔ دائرة
            NotificationType.Error => (Color.FromRgb(0xE0, 0x60, 0x3F), ""),     // ✖ دائرة
            NotificationType.Warning => (Color.FromRgb(0xE0, 0xAF, 0x68), ""),   // ⚠
            _ => (Color.FromRgb(0x6C, 0xA8, 0xF0), ""),                          // ℹ
        };
        var strong = new SolidColorBrush(c);
        var soft = new SolidColorBrush(c) { Opacity = 0.20 };
        strong.Freeze();
        return (strong, soft, glyph);
    }

    private static Brush Res(string key)
        => Application.Current?.Resources[key] as Brush ?? Brushes.Gray;
}
