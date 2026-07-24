using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TerminalLauncher.Services;

namespace TerminalLauncher.Controls;

/// <summary>
/// رقاقة غير مقاطِعة تظهر بعد أمر فاشل: «اشرح هذا الخطأ؟»، أو «رأيت هذا من قبل — الحل السابق»
/// إن كان للبصمة حلّ محفوظ.
///
/// <para><b>لا تسرق التركيز ولا تزيح التخطيط:</b> تُوضَع في طبقة عائمة فوق التيرمنال
/// (<see cref="Panel.ZIndexProperty"/>) ولا تدخل ترتيب الشبكة. رقاقة تُزيح ما تحتها أثناء
/// الكتابة أسوأ من ألّا تكون.</para>
///
/// <para>تتلاشى تلقائيّاً بعد مهلة قصيرة؛ الإخفاء اليدويّ يُحتسَب تجاهلاً — ثلاثة متتالية تقترح
/// «الوضع الهادئ».</para>
/// </summary>
public sealed class AiErrorChip : Border
{
    /// <summary>مدّة بقاء الرقاقة قبل التلاشي التلقائيّ.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(12);

    private readonly DispatcherTimer _timer;
    private readonly StackPanel _content;
    private Action? _onDismiss;

    public AiErrorChip()
    {
        Visibility = Visibility.Collapsed;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(12, 0, 12, 16);
        Padding = new Thickness(12, 8, 12, 8);
        CornerRadius = new CornerRadius(10);
        Focusable = false;
        IsHitTestVisible = true;

        _content = new StackPanel { Orientation = Orientation.Vertical };
        Child = _content;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Lifetime };
        _timer.Tick += (_, _) => Conceal();

        Loaded += (_, _) => ApplyTheme();
        Loc.Changed += ApplyThemeSafe;
        Unloaded += (_, _) => Loc.Changed -= ApplyThemeSafe;
    }

    private void ApplyThemeSafe()
    {
        if (IsLoaded) ApplyTheme();
    }

    private void ApplyTheme()
    {
        FlowDirection = Loc.Flow;
        Background = (Brush)FindResource("Brush.GlassStrong");
        BorderBrush = (Brush)FindResource("Brush.Border");
        BorderThickness = new Thickness(1);
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 14,
            ShadowDepth = 2,
            Opacity = 0.35,
            Color = Colors.Black,
        };
    }

    /// <summary>
    /// يعرض الرقاقة. حين يُمرَّر <paramref name="knownSolution"/> تعرض الحلّ المحفوظ محلّيّاً مع
    /// زرّ إدراجه — قيمة فوريّة بلا اتّصال ولا كلفة API.
    /// </summary>
    /// <param name="knownSolution">حلّ سابق مقبول لهذه البصمة، أو null.</param>
    /// <param name="onExplain">يُطلَق عند طلب شرح من المساعد.</param>
    /// <param name="onDismiss">يُطلَق عند الإخفاء اليدويّ (يُحتسَب تجاهلاً).</param>
    /// <param name="onInsert">يُدرج الحلّ المحفوظ في سطر الإدخال.</param>
    public void Show(string? knownSolution, Action onExplain, Action onDismiss, Action<string> onInsert)
    {
        _onDismiss = onDismiss;
        _content.Children.Clear();

        if (knownSolution is { Length: > 0 })
        {
            _content.Children.Add(Label(Loc.T("ai.chip.seenBefore"), muted: true));
            _content.Children.Add(new TextBlock
            {
                Text = knownSolution,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 11,
                FlowDirection = FlowDirection.LeftToRight,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 460,
                Margin = new Thickness(0, 2, 0, 6),
                Foreground = (Brush)FindResource("Brush.Text"),
            });
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        if (knownSolution is { Length: > 0 })
            row.Children.Add(Action(Loc.T("ai.ctx.insert"), () => { Conceal(); onInsert(knownSolution); }));

        row.Children.Add(Action(Loc.T("ai.chip.explain"), () => { Conceal(); onExplain(); }));
        row.Children.Add(Action(Loc.T("ai.chip.dismiss"), () => { Conceal(); _onDismiss?.Invoke(); }));

        _content.Children.Add(row);

        Visibility = Visibility.Visible;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>يخفي الرقاقة بلا احتساب تجاهل (التلاشي التلقائيّ ليس رفضاً).</summary>
    public void Conceal()
    {
        _timer.Stop();
        Visibility = Visibility.Collapsed;
    }

    private TextBlock Label(string text, bool muted) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = muted ? FontWeights.Normal : FontWeights.SemiBold,
        Foreground = (Brush)FindResource(muted ? "Brush.TextMuted" : "Brush.Text"),
    };

    private Button Action(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 11,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 6, 0),
            Style = (Style)FindResource("ChromeButton"),
        };
        button.Click += (_, _) => onClick();
        return button;
    }
}
