using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TerminalLauncher.Services;
using TerminalLauncher.Theme;
// خاصّية ThemeMode في Window (WPF 10) تحجب نوع الثيمات — نُسمّيه كما في MainWindow.
using AppThemeMode = TerminalLauncher.Theme.ThemeMode;

namespace TerminalLauncher.Views;

/// <summary>
/// شاشة البدء: بطاقة صغيرة تحمل هويّة الأداة (العلامة + اسمها + الإصدار) وحلقة تحميل دوّارة،
/// تظهر فور انطلاق العمليّة وتبقى حتّى تُرسَم النافذة الرئيسة أوّل مرّة.
///
/// <para><b>مكتفية بذاتها عمداً:</b> تعمل على خيط مستقلّ (راجع <see cref="SplashScreenHost"/>)،
/// فلا تقرأ موارد التطبيق ولا تلمس كائناً يملكه الخيط الرئيس. ألوانها تُشتقّ هنا من ثيم المستخدم
/// عبر <see cref="ThemeManager.Resolve"/> (سجلّ ألوان صرف بلا فراشٍ مشتركة)، ولغتها من
/// <see cref="BootProfile"/>.</para>
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>
    /// صمّام أمان: إن تعذّر على النافذة الرئيسة أن تُرسَم (خطأ أثناء البناء مثلاً) تُغلق الشاشة
    /// نفسها بدل أن تبقى معلَّقة فوق كلّ شيء (Topmost) وتحجب حوار الخطأ.
    /// </summary>
    private static readonly TimeSpan Backstop = TimeSpan.FromSeconds(20);

    private readonly DispatcherTimer _backstopTimer;
    private bool _closing;

    public SplashWindow(BootProfile.Hints hints)
    {
        InitializeComponent();

        FlowDirection = hints.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ApplyPalette(ThemeManager.Resolve(hints.ThemeId));

        TitleText.Text = Loc.T("app.title");
        VersionText.Text = "v" + AppVersion.Current;
        StatusText.Text = Loc.T("splash.starting");

        _backstopTimer = new DispatcherTimer(Backstop, DispatcherPriority.Normal,
            (_, _) => SplashScreenHost.Close(), Dispatcher);
        _backstopTimer.Start();
    }

    /// <summary>يُلبس البطاقة ألوان ثيم المستخدم: سطح + كتابة + لكنة (الحلقة والشريط والهالة).</summary>
    private void ApplyPalette(ThemeManager.ThemePreset p)
    {
        Root.Background  = Brush(p.Surface);
        Root.BorderBrush = Brush(Alpha(p.Text, 0x22));
        RootShadow.Color = p.Mode == AppThemeMode.Light ? Color.FromRgb(0x6A, 0x66, 0x60) : Colors.Black;

        // هالة اللكنة: مركزها فوق العلامة وتتلاشى إلى الشفافيّة — عمق بلا صورة.
        Glow.Fill = new RadialGradientBrush(Alpha(p.Accent, p.Mode == AppThemeMode.Light ? (byte)0x28 : (byte)0x38),
                                            Colors.Transparent)
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };

        RingTrack.Stroke = Brush(Alpha(p.Text, 0x1A));
        RingArc.Stroke   = Brush(p.Accent);

        BarTrack.Background = Brush(Alpha(p.Text, 0x16));
        BarFill.Background  = Brush(p.Accent);

        TitleText.Foreground   = Brush(p.Text);
        VersionText.Foreground = Brush(Alpha(p.TextMuted, 0xC8));
        StatusText.Foreground  = Brush(p.TextMuted);
    }

    /// <summary>يحدّث سطر المرحلة الجارية (يُستدعى على خيط هذه النافذة).</summary>
    public void SetStatus(string text)
    {
        if (!_closing) StatusText.Text = text;
    }

    /// <summary>
    /// تلاشٍ قصير ثمّ إغلاق، ثمّ <paramref name="onClosed"/> (يُنهي به المضيف موزّع هذا الخيط).
    /// يُستدعى مرّة واحدة؛ أيّ نداء لاحق يُتجاهَل.
    /// </summary>
    public void FadeOutAndClose(Action? onClosed)
    {
        if (_closing) return;
        _closing = true;
        _backstopTimer.Stop();

        try
        {
            var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            fade.Completed += (_, _) => Finish(onClosed);
            BeginAnimation(OpacityProperty, fade);
        }
        catch
        {
            Finish(onClosed);   // بلا حركة خير من نافذة عالقة
        }
    }

    private void Finish(Action? onClosed)
    {
        try { Close(); } catch { /* أُغلقت سلفاً */ }
        onClosed?.Invoke();
    }

    private static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
