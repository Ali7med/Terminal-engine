using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TerminalLauncher.Services;

namespace TerminalLauncher.Controls;

/// <summary>
/// شريط نتيجة المساعد <b>داخل منطقة صندوق الأوامر</b>: السؤال، ثمّ الردّ، ثمّ الأمر المستخرَج
/// وحالته (نُفِّذ تلقائياً · خطر ينتظر تأكيداً · اقتراح جاهز).
///
/// <para><b>لماذا هنا لا في لوحة الدردشة:</b> من سأل من التيرمنال يتوقّع الجواب في التيرمنال.
/// فتحُ لوحة جانبية ينقل نظره وتركيزه إلى شاشة أخرى ويكسر التسلسل الذي كان فيه. اللوحة تبقى
/// موجودة ومسجَّلة فيها المحادثة كاملةً — لكنّها لا تُفتح إلّا بطلبه الصريح.</para>
///
/// <para>عنصر عاديّ في التخطيط (لا طبقة عائمة): النتيجة تستحقّ مكاناً ثابتاً يُقرأ، بخلاف رقاقة
/// الخطأ العابرة.</para>
/// </summary>
public sealed class AiInlineResult : Border
{
    /// <summary>نبضة تحريك نقاط «يفكّر…».</summary>
    private static readonly TimeSpan DotInterval = TimeSpan.FromMilliseconds(380);

    private readonly TextBlock _spark = new();
    private readonly TextBlock _question = new();
    private readonly TextBlock _answer = new();
    private readonly TextBlock _hint = new();
    private readonly TextBlock _commandText = new();
    private readonly TextBlock _statusText = new();
    private readonly Border _commandBox;
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _commandRow;

    private readonly DispatcherTimer _dots;
    private int _dotCount;

    /// <summary>يُطلَق حين يطلب المستخدم فتح المحادثة الكاملة في اللوحة الجانبيّة.</summary>
    public event Action? OpenChatRequested;

    public AiInlineResult()
    {
        Visibility = Visibility.Collapsed;
        Padding = new Thickness(16, 11, 16, 12);
        BorderThickness = new Thickness(0, 0, 0, 1);
        SetResourceReference(BackgroundProperty, "Brush.RowHover");
        SetResourceReference(BorderBrushProperty, "Brush.Hairline");

        // ===== الصفّ الأوّل: ✨ + السؤال + أفعال الشريط =====
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _spark.Text = "✨";
        _spark.VerticalAlignment = VerticalAlignment.Center;
        _spark.Margin = new Thickness(0, 0, 8, 0);
        _spark.SetResourceReference(TextBlock.FontSizeProperty, "Size.Ui");

        _question.VerticalAlignment = VerticalAlignment.Center;
        _question.TextTrimming = TextTrimming.CharacterEllipsis;
        _question.FontWeight = FontWeights.SemiBold;
        _question.SetResourceReference(TextBlock.FontSizeProperty, "Size.Ui");
        _question.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        Grid.SetColumn(_question, 1);

        var headActions = new StackPanel { Orientation = Orientation.Horizontal };
        headActions.Children.Add(Link(() => Loc.T("ai.inline.openChat"), () => OpenChatRequested?.Invoke()));
        headActions.Children.Add(Link(() => "✕", Hide));
        Grid.SetColumn(headActions, 2);

        head.Children.Add(_spark);
        head.Children.Add(_question);
        head.Children.Add(headActions);

        // ===== الصفّ الثاني: نصّ الردّ =====
        _answer.TextWrapping = TextWrapping.Wrap;
        _answer.MaxHeight = 110;                 // ملخّص لا محادثة: الباقي في اللوحة عند الطلب
        _answer.Margin = new Thickness(0, 7, 0, 0);
        _answer.SetResourceReference(TextBlock.FontSizeProperty, "Size.Ui");
        _answer.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        // سطر توجيه: يظهر حين يكون الردّ سؤالاً ينتظر جوابك في الصندوق نفسه
        _hint.Visibility = Visibility.Collapsed;
        _hint.Margin = new Thickness(0, 6, 0, 0);
        _hint.FontWeight = FontWeights.SemiBold;
        _hint.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");

        // ===== الصفّ الثالث: الأمر المستخرَج + حالته + أفعاله =====
        _commandText.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _commandText.TextTrimming = TextTrimming.CharacterEllipsis;
        _commandText.VerticalAlignment = VerticalAlignment.Center;
        _commandText.SetResourceReference(TextBlock.FontSizeProperty, "Size.Ui");
        _commandText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

        _commandBox = new Border
        {
            // الأمر يبقى LTR مهما كان اتّجاه الواجهة — سطر شِل معكوساً غير قابل للقراءة.
            FlowDirection = FlowDirection.LeftToRight,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 6),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _commandText,
        };
        _commandBox.SetResourceReference(BackgroundProperty, "Brush.KeyCap");

        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(0, 0, 10, 0);
        _statusText.TextWrapping = TextWrapping.Wrap;
        _statusText.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");

