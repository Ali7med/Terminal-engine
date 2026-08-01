using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TerminalLauncher.Controls;

/// <summary>
/// شريط رؤوس التبويبات: صفٌّ أفقيّ يرسم <b>إطار كلّ مجموعة واسمها</b> خلف أعضائها.
///
/// <para><b>لماذا لوحة مخصّصة:</b> رؤوس التبويبات إخوةٌ في لوحة واحدة، فلا سبيل لِلَفّ بعضها في
/// حاوية. اللوحة تعرف مواضع أبنائها، فترسم خلف كلّ سلسلة متجاورة من أعضاء مجموعةٍ مستطيلاً
/// مستدير الأركان بحدّ خفيف واسم المجموعة داخله — فتُقرأ حدود المجموعة بلمحة كما في المتصفّح.</para>
///
/// <para><b>المطويّة لا تتزحزح:</b> المجموعة المطويّة تبقى إطاراً في <b>موضعها نفسه</b> ينكمش على
/// اسمه («▸ الاسم (العدد)») بعد أن تنكمش رؤوس أعضائه إلى الصفر. رسمُها حبّةً في مقدّمة الشريط
/// كان يقذف المجموعة الثانية إلى الأوّل كلّما طُويت — فيفقد المستخدم أثرها.</para>
/// </summary>
public sealed class TabStripPanel : Panel
{
    /// <summary>اسم مجموعة التبويب («» = بلا مجموعة). تُضبَط من المُضيف على كلّ رأس.</summary>
    public static readonly DependencyProperty GroupProperty = DependencyProperty.RegisterAttached(
        "Group", typeof(string), typeof(TabStripPanel),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsParentArrange
                                        | FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static string GetGroup(DependencyObject o) => (string)o.GetValue(GroupProperty);
    public static void SetGroup(DependencyObject o, string value) => o.SetValue(GroupProperty, value ?? "");

    /// <summary>لون المجموعة (#RRGGBB، فارغ = لون محايد).</summary>
    public static readonly DependencyProperty GroupColorProperty = DependencyProperty.RegisterAttached(
        "GroupColor", typeof(string), typeof(TabStripPanel),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static string GetGroupColor(DependencyObject o) => (string)o.GetValue(GroupColorProperty);
    public static void SetGroupColor(DependencyObject o, string value) => o.SetValue(GroupColorProperty, value ?? "");

    /// <summary>
    /// تقدّم طيّ الرأس (0 = ظاهر تماماً · 1 = مطويّ تماماً). يحرّكه المُضيف فينكمش عرض الرأس
    /// وتخفت عتامته تدريجيّاً — الطيّ الفوريّ كان يبتلع التبويبات دفعةً واحدة بلا أن تتبعها العين.
    /// </summary>
    public static readonly DependencyProperty CollapseProgressProperty = DependencyProperty.RegisterAttached(
        "CollapseProgress", typeof(double), typeof(TabStripPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure
                                         | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetCollapseProgress(DependencyObject o) => (double)o.GetValue(CollapseProgressProperty);
    public static void SetCollapseProgress(DependencyObject o, double value) => o.SetValue(CollapseProgressProperty, value);

    /// <summary>عرض الرأس بعد أثر الطيّ (المخفيّ فعليّاً لا يشغل شيئاً).</summary>
    private static double EffectiveWidth(UIElement child) =>
        child.Visibility != Visibility.Visible
            ? 0
            : child.DesiredSize.Width * Math.Max(0, 1 - GetCollapseProgress(child));

    /// <summary>
    /// أسماء المجموعات المطويّة. يضبطها المُضيف <b>لحظة</b> الطيّ لا بعد انتهاء الحركة: الاسم
    /// يأخذ صورته المطويّة من أوّل إطار، فينكمش الإطار انكماشاً متّصلاً بلا قفزة في نهايته.
    /// </summary>
    public IReadOnlyCollection<string> CollapsedGroups
    {
        get => _collapsedGroups;
        set
        {
            _collapsedGroups = value is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);

    // مقاسات الإطار — على شبكة الأربعة مثل بقيّة التصميم.
    private const double FramePadX = 4;    // فراغ داخل الإطار يمنة ويسرة
    private const double FramePadY = 2;
    private const double LabelGap = 8;     // بين الاسم وأوّل رأس
    private const double RunGap = 8;       // بين مجموعة وما بعدها
    private const double LabelFontSize = 10.5;
    private const double MinRowHeight = 26;

    /// <summary>حدود إطار كلّ مجموعة بعد آخر تخطيط — يقرؤها الإسقاط ليعرف أين هبط التبويب.</summary>
    private readonly Dictionary<string, Rect> _runBounds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>النصّ المرسوم لاسم كلّ مجموعة كما حُسب في التخطيط (يحمل العدد حين تكون مطويّة).</summary>
    private readonly Dictionary<string, string> _runLabels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>إطار المجموعة كما رُسم، أو null إن لم تكن معروضة.</summary>
    public Rect? GroupBounds(string group) => _runBounds.TryGetValue(group, out var r) ? r : null;

    /// <summary>اسم المجموعة التي تقع النقطة داخل <b>اسمها</b> — هدف النقر للطيّ/الفرد.</summary>
    public string? GroupLabelAt(Point p)
    {
        foreach (var (name, rect) in _runBounds)
        {
            // منطقة الاسم وحدها: النقر على رأس تبويب داخل المجموعة يجب أن يختاره لا أن يطوي المجموعة.
            double w = LabelWidth(_runLabels.TryGetValue(name, out var t) ? t : name) + LabelGap;
            var label = new Rect(rect.Left, rect.Top, Math.Min(w, rect.Width), rect.Height);
            if (label.Contains(p)) return name;
        }
        return null;
    }

    /// <summary>اسم المجموعة التي تقع النقطة داخل إطارها كاملاً — قاعدة الضمّ بالإسقاط.</summary>
    public string? GroupFrameAt(Point p)
    {
        foreach (var (name, rect) in _runBounds)
            if (rect.Contains(p)) return name;
        return null;
    }

    private readonly Dictionary<string, double> _labelWidths = new(StringComparer.Ordinal);

    private double LabelWidth(string name)
    {
        if (_labelWidths.TryGetValue(name, out double w)) return w;
        w = BuildLabel(name, Brushes.White).WidthIncludingTrailingWhitespace + 10;
        _labelWidths[name] = w;
        return w;
    }

    private FormattedText BuildLabel(string text, Brush brush) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
        LabelFontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>
    /// الأبناء كلّهم بترتيبهم. المخفيّ منهم عضوٌ في مجموعة مطويّة ويبقى محسوباً في سلسلتها —
    /// وبه وحده يبقى موضع المجموعة المطويّة حيث كان.
    /// </summary>
    private List<UIElement> AllChildren()
    {
        var list = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement child in InternalChildren) list.Add(child);
        return list;
    }

    /// <summary>نصّ اسم المجموعة: مطويّةً يحمل السهم والعدد، ومفرودةً اسمَها وحده.</summary>
    private string RunLabel(string group, int count) =>
        _collapsedGroups.Contains(group) ? $"▸ {group} ({count})" : group;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        var children = AllChildren();

        foreach (var child in children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (child.Visibility != Visibility.Visible) continue;
            width += EffectiveWidth(child);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        // فراغ الأسماء والإطارات: لكلّ سلسلة مجموعةٍ اسمُها + حشوتها.
        foreach (var run in Runs(children))
            width += LabelWidth(RunLabel(run.Group, run.End - run.Start + 1))
                   + LabelGap + FramePadX * 2 + RunGap;

        return new Size(width, Math.Max(height, MinRowHeight) + FramePadY * 2);
    }

    /// <summary>سلاسل الأبناء المتجاورين المنتمين لمجموعة واحدة.</summary>
    private static List<(string Group, int Start, int End)> Runs(List<UIElement> children)
    {
        var runs = new List<(string, int, int)>();
        int i = 0;
        while (i < children.Count)
        {
            string g = GetGroup(children[i]);
            if (g.Length == 0) { i++; continue; }

            int start = i;
            while (i < children.Count && string.Equals(GetGroup(children[i]), g, StringComparison.OrdinalIgnoreCase)) i++;
            runs.Add((g, start, i - 1));
        }
        return runs;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _runBounds.Clear();
        _runLabels.Clear();

        var children = AllChildren();
        var runStartAt = Runs(children).ToDictionary(r => r.Start, r => r);

        double x = 0;
        double top = FramePadY;
        double rowHeight = Math.Max(0, finalSize.Height - FramePadY * 2);

        for (int i = 0; i < children.Count; i++)
        {
            if (runStartAt.TryGetValue(i, out var run))
            {
                double frameLeft = x;
                string label = RunLabel(run.Group, run.End - run.Start + 1);
                x += LabelWidth(label) + LabelGap + FramePadX;

                for (int k = run.Start; k <= run.End; k++)
                {
                    var c = children[k];
                    double cw = EffectiveWidth(c);
                    c.Arrange(new Rect(x, top, cw, rowHeight));
                    x += cw;
                }

                x += FramePadX;
                _runBounds[run.Group] = new Rect(frameLeft, 0, x - frameLeft, finalSize.Height);
                _runLabels[run.Group] = label;
                x += RunGap;
                i = run.End;
                continue;
            }

            var child = children[i];
            double w = EffectiveWidth(child);
            child.Arrange(new Rect(x, top, w, rowHeight));
            x += w;
        }

        InvalidateVisual();
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        foreach (var (name, rect) in _runBounds)
        {
            var colour = ResolveColor(GroupColorOf(name));
            DrawFrame(dc, rect, colour, filled: _collapsedGroups.Contains(name));
            DrawLabel(dc, _runLabels.TryGetValue(name, out var t) ? t : name,
                      rect.Left + FramePadX + 2, rect, colour);
        }
    }

    private void DrawFrame(DrawingContext dc, Rect rect, Color colour, bool filled = false)
    {
        // خفيف عمداً: الإطار علامةُ انتماء لا عنصرٌ يزاحم التبويبات نفسها على الانتباه.
        var fill = new SolidColorBrush(Color.FromArgb((byte)(filled ? 26 : 10), colour.R, colour.G, colour.B));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(64, colour.R, colour.G, colour.B)), 1);
        fill.Freeze();
        pen.Freeze();

        // نصف بكسل: الحدّ بعرض ١ يُرسَم على مركز المسار، فبلا الإزاحة يخرج ضبابيّاً بين بكسلين.
        var r = new Rect(rect.X + 0.5, rect.Y + 0.5, Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));
        dc.DrawRoundedRectangle(fill, pen, r, 8, 8);
    }

    private void DrawLabel(DrawingContext dc, string text, double x, Rect rect, Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        var ft = BuildLabel(text, brush);
        dc.DrawText(ft, new Point(x, rect.Y + (rect.Height - ft.Height) / 2));
    }

    /// <summary>لون المجموعة مقروءاً من أوّل عضو فيها (المُضيف يضبطه على كلّ رأس).</summary>
    private string GroupColorOf(string group)
    {
        foreach (UIElement child in InternalChildren)
            if (string.Equals(GetGroup(child), group, StringComparison.OrdinalIgnoreCase))
                return GetGroupColor(child);
        return "";
    }

    /// <summary>لون محايد حين لا يختار المستخدم لوناً — فالإطار يُرى في الثيمين.</summary>
    private static readonly Color NeutralColour = Color.FromRgb(0x8A, 0x8A, 0x8A);

    private static Color ResolveColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return NeutralColour;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return NeutralColour; }
    }
}
