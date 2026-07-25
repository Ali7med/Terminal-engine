using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TerminalLauncher.Services;
using TerminalLauncher.Theme;
// خاصّية ThemeMode في Window (WPF 10) تحجب نوع الثيمات — نُسمّيه كما في MainWindow.
using AppThemeMode = TerminalLauncher.Theme.ThemeMode;

namespace TerminalLauncher.Views;

/// <summary>
/// شاشة البدء: نافذة تيرمنال مصغّرة تحمل هويّة الأداة — شريط بثلاث نقاط وعلامة واسم، وجلسة
/// تُطبَع فيها مراحل الإقلاع سطراً سطراً: أمر يُكتب حرفاً حرفاً، ثمّ سطر لكلّ مرحلة يحمل مؤشّر
/// دوران متحرّكاً حتّى تنتهي فيصير علامة ✓ وينتقل الدور للسطر التالي.
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

    /// <summary>الأمر «المكتوب» في أوّل سطر — تقنيّ لا يُترجَم (محاكاة سطر أوامر).</summary>
    private const string BootCommand = "terminal-engine --boot";

    /// <summary>إطارات مؤشّر الدوران: أرباع كتلة تدور. حروف Block Elements متوفّرة في Consolas.</summary>
    private static readonly string[] SpinnerFrames = { "▘", "▝", "▗", "▖" };

    private const string DoneGlyph = "✓";

    private readonly DispatcherTimer _backstopTimer;
    private readonly DispatcherTimer _typeTimer;     // كتابة الأمر حرفاً حرفاً
    private readonly DispatcherTimer _spinTimer;     // دوران مؤشّر السطر الجاري
    private readonly Queue<string> _pending = new(); // مراحل وصلت قبل أن ينتهي طبع الأمر

    private Brush _accent = Brushes.Gray;
    private Brush _text = Brushes.White;
    private Brush _muted = Brushes.Gray;
    private Brush _success = Brushes.YellowGreen;

    private TextBlock? _promptCursor;                // مؤشّر سطر الأمر (يختفي بعد كتابته)
    private TextBlock? _promptText;
    private TextBlock? _activeGlyph;                 // مؤشّر دوران السطر الجاري
    private TextBlock? _activeText;
    private TextBlock? _activeCursor;

    private int _typed;
    private int _frame;
    private bool _closing;

    public SplashWindow(BootProfile.Hints hints)
    {
        InitializeComponent();

        ApplyPalette(ThemeManager.Resolve(hints.ThemeId));

        TitleText.Text = Loc.T("app.title");
        VersionText.Text = "v" + AppVersion.Current;

        StartPromptLine();

        _typeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(26), DispatcherPriority.Render,
            (_, _) => TypeTick(), Dispatcher);
        _typeTimer.Start();

        _spinTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(110), DispatcherPriority.Render,
            (_, _) => SpinTick(), Dispatcher);

        _backstopTimer = new DispatcherTimer(Backstop, DispatcherPriority.Normal,
            (_, _) => SplashScreenHost.Close(), Dispatcher);
        _backstopTimer.Start();

        // أوّل مرحلة تُطبَع بعد أن يكتمل الأمر (تدخل الطابور الآن).
        _pending.Enqueue(Loc.T("splash.starting"));
    }

    // ===== الألوان =====

    /// <summary>يُلبس النافذة ألوان ثيم المستخدم: سطح التيرمنال + شريط العنوان + اللكنة والكتابة.</summary>
    private void ApplyPalette(ThemeManager.ThemePreset p)
    {
        bool light = p.Mode == AppThemeMode.Light;

        _accent  = Freeze(new SolidColorBrush(p.Accent));
        _text    = Freeze(new SolidColorBrush(p.Text));
        _muted   = Freeze(new SolidColorBrush(p.TextMuted));
        _success = Freeze(new SolidColorBrush(light ? Color.FromRgb(0x2E, 0x7D, 0x57)
                                                    : Color.FromRgb(0x9E, 0xCE, 0x6A)));

        Root.Background  = Freeze(new SolidColorBrush(p.TerminalBg));
        Root.BorderBrush = Freeze(new SolidColorBrush(Alpha(p.Text, 0x24)));
        RootShadow.Color = light ? Color.FromRgb(0x6A, 0x66, 0x60) : Colors.Black;

        TitleBar.Background = Freeze(new SolidColorBrush(p.Surface));
        TitleRule.Background = Freeze(new SolidColorBrush(Alpha(p.Text, 0x18)));
        TitleText.Foreground = Freeze(new SolidColorBrush(Alpha(p.TextMuted, 0xE0)));

        // نقاط نافذة التيرمنال الثلاث بألوانها المتعارَفة (تُقرأ فوراً كـ«نافذة طرفية»).
        Dot1.Fill = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57)));
        Dot2.Fill = Freeze(new SolidColorBrush(Color.FromRgb(0xFE, 0xBC, 0x2E)));
        Dot3.Fill = Freeze(new SolidColorBrush(Color.FromRgb(0x28, 0xC8, 0x40)));

        VersionText.Foreground = Freeze(new SolidColorBrush(Alpha(p.TextMuted, 0xA0)));
    }

    // ===== الأسطر =====

    /// <summary>سطر الأمر: محثّ بلون اللكنة، ثمّ نصّ يُكتب حرفاً حرفاً، ثمّ مؤشّر وامض.</summary>
    private void StartPromptLine()
    {
        var glyph = MakeCell("$", _accent, bold: true);
        _promptText = MakeCell("", _text);
        _promptCursor = MakeCursor();

        Lines.Children.Add(MakeLine(glyph, _promptText, _promptCursor));
    }

    private void TypeTick()
    {
        if (_promptText == null) { _typeTimer.Stop(); return; }

        if (_typed >= BootCommand.Length)
        {
            FinishTyping();
            return;
        }

        _typed++;
        _promptText.Text = BootCommand.Substring(0, _typed);
    }

    /// <summary>ينهي كتابة الأمر فوراً (طبيعيّاً أو حين تصل مرحلة قبل انتهائه) ثمّ يُفرغ الطابور.</summary>
    private void FinishTyping()
    {
        _typeTimer.Stop();
        _typed = BootCommand.Length;
        if (_promptText != null) _promptText.Text = BootCommand;
        StopCursor(_promptCursor);
        _promptCursor = null;

        while (_pending.Count > 0) AppendStage(_pending.Dequeue());
    }

    /// <summary>يختم السطر الجاري بعلامة ✓ ثمّ يفتح سطراً جديداً بمؤشّر دوران ومؤشّر وامض.</summary>
    private void AppendStage(string text)
    {
        CompleteActiveLine();

        _activeGlyph = MakeCell(SpinnerFrames[_frame], _accent);
        _activeText = MakeCell(text, _text);
        _activeCursor = MakeCursor();

        Lines.Children.Add(MakeLine(_activeGlyph, _activeText, _activeCursor));

        // أطول مما تتّسع له البطاقة ⇒ يزحف أقدم سطر خارجها (كتيرمنال حقيقيّ).
        if (Lines.Children.Count > 6) Lines.Children.RemoveAt(0);

        if (!_spinTimer.IsEnabled) _spinTimer.Start();
    }

    private void CompleteActiveLine()
    {
        if (_activeGlyph != null) { _activeGlyph.Text = DoneGlyph; _activeGlyph.Foreground = _success; }
        if (_activeText != null) _activeText.Foreground = _muted;
        StopCursor(_activeCursor);

        _activeGlyph = null;
        _activeText = null;
        _activeCursor = null;
    }

    private void SpinTick()
    {
        if (_activeGlyph == null) { _spinTimer.Stop(); return; }
        _frame = (_frame + 1) % SpinnerFrames.Length;
        _activeGlyph.Text = SpinnerFrames[_frame];
    }

    /// <summary>يبني سطراً أفقيّاً: خانة الرمز بعرض ثابت كي تتحاذى بداياتُ النصوص.</summary>
    private static UIElement MakeLine(TextBlock glyph, TextBlock text, TextBlock cursor)
    {
        glyph.Width = 16;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
        panel.Children.Add(glyph);
        panel.Children.Add(text);
        panel.Children.Add(cursor);
        return panel;
    }

    private static TextBlock MakeCell(string text, Brush brush, bool bold = false) => new()
    {
        Text = text,
        Foreground = brush,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>مؤشّر كتلة وامض (نبضة ثنائيّة حادّة كمؤشّر التيرمنال، لا تلاشياً ناعماً).</summary>
    private TextBlock MakeCursor()
    {
        var cursor = new TextBlock
        {
            Text = "█",
            Foreground = _accent,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var blink = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(1060)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromPercent(0.0)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromPercent(0.5)));
        cursor.BeginAnimation(OpacityProperty, blink);

        return cursor;
    }

    private static void StopCursor(TextBlock? cursor)
    {
        if (cursor == null) return;
        cursor.BeginAnimation(OpacityProperty, null);
        cursor.Visibility = Visibility.Collapsed;
    }

    // ===== واجهة المضيف =====

    /// <summary>يضيف سطر المرحلة الجارية (يُستدعى على خيط هذه النافذة).</summary>
    public void SetStatus(string text)
    {
        if (_closing) return;

        if (_typeTimer.IsEnabled) { _pending.Enqueue(text); FinishTyping(); }
        else AppendStage(text);
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
        _typeTimer.Stop();
        _spinTimer.Stop();
        CompleteActiveLine();   // آخر مرحلة تُختَم بعلامة ✓ قبل الاختفاء

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

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