        _commandRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        _commandRow.Children.Add(_commandBox);
        _commandRow.Children.Add(_statusText);
        _commandRow.Children.Add(_actions);

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(_answer);
        body.Children.Add(_hint);
        body.Children.Add(_commandRow);
        Child = body;

        _dots = new DispatcherTimer(DispatcherPriority.Background) { Interval = DotInterval };
        _dots.Tick += (_, _) => TickDots();

        Loc.Changed += ApplyLanguage;
        Unloaded += (_, _) => { Loc.Changed -= ApplyLanguage; StopThinking(); };
    }

    // ===== حالات العرض =====

    /// <summary>
    /// يبدأ عرض طلب جديد: السؤال ظاهراً و«يفكّر» بنقاط متحرّكة ورمزٍ نابض — الانتظار أمام شريط
    /// ساكن يبدو تعليقاً لا عملاً.
    /// </summary>
    public void ShowThinking(string question)
    {
        _question.Text = question;
        _answer.Visibility = Visibility.Visible;
        _answer.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _hint.Visibility = Visibility.Collapsed;
        _commandBox.Visibility = Visibility.Visible;
        _commandRow.Visibility = Visibility.Collapsed;
        _actions.Children.Clear();
        Visibility = Visibility.Visible;

        StartThinking();
    }

    /// <summary>يعرض نصّ الردّ (بلا كتل الكود) — فارغ = يُخفي سطر الردّ.</summary>
    public void ShowAnswer(string answer)
    {
        StopThinking();
        _answer.Text = answer.Trim();
        _answer.Visibility = _answer.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// الردّ سؤال توضيحيّ: يبقى الشريط ظاهراً ويُوجَّه المستخدم للإجابة في الصندوق نفسه —
    /// المحادثة تكمل في مكانها بلا نافذة ولا لوحة.
    /// </summary>
    public void ShowAwaitingAnswer()
    {
        _hint.Text = Loc.T("ai.inline.answerHint");
        _hint.Visibility = Visibility.Visible;
        _commandRow.Visibility = Visibility.Collapsed;
        _actions.Children.Clear();
    }

    /// <summary>حالة «نُفِّذ تلقائياً»: الأمر ظاهر كي يُقرأ بعد تنفيذه، بلا أزرار.</summary>
    public void ShowRan(string command)
    {
        SetCommand(command);
        _statusText.Text = "▸ " + Loc.T("ai.inline.ran");
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Success");
        _actions.Children.Clear();
    }

    /// <summary>
    /// حالة «أمر خطر»: لا يُنفَّذ مهما كان إعداد التنفيذ التلقائيّ — يُعرض بلون التحذير مع
    /// تشغيل صريح أو تعديل.
    /// </summary>
    public void ShowRisky(string command, Action onRun, Action onEdit)
    {
        SetCommand(command);
        _statusText.Text = "⚠ " + Loc.T("ai.ctx.risky");
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        BuildActions(Loc.T("ai.inline.run"), onRun, Loc.T("ai.inline.edit"), onEdit);
    }

    /// <summary>حالة «اقتراح جاهز»: التنفيذ التلقائيّ مطفأ أو الكتلة متعدّدة الأسطر.</summary>
    public void ShowSuggestion(string command, Action onRun, Action onEdit)
    {
        SetCommand(command);
        _statusText.Text = "";
        BuildActions(Loc.T("ai.inline.run"), onRun, Loc.T("ai.inline.edit"), onEdit);
    }

    /// <summary>لا أمر في الردّ — الجواب نصّيّ فقط.</summary>
    public void ShowNoCommand()
    {
        _commandRow.Visibility = Visibility.Collapsed;
        _actions.Children.Clear();
    }

    /// <summary>
    /// موافقة قبل الإرسال — تُعرض <b>هنا لا في لوحة الدردشة</b> حين يحجب المُنقّح شيئاً فعلاً.
    /// الموافقة على ما يُرسَل شرطٌ لا يسقط، لكنّها لا تستوجب نقل المستخدم إلى شاشة أخرى.
    /// </summary>
    public void ShowConfirm(string question, string warning, Action onSend, Action onCancel)
    {
        StopThinking();
        _question.Text = question;
        _answer.Text = warning;
        _answer.Visibility = Visibility.Visible;
        _answer.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        _hint.Visibility = Visibility.Collapsed;

        _commandBox.Visibility = Visibility.Collapsed;
        _commandRow.Visibility = Visibility.Visible;
        _statusText.Text = "";
        BuildActions(Loc.T("ai.prev.send"), onSend, Loc.T("ai.prev.cancel"), onCancel);
        Visibility = Visibility.Visible;
    }

    /// <summary>يعرض فشلاً مع فعل مقترَح (فتح الإعدادات مثلاً).</summary>
    public void ShowFailure(string message, string? ctaLabel, Action? onCta)
    {
        StopThinking();
        _answer.Text = message;
        _answer.Visibility = Visibility.Visible;
        _answer.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        _hint.Visibility = Visibility.Collapsed;

        _commandBox.Visibility = Visibility.Collapsed;
        _commandRow.Visibility = Visibility.Visible;
        _statusText.Text = "";
        _actions.Children.Clear();

        if (ctaLabel is { Length: > 0 } && onCta is not null)
            _actions.Children.Add(Action(() => ctaLabel, onCta, accent: true));

        Visibility = Visibility.Visible;
    }

    /// <summary>يُخفي الشريط ويعيده إلى حالته المحايدة.</summary>
    public void Hide()
    {
        StopThinking();
        Visibility = Visibility.Collapsed;
        _actions.Children.Clear();
        _hint.Visibility = Visibility.Collapsed;
        _answer.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
    }

    // ===== حركة «يفكّر» =====

    private void StartThinking()
    {
        _dotCount = 0;
        TickDots();
        _dots.Start();

        // نبض هادئ على الرمز: إشارةُ حياة بلا ضجيج. 900ms ذهاباً وإياباً — أبطأ من أن يُلهي.
        var pulse = new DoubleAnimation(1.0, 0.35, new Duration(TimeSpan.FromMilliseconds(900)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _spark.BeginAnimation(OpacityProperty, pulse);
    }

    private void StopThinking()
    {
        _dots.Stop();
        _spark.BeginAnimation(OpacityProperty, null);   // يُزيل الحركة ويعيد القيمة المحلّيّة
        _spark.Opacity = 1.0;
    }

    private void TickDots()
    {
        _dotCount = (_dotCount + 1) % 4;
        _answer.Text = Loc.T("ai.panel.thinking").TrimEnd('…', '.', ' ') + new string('.', _dotCount);
    }

    // ===== بناء العناصر =====

    private void SetCommand(string command)
    {
        StopThinking();
        _commandText.Text = command;
        _commandBox.Visibility = Visibility.Visible;
        _commandRow.Visibility = Visibility.Visible;
        _hint.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }

    private void BuildActions(string primaryLabel, Action onPrimary, string secondaryLabel, Action onSecondary)
    {
        _actions.Children.Clear();
        _actions.Children.Add(Action(() => primaryLabel, () => { Hide(); onPrimary(); }, accent: true));
        _actions.Children.Add(Action(() => secondaryLabel, () => { Hide(); onSecondary(); }, accent: false));
    }

    /// <summary>اللغة تتبدّل حيّاً: النصوص الثابتة (لا محتوى الردّ) تُعاد كتابتها.</summary>
    private void ApplyLanguage()
    {
        FlowDirection = Loc.Flow;
        if (_hint.Visibility == Visibility.Visible) _hint.Text = Loc.T("ai.inline.answerHint");

        foreach (object child in _actions.Children) Relabel(child);
        if (Child is StackPanel body && body.Children[0] is Grid head)
            foreach (object child in head.Children)
                if (child is StackPanel panel)
                    foreach (object item in panel.Children) Relabel(item);
    }

    private static void Relabel(object child)
    {
        if (child is Button { Tag: Func<string> label } button) button.Content = label();
    }

    /// <summary>
    /// زرّ فعل. النصّ يُمرَّر كدالّة لا كقيمة كي يُعاد حسابه عند تبديل اللغة — الزرّ المبنيّ
    /// بنصّ ثابت يبقى بلغة إنشائه إلى الأبد.
    /// </summary>
    private Button Action(Func<string> label, Action onClick, bool accent)
    {
        var button = new Button
        {
            Content = label(),
            Tag = label,
            Padding = new Thickness(12, 4, 12, 5),
            Margin = new Thickness(0, 0, 6, 0),
        };
        // الثانويّ يبقى بنمط الزرّ الضمنيّ — لا ChromeButton، فذاك خطّ أيقونات يحوّل النصّ مربّعات.
        if (accent) button.SetResourceReference(StyleProperty, "AccentButton");
        button.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        button.Click += (_, _) => onClick();
        return button;
    }

    private Button Link(Func<string> label, Action onClick)
    {
        var button = new Button
        {
            Content = label(),
            Tag = label,
            Padding = new Thickness(8, 2, 8, 3),
            Margin = new Thickness(6, 0, 0, 0),
            Focusable = false,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.SetResourceReference(StyleProperty, "TextLinkButton");
        button.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        button.Click += (_, _) => onClick();
        return button;
    }
}
