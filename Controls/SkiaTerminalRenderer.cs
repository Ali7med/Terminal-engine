using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using TerminalLauncher.Terminal;

namespace TerminalLauncher.Controls;

/// <summary>نمط رسم المؤشّر.</summary>
public enum CursorStyle
{
    /// <summary>مربّع مملوء يغطّي الخليّة (بألوان معكوسة).</summary>
    Block,
    /// <summary>شريط رأسيّ رفيع عند حافة الخليّة اليسرى.</summary>
    Bar,
    /// <summary>خطّ سفليّ تحت الخليّة.</summary>
    Underline,
}

/// <summary>
/// عارض شبكة خلايا أُحاديّة المسافة (monospace) عبر SkiaSharp (النواة فقط) يرسم
/// <see cref="ScreenSnapshot"/> على <see cref="WriteableBitmap"/> نُنشئ فوقه <c>SKSurface</c> مباشرةً.
/// لا يعتمد على SkiaSharp.Views.WPF (الذي يجرّ OpenTK لإطار .NET Framework) — نرسم البِتماب بأنفسنا.
/// يُشتقّ من <see cref="FrameworkElement"/> ويرسم البِتماب في <c>OnRender</c> — فيملأ خليّته دائماً
/// (لا يتقلّص كـ<c>Image</c> بلا Source) ويستقبل تركيز لوحة المفاتيح. الرسم كلّه على خيط الواجهة.
/// </summary>
public sealed class SkiaTerminalRenderer : FrameworkElement, IRenderer
{
    // ===== مقاييس الخطّ والخلايا =====
    private SKTypeface? _typeface;
    private SKTypeface? _boldTypeface;
    private SKFont? _font;         // الخطّ العاديّ
    private SKFont? _fontBold;     // الخطّ العريض
    private SKFont? _fontItalic;   // مائل (skew على العاديّ)
    private SKFont? _fontBoldItalic;
    private float _cellWidth = 8f;
    private float _cellHeight = 16f;
    private float _ascent;         // مقدار الصعود (سالب في Skia) — للأساس (baseline)

    // كاش خطوط بديلة لنقاط الترميز التي لا يملكها الخطّ الأساس (عربي/إيموجي/CJK).
    private readonly System.Collections.Generic.Dictionary<int, SKFont?> _fallbackCache = new();

    // كاش مُشكِّلات HarfBuzz حسب الـtypeface (للنصوص المعقّدة: العربية).
    private readonly System.Collections.Generic.Dictionary<SKTypeface, SKShaper> _shapers = new();

    // حاشية داخليّة (DIP) كي لا تلتصق الأحرف بإطار اللوحة.
    private const float PaddingDip = 6f;

    // ===== البِتماب =====
    private WriteableBitmap? _bitmap;
    private int _pixelWidth;
    private int _pixelHeight;

    // ===== الحالة =====
    private ScreenSnapshot? _snapshot;
    private FontFamily _fontFamily = new("Cascadia Mono");
    private double _fontSize = 14;

    // ===== التحديد (Selection) — إحداثيّات في فضاء فهارس snapshot.Lines =====
    private (int Line, int Col)? _selStart;
    private (int Line, int Col)? _selEnd;

    // ===== مطابقات البحث (Search) — إحداثيّات في فضاء فهارس snapshot.Lines =====
    private IReadOnlyList<(int Line, int StartCol, int Length)> _searchMatches =
        Array.Empty<(int, int, int)>();
    private int _searchCurrent = -1;

    // ألوان التزيين (شفّافة تُرسَم خلف الغليف).
    private static readonly SKColor SelectionColor = new(90, 120, 200, 110);
    private static readonly SKColor SearchColor = new(220, 200, 60, 120);
    private static readonly SKColor SearchCurrentColor = new(255, 150, 40, 160);
    private static readonly SKColor HyperlinkColor = new(90, 150, 245, 255);
    private static readonly SKColor BlockSuccessColor = new(120, 205, 100, 255);
    private static readonly SKColor BlockFailedColor = new(240, 90, 100, 255);

    public event Action? RenderNeeded;

    public SkiaTerminalRenderer()
    {
        // الجودة: البكسل الحقيقيّ بلا تنعيم WPF (Skia يتكفّل بالتنعيم).
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        // العرض يبدأ من أعلى-يسار الشبكة مهما كان اتّجاه الواجهة (LTR للتيرمنال).
        FlowDirection = FlowDirection.LeftToRight;
        // يستقبل تركيز لوحة المفاتيح (الكتابة/الاختصارات) بلا مستطيل تركيز.
        Focusable = true;
        FocusVisualStyle = null;

        BuildFonts();
        SizeChanged += OnSizeChanged;
        Loaded += (_, _) => { Focus(); Rebuild(); };
    }

    /// <summary>نطالب بكامل المساحة المتاحة (لا نتقلّص إلى صفر) كي يُنشأ البِتماب ويُرسَم.</summary>
    protected override Size MeasureOverride(Size constraint)
    {
        double w = double.IsInfinity(constraint.Width) ? 0 : constraint.Width;
        double h = double.IsInfinity(constraint.Height) ? 0 : constraint.Height;
        return new Size(w, h);
    }

    /// <summary>يرسم البِتماب (أو خلفية التيرمنال قبل إنشائه) ليملأ العنصر ويكون قابلاً للنقر.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (_bitmap != null)
            dc.DrawImage(_bitmap, rect);
        else
            dc.DrawRectangle(BackgroundBrush, null, rect);   // خلفية للـhit-test قبل جاهزيّة البِتماب
    }

    // فرشاة خلفية التيرمنال (مُجمّدة) — للرسم قبل البِتماب ولضمان قابليّة النقر.
    private static readonly Brush BackgroundBrush = CreateBackgroundBrush();
    private static Brush CreateBackgroundBrush()
    {
        var b = new SolidColorBrush(AnsiPalette.BackgroundColor);
        b.Freeze();
        return b;
    }

    // ===== الخصائص القابلة للضبط =====

    /// <summary>عائلة الخطّ (أُحاديّة المسافة يُفضَّل)؛ افتراضها "Cascadia Mono".</summary>
    public FontFamily TerminalFontFamily
    {
        get => _fontFamily;
        set { _fontFamily = value ?? new FontFamily("Cascadia Mono"); BuildFonts(); Rebuild(); }
    }

    /// <summary>حجم الخطّ بالنقاط (device-independent)؛ افتراضه 14.</summary>
    public double TerminalFontSize
    {
        get => _fontSize;
        set { _fontSize = value <= 0 ? 14 : value; BuildFonts(); Rebuild(); }
    }

    /// <summary>نمط رسم المؤشّر (افتراضه <see cref="CursorStyle.Block"/>).</summary>
    public CursorStyle CursorStyle { get; set; } = CursorStyle.Block;

    // شفافيّة خلفيّة التيرمنال الافتراضيّة (1 = معتِم كالمعتاد؛ أقلّ = تظهر صورة خلفيّة النافذة).
    private double _backgroundAlpha = 1.0;

    /// <summary>
    /// شفافيّة الخلفيّة الافتراضيّة للتيرمنال (0.30–1.00). عند وجود صورة خلفيّة للنافذة تُضبَط دون 1
    /// فيصبح ملء الخلفيّة الافتراضيّة (والمسح) شبه شفّاف — تظهر الصورة خلفه بينما يبقى النصّ معتِماً.
    /// خلايا الخلفيّة الصريحة (ألوان SGR) تبقى معتِمة. تغييرها يعيد الرسم فوراً.
    /// </summary>
    public double BackgroundAlpha
    {
        get => _backgroundAlpha;
        set
        {
            double v = value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
            if (Math.Abs(_backgroundAlpha - v) < 0.0001) return;
            _backgroundAlpha = v;
            if (_bitmap != null) Render();   // إعادة رسم كاملة (تغيّر المسح والخلفيّة الافتراضيّة)
        }
    }

    /// <summary>لون خلفيّة التيرمنال الافتراضيّ مطبَّقاً عليه <see cref="BackgroundAlpha"/> (يُضرَب في قناة ألفا).</summary>
    private SKColor DefaultBackgroundSk()
    {
        var c = ToSk(AnsiPalette.BackgroundColor);
        if (_backgroundAlpha >= 0.9999) return c;
        return c.WithAlpha((byte)Math.Round(c.Alpha * _backgroundAlpha));
    }

    // نصّ الإكمال التلقائيّ الشبحيّ (ghost) المرسوم بعد المؤشّر بلون باهت.
    private string? _ghostText;

    /// <summary>
    /// نصّ الإكمال الشبحيّ (ghost text) المرسوم بلون باهت بعد المؤشّر على الشاشة الأساس فقط.
    /// يضبطه <see cref="TerminalTabView"/> من سجلّ الأوامر؛ تغييره يعيد الرسم فوراً.
    /// </summary>
    public string? GhostText
    {
        get => _ghostText;
        set { if (_ghostText == value) return; _ghostText = value; Invalidate(); }
    }

    // حالة طور الوميض: true = المؤشّر ظاهر في هذا الطور. يقودها مؤقّت الوميض في الكنترول المضيف.
    private bool _cursorBlinkOn = true;

    /// <summary>
    /// طور وميض المؤشّر: <c>true</c> يرسم المؤشّر، <c>false</c> يخفيه (يبقى الحرف تحته ظاهراً).
    /// يقلّبه مؤقّت الوميض في <c>TerminalTabView</c>؛ تغييره يعيد الرسم فوراً.
    /// </summary>
    public bool CursorBlinkOn
    {
        get => _cursorBlinkOn;
        set { if (_cursorBlinkOn == value) return; _cursorBlinkOn = value; Invalidate(); }
    }

    /// <summary>
    /// إزاحة التمرير بالسطور من القاع: 0 = عرض ذيل السطور (الأحدث). قيمة موجبة تُظهر سطوراً أعلى.
    /// تُقصّ إلى مدى صالح عند الرسم.
    /// </summary>
    public int ScrollOffset { get; set; }

    // ===== هندسة نافذة العرض (تُحدَّث داخل DrawGrid عند آخر رسم) =====

    /// <summary>عدد الأعمدة الظاهرة في نافذة العرض الحاليّة.</summary>
    public int VisibleCols { get; private set; }

    /// <summary>عدد الصفوف الظاهرة في نافذة العرض الحاليّة.</summary>
    public int VisibleRows { get; private set; }

    /// <summary>فهرس أوّل سطر ظاهر داخل <c>snapshot.Lines</c>.</summary>
    public int TopLineIndex { get; private set; }

    /// <summary>إجماليّ سطور اللقطة الحاليّة (<c>snapshot.Lines.Count</c>).</summary>
    public int TotalLines { get; private set; }

    /// <summary>أقصى إزاحة تمرير ممكنة بالسطور.</summary>
    public int MaxScrollOffset => Math.Max(0, TotalLines - VisibleRows);

    /// <summary>عرض الخليّة بوحدات مستقلّة عن DPI (DIP).</summary>
    public double CellWidthDip => _cellWidth;

    /// <summary>ارتفاع الخليّة بوحدات مستقلّة عن DPI (DIP).</summary>
    public double CellHeightDip => _cellHeight;

    // ===== IRenderer =====

    public void SetSnapshot(ScreenSnapshot snapshot)
    {
        _snapshot = snapshot;
        Render();
    }

    /// <summary>عدد الخلايا التي تتّسع في مساحة بالبكسل حسب مقاييس الخطّ الحاليّة.</summary>
    /// <summary>
    /// عدد الخلايا التي تتّسع في مساحة (بوحدات DIP) حسب مقاييس الخطّ الحاليّة.
    ///
    /// يُقاس بنفس مسار <see cref="DrawGrid"/> تماماً — الخطّ المُقاس إلى دقّة الشاشة — لا بالمقاييس
    /// غير المقيسة. فـ<c>MeasureText(size·s) ≠ s·MeasureText(size)</c> (تلميح/تقريب دون-بكسليّ)،
    /// وكان الاختلاف يجعل عدد الأعمدة المُبلَّغ للتطبيق يزيد عمّا يُرسَم فعليّاً بعمود عند تكبير
    /// شاشة ويندوز (١٢٥٪/١٥٠٪) — فيظنّ التطبيق أنّه يملك عموداً لا يُرسم أبداً ⇒ بقايا عند الحافّة.
    /// </summary>
    public (int Cols, int Rows) Measure(double pixelWidth, double pixelHeight)
    {
        double scale = 1.0;
        try { scale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { /* غير مُركَّب بعد */ }
        if (scale <= 0) scale = 1.0;

        // نفس حساب DrawGrid: (العرض − حاشيتان) ثمّ التحويل للبكسل، مقسوماً على خليّة الخطّ المُقاس.
        double innerW = Math.Max(0, pixelWidth - 2 * PaddingDip) * scale;
        double innerH = Math.Max(0, pixelHeight - 2 * PaddingDip) * scale;

        double cellW = _cellWidth * scale, cellH = _cellHeight * scale;
        try
        {
            var (fontN, _, _, _) = ScaledFonts((float)scale);
            float mw = fontN.MeasureText("M");
            float mh = fontN.Spacing;
            if (mw > 0) cellW = mw;
            if (mh > 0) cellH = mh;
        }
        catch { /* نُبقي التقدير المُقاس أعلاه */ }

        int cols = cellW > 0 ? (int)Math.Floor(innerW / cellW) : 0;
        int rows = cellH > 0 ? (int)Math.Floor(innerH / cellH) : 0;
        return (Math.Max(1, cols), Math.Max(1, rows));
    }

    // ===== اختبار الإصابة (Hit-testing) =====

    /// <summary>
    /// يحوّل نقطة بإحداثيّات هذا العنصر المستقلّة عن DPI (كما من <c>MouseEventArgs.GetPosition(renderer)</c>)
    /// إلى (سطر، عمود) في فضاء فهارس <c>snapshot.Lines</c>. يعيد <c>null</c> إن كانت خارج العنصر أو بلا لقطة.
    /// </summary>
    public (int Line, int Col)? CellFromPoint(System.Windows.Point p)
    {
        if (_snapshot == null) return null;
        if (_cellWidth <= 0 || _cellHeight <= 0) return null;
        if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight) return null;

        int visRow = VisRowFromY(p.Y);
        int col = (int)Math.Floor((p.X - PaddingDip) / _cellWidth);
        if (visRow < 0) visRow = 0;
        if (col < 0) col = 0;

        int line = TopLineIndex + visRow;
        col = Math.Clamp(col, 0, VisibleCols);
        return (line, col);
    }

    /// <summary>
    /// هل النقطة داخل «مِزراب» الكتل (الشريط الملوّن على الحافّة اليسرى)؟ النقر هناك يؤشّر الكتلة
    /// كاملةً بدل بدء تحديد حرّ — لذلك يحتاجه المُضيف قبل معالجة الماوس المعتادة.
    /// </summary>
    public bool IsInBlockGutter(System.Windows.Point p) => p.X >= 0 && p.X <= PaddingDip;

    // ===== تخطيط الصفوف مع فجوات الكتل =====

    /// <summary>
    /// ارتفاع الفجوة قبل كلّ كتلة كنسبة من ارتفاع الخليّة (٠ = بلا فجوات). الفجوات تُزيح التاريخ
    /// لأعلى بلا تغيير عدد الصفوف، فلا تُحدِث إعادة قياس للـ PTY مع التمرير.
    /// </summary>
    public float BlockGapCells { get; set; } = 0.75f;

    private float[] _rowYPx = Array.Empty<float>();    // y كلّ صفّ ظاهر بالبكسل (للرسم)
    private float[] _rowYDip = Array.Empty<float>();   // ونفسها مستقلّة عن DPI (للاختبار بالمؤشّر)

    /// <summary>y الصفّ الظاهر رقم <paramref name="vis"/> بالبكسل (بعد حساب الفجوات).</summary>
    private float RowY(int vis)
        => vis >= 0 && vis < _rowYPx.Length ? _rowYPx[vis] : float.NegativeInfinity;

    /// <summary>
    /// يبني مواضع الصفوف من القاع لأعلى، مضيفاً فجوةً قبل أوّل سطر من كلّ كتلة (ما عدا الكتلة
    /// الأولى الظاهرة والشاشة البديلة). النتيجة: مسافة مرتّبة تفصل نتائج الأوامر بعضها عن بعض.
    /// </summary>
    private void BuildRowLayout(ScreenSnapshot snap, int top, int rows, int total, float pad, float cellH, float dpiScale)
    {
        if (_rowYPx.Length != rows) { _rowYPx = new float[rows]; _rowYDip = new float[rows]; }

        // مجموعة أسطر بداية الكتل ضمن النافذة (فهارس داخل lines).
        var starts = new HashSet<int>();
        if (!snap.AltScreen && snap.Blocks is { Count: > 0 })
            foreach (var b in snap.Blocks)
            {
                int startIndex = (int)(b.StartLine - snap.BaseLine);
                if (startIndex > top && startIndex < top + rows) starts.Add(startIndex);
            }

        float gap = snap.AltScreen ? 0f : cellH * BlockGapCells;
        float y = pad + rows * cellH;      // حافّة القاع الافتراضيّة
        for (int vis = rows - 1; vis >= 0; vis--)
        {
            y -= cellH;
            _rowYPx[vis] = y;
            _rowYDip[vis] = y / (dpiScale <= 0 ? 1f : dpiScale);
            // الفجوة تسبق الصفّ الذي تبدأ عنده الكتلة ⇒ ندفع ما فوقه لأعلى.
            if (starts.Contains(top + vis)) y -= gap;
        }
    }

    /// <summary>يعكس التخطيط: من إحداثيّ y (مستقلّ عن DPI) إلى رقم الصفّ الظاهر الأقرب.</summary>
    private int VisRowFromY(double y)
    {
        if (_rowYDip.Length == 0) return (int)Math.Floor((y - PaddingDip) / _cellHeight);
        for (int vis = 0; vis < _rowYDip.Length; vis++)
            if (y < _rowYDip[vis] + _cellHeight) return vis;
        return _rowYDip.Length - 1;
    }

    // ===== التحديد (Selection) =====

    /// <summary>هل يوجد تحديد نشط؟</summary>
    public bool HasSelection => _selStart.HasValue && _selEnd.HasValue;

    /// <summary>
    /// يضبط التحديد (يُطبّع بحيث start ≤ end) ثمّ يعيد الرسم.
    /// الإحداثيّات في فضاء فهارس <c>snapshot.Lines</c>. تمرير null للطرفين يمسح التحديد.
    /// </summary>
    public void SetSelection((int Line, int Col)? start, (int Line, int Col)? end)
    {
        if (start.HasValue && end.HasValue)
        {
            var a = start.Value;
            var b = end.Value;
            if (b.Line < a.Line || (b.Line == a.Line && b.Col < a.Col))
                (a, b) = (b, a);
            _selStart = a;
            _selEnd = b;
        }
        else
        {
            _selStart = null;
            _selEnd = null;
        }
        Invalidate();
    }

    /// <summary>يمسح التحديد ويعيد الرسم.</summary>
    public void ClearSelection()
    {
        _selStart = null;
        _selEnd = null;
        Invalidate();
    }

    /// <summary>
    /// يستخرج نصّ التحديد من اللقطة المخزّنة: لكل سطر محدَّد يأخذ نصّه العاديّ (سَلسَلة نصوص المقاطع)
    /// ويقتطعه حسب مدى الأعمدة المحدَّدة (بالعمود المعروض، مع مراعاة الأحرف العريضة)، ثمّ يصل السطور بـ "\n"
    /// مع قصّ الفراغات الذيليّة لكل سطر. يعيد "" إن لا تحديد/لا لقطة.
    /// </summary>
    public string GetSelectedText()
    {
        var snap = _snapshot;
        if (snap == null || !_selStart.HasValue || !_selEnd.HasValue) return "";

        var start = _selStart.Value;
        var end = _selEnd.Value;
        var lines = snap.Lines;
        int total = lines.Count;

        var sb = new StringBuilder();
        for (int line = start.Line; line <= end.Line; line++)
        {
            if (line < 0 || line >= total)
            {
                if (line != end.Line) sb.Append('\n');
                continue;
            }

            string plain = LinePlainText(lines[line]);
            var map = BuildColumnMap(lines[line]);   // عمود معروض → فهرس سلسلة

            int startCol = line == start.Line ? start.Col : 0;
            int endCol = line == end.Line ? end.Col : map.DisplayWidth;

            startCol = Math.Clamp(startCol, 0, map.DisplayWidth);
            endCol = Math.Clamp(endCol, 0, map.DisplayWidth);
            if (endCol < startCol) endCol = startCol;

            int si = map.ToStringIndex(startCol);
            int ei = map.ToStringIndex(endCol);
            si = Math.Clamp(si, 0, plain.Length);
            ei = Math.Clamp(ei, 0, plain.Length);
            string piece = ei > si ? plain.Substring(si, ei - si) : "";

            sb.Append(piece.TrimEnd(' '));
            if (line != end.Line) sb.Append('\n');
        }
        return sb.ToString();
    }

    // ===== مطابقات البحث (Search) =====

    /// <summary>يضبط مطابقات البحث (بإحداثيّات فهارس snapshot.Lines) ثمّ يعيد الرسم.</summary>
    public void SetSearchMatches(
        System.Collections.Generic.IReadOnlyList<(int Line, int StartCol, int Length)> matches,
        int currentIndex)
    {
        _searchMatches = matches ?? Array.Empty<(int, int, int)>();
        _searchCurrent = currentIndex;
        Invalidate();
    }

    /// <summary>يمسح مطابقات البحث ويعيد الرسم.</summary>
    public void ClearSearchMatches()
    {
        _searchMatches = Array.Empty<(int, int, int)>();
        _searchCurrent = -1;
        Invalidate();
    }

    // ===== بناء الخطوط ومقاييس الخليّة =====

    private void BuildFonts()
    {
        // نحاول أسرة الخطّ المطلوبة (قد تكون قائمة مفصولة بفواصل) ثمّ بدائل أُحاديّة المسافة.
        string family = _fontFamily?.Source ?? "Cascadia Mono";
        _typeface = ResolveTypeface(family, SKFontStyle.Normal);
        _boldTypeface = ResolveTypeface(family, SKFontStyle.Bold);

        float size = (float)_fontSize;
        _font = new SKFont(_typeface, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _fontBold = new SKFont(_boldTypeface, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _fontItalic = new SKFont(_typeface, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true, SkewX = -0.25f };
        _fontBoldItalic = new SKFont(_boldTypeface, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true, SkewX = -0.25f };

        // عرض الخليّة = عرض حرف نموذجيّ (خطّ أُحاديّ فكلّها متساوية)؛ الارتفاع = تباعد الأسطر.
        _cellWidth = _font.MeasureText("M");
        if (_cellWidth <= 0) _cellWidth = size * 0.6f;
        var metrics = _font.Metrics;
        _cellHeight = _font.Spacing;                  // ascent→descent + leading
        if (_cellHeight <= 0) _cellHeight = size * 1.3f;
        _ascent = -metrics.Ascent;                    // Ascent سالب في Skia

        // أبطِل كاش الخطوط المُقاسة كي يُعاد بناؤها من الخطّ الجديد — وإلّا لا يتغيّر الخطّ عند DPI≠100%.
        _scaledFor = -1f;
        _sN?.Dispose(); _sB?.Dispose(); _sI?.Dispose(); _sBI?.Dispose();
        _sN = _sB = _sI = _sBI = null;
        _fallbackCache.Clear();
        foreach (var sh in _shapers.Values) sh.Dispose();
        _shapers.Clear();
    }

    /// <summary>
    /// يحلّ عائلة خطّ (قد تكون "A, B, C") إلى typeface فعليّ: يجرّب كل اسم ويقبله فقط إن حُلّ فعلاً
    /// (اسم العائلة الناتج يطابق المطلوب)؛ وإلّا يرتدّ إلى Consolas أُحاديّ المسافة ثمّ الافتراضيّ.
    /// يتفادى فخّ <c>FromFamilyName("Cascadia Mono, Consolas")</c> الذي يعيد خطّاً افتراضياً
    /// متناسِب العرض (غير null) فتظهر فجوات بين الأحرف.
    /// </summary>
    private static SKTypeface ResolveTypeface(string families, SKFontStyle style)
    {
        foreach (var raw in families.Split(','))
        {
            string name = raw.Trim();
            if (name.Length == 0) continue;
            var tf = SKTypeface.FromFamilyName(name, style);
            if (tf != null && string.Equals(tf.FamilyName, name, StringComparison.OrdinalIgnoreCase))
                return tf;   // حُلّ فعلاً (لا افتراضيّ)
        }
        return SKTypeface.FromFamilyName("Consolas", style) ?? SKTypeface.CreateDefault();
    }

    // ===== دورة الحياة/الحجم =====

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Rebuild();

    /// <summary>يعيد بناء البِتماب على الحجم الحاليّ ثمّ يرسم؛ يُطلِق <see cref="RenderNeeded"/>.</summary>
    private void Rebuild()
    {
        EnsureBitmap();
        RenderNeeded?.Invoke();
        Render();
    }

    /// <summary>يعيد الرسم إن أمكن (محروس ضدّ غياب البِتماب/اللقطة).</summary>
    private void Invalidate()
    {
        if (_bitmap == null || _snapshot == null) return;
        Render();
    }

    /// <summary>يضمن وجود <see cref="WriteableBitmap"/> بأبعاد المِقبض الحاليّة (بالبكسل الفعليّ حسب DPI).</summary>
    private void EnsureBitmap()
    {
        double dw = ActualWidth, dh = ActualHeight;
        if (dw < 1 || dh < 1) return;

        // نحسب دقّة البكسل من DPI الشاشة كي يكون النصّ حادّاً.
        double dpiScale = 1.0;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            dpiScale = source.CompositionTarget.TransformToDevice.M11;

        int pw = Math.Max(1, (int)Math.Ceiling(dw * dpiScale));
        int ph = Math.Max(1, (int)Math.Ceiling(dh * dpiScale));
        double dpi = 96.0 * dpiScale;

        if (_bitmap != null && pw == _pixelWidth && ph == _pixelHeight) return;

        _pixelWidth = pw;
        _pixelHeight = ph;
        _bitmap = new WriteableBitmap(pw, ph, dpi, dpi, PixelFormats.Pbgra32, null);
    }

    // ===== الرسم =====

    /// <summary>يرسم اللقطة الحاليّة على البِتماب عبر SKSurface فوق الـ BackBuffer.</summary>
    private void Render()
    {
        // الرسم على خيط الواجهة حصراً (نتعامل مع WriteableBitmap).
        if (!CheckAccess()) { Dispatcher.Invoke(Render); return; }

        EnsureBitmap();
        if (_bitmap == null || _font == null) return;

        int w = _pixelWidth, h = _pixelHeight;
        _bitmap.Lock();
        try
        {
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, _bitmap.BackBuffer, _bitmap.BackBufferStride);
            if (surface != null)
                DrawGrid(surface.Canvas, w, h);
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
        }
        finally
        {
            _bitmap.Unlock();
        }

        InvalidateVisual();   // يطلب إعادة استدعاء OnRender لعرض البِتماب المحدَّث
    }

    private void DrawGrid(SKCanvas canvas, int pixelW, int pixelH)
    {
        // نرسم بوحدات البكسل الفعليّة: نُقيس مقاييس الخليّة إلى دقّة البِتماب.
        // ActualWidth بوحدات مستقلّة عن DPI، وpixelW بالبكسل الفعليّ ⇒ نُطبّق نفس المقياس على الخليّة.
        double dpiScale = ActualWidth > 0 ? pixelW / ActualWidth : 1.0;

        // اختيار الخطوط المُقاسة إلى الدقّة الفعليّة.
        var (fontN, fontB, fontI, fontBI) = ScaledFonts((float)dpiScale);

        // نقيس الخليّة من نفس الخطّ المرسوم — فيتطابق عرض الخليّة مع تقدّم الحرف تماماً
        // (التباعد الافتراضيّ للخطّ، بلا فجوة ناتجة عن قياس منفصل ثمّ ضربه في DPI).
        float cellW = fontN.MeasureText("M");
        if (cellW <= 0) cellW = _cellWidth * (float)dpiScale;
        float cellH = fontN.Spacing;
        if (cellH <= 0) cellH = _cellHeight * (float)dpiScale;
        float ascent = -fontN.Metrics.Ascent;

        // خلفية السطح (مطبَّقاً عليها BackgroundAlpha: تصبح شبه شفّافة حين توجد صورة خلفيّة للنافذة).
        canvas.Clear(DefaultBackgroundSk());

        var snap = _snapshot;
        if (snap == null)
        {
            VisibleCols = 0; VisibleRows = 0; TopLineIndex = 0; TotalLines = 0;
            return;
        }

        // حاشية داخليّة (padding) كي لا تلتصق الأحرف بالإطار.
        float pad = PaddingDip * (float)dpiScale;
        float innerW = Math.Max(0, pixelW - 2 * pad);
        float innerH = Math.Max(0, pixelH - 2 * pad);

        int cols = cellW > 0 ? Math.Max(1, (int)(innerW / cellW)) : 1;
        int rows = cellH > 0 ? Math.Max(1, (int)(innerH / cellH)) : 1;

        var lines = snap.Lines;
        int total = lines.Count;

        // نافذة العرض: آخر rows سطور، مع مراعاة ScrollOffset (من القاع لأعلى).
        int maxOffset = Math.Max(0, total - rows);
        int offset = Math.Clamp(ScrollOffset, 0, maxOffset);
        int top = Math.Max(0, total - rows - offset);   // فهرس أوّل سطر ظاهر ضمن lines

        // نشر هندسة نافذة العرض للمُضيف (اختيار/بحث/تمرير).
        VisibleCols = cols;
        VisibleRows = rows;
        TopLineIndex = top;
        TotalLines = total;

        // تخطيط الصفوف: y لكلّ صفّ ظاهر — يُحسَب من القاع لأعلى كي تبقى آخر سطور الشاشة (حيث
        // يكتب المستخدم) ملاصقةً للحافّة السفلى مهما زادت الفجوات، فالفجوة تدفع التاريخ لأعلى
        // لا الأمر النشط لأسفل. عدد الصفوف لا يتغيّر بالفجوات ⇒ لا يتأثّر مقاس الـ PTY.
        BuildRowLayout(snap, top, rows, total, pad, cellH, (float)dpiScale);

        using var paint = new SKPaint { IsAntialias = true };

        // بطاقات الكتل (نمط Warp): شريط خلفية full-width أفتح قليلاً خلف سطر الأمر (رأس) كلّ كتلة —
        // فيبدو كلّ أمر بطاقةً منفصلة عن مخرجاته. يُرسَم قبل النصّ كي يجلس النصّ فوقه.
        DrawBlockBands(canvas, paint, snap, top, rows, total, pad, cellH, pixelW);

        for (int vis = 0; vis < rows; vis++)
        {
            int lineIndex = top + vis;
            if (lineIndex >= total) break;
            float y = RowY(vis);
            if (y + cellH < 0) continue;   // انزاح خارج الأعلى بفعل الفجوات
            DrawLine(canvas, paint, lines[lineIndex], lineIndex, y, pad, cellW, cellH, ascent, fontN, fontB, fontI, fontBI);
        }

        // تزيين الكتل (Blocks): شريط رأسيّ ملوّن بحالة الكتلة على يسار صفوفها الظاهرة.
        DrawBlocks(canvas, paint, snap, top, rows, total, pad, cellH);

        // المؤشّر: نحوّل فهرس سطره المطلق داخل lines إلى صفّ ظاهر (يُخفى في طور الوميض المُطفَأ).
        if (snap.CursorVisible && _cursorBlinkOn)
        {
            int cursorVisRow = snap.CursorLine - top;
            if (cursorVisRow >= 0 && cursorVisRow < rows)
                DrawCursor(canvas, snap.CursorColumn, cursorVisRow, pad, cellW, cellH, ascent, fontN);
        }

        // نصّ الإكمال الشبحيّ (ghost): بعد المؤشّر بلون المقدّمة الافتراضيّ ~40% (باهت)، على
        // الشاشة الأساس فقط وحين يكون المؤشّر ظاهراً. يُقصّ إلى عدد الأعمدة الظاهرة (لا يتجاوز الحافة).
        if (!snap.AltScreen && snap.CursorVisible && !string.IsNullOrEmpty(_ghostText))
        {
            int ghostVisRow = snap.CursorLine - top;
            if (ghostVisRow >= 0 && ghostVisRow < rows)
                DrawGhost(canvas, _ghostText!, snap.CursorColumn, ghostVisRow, cols, pad, cellW, cellH, ascent, fontN);
        }
    }

    /// <summary>
    /// يرسم نصّ الإكمال الشبحيّ ابتداءً من خليّة المؤشّر بلون المقدّمة الافتراضيّ بشفافيّة ~40%،
    /// حرفاً حرفاً محترماً عرض الخلايا، مقصوصاً عند آخر عمود ظاهر (بلا خلفيّة).
    /// </summary>
    private void DrawGhost(SKCanvas canvas, string ghost, int startCol, int visRow, int cols,
        float pad, float cellW, float cellH, float ascent, SKFont fontN)
    {
        var color = ToSk(AnsiPalette.DefaultForeground);
        color = color.WithAlpha((byte)(color.Alpha * 0.4f));   // باهت ~40%
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };

        float y = RowY(visRow);   // يحترم فجوات الكتل
        int col = startCol;
        for (int i = 0; i < ghost.Length;)
        {
            int rune, adv;
            if (char.IsHighSurrogate(ghost[i]) && i + 1 < ghost.Length && char.IsLowSurrogate(ghost[i + 1]))
            { rune = char.ConvertToUtf32(ghost[i], ghost[i + 1]); adv = 2; }
            else { rune = ghost[i]; adv = 1; }
            i += adv;

            int width = RuneWidth(rune);
            if (col + width > cols) break;   // القصّ عند الحافة اليمنى (لا نتجاوز الأعمدة الظاهرة)
            if (rune != ' ' && rune != 0)
            {
                float x = pad + col * cellW;
                string glyph = char.ConvertFromUtf32(rune);
                canvas.DrawText(glyph, x, y + ascent, SKTextAlign.Left, GlyphFont(fontN, rune), paint);
            }
            col += width;
        }
    }

    /// <summary>يرسم أشرطة تزيين الكتل الرأسيّة (نمط Warp) على يسار صفوفها الظاهرة (تُتخطّى في الشاشة البديلة).</summary>
    private void DrawBlocks(SKCanvas canvas, SKPaint paint, ScreenSnapshot snap,
        int top, int rows, int total, float pad, float cellH)
    {
        if (snap.AltScreen) return;
        var blocks = snap.Blocks;
        if (blocks == null || blocks.Count == 0) return;

        float barW = Math.Max(1f, cellH * 0.18f);   // عرض الشريط ~3px عند مقاييس نموذجيّة

        foreach (var block in blocks)
        {
            int startIndex = (int)(block.StartLine - snap.BaseLine);
            // نهاية الكتلة (حصريّة)؛ المفتوحة (long.MaxValue) تمتدّ لآخر السطور.
            long endAbs = block.EndLine == long.MaxValue ? snap.BaseLine + total : block.EndLine;
            int endIndex = (int)(endAbs - snap.BaseLine);

            // تقاطع مدى الكتلة مع نافذة العرض [top, top+rows).
            int visStart = Math.Max(startIndex, top);
            int visEnd = Math.Min(endIndex, top + rows);
            if (visEnd <= visStart) continue;

            // كتل «قيد التشغيل» كانت تُظهر خطّاً أزرق دائماً على الحافة اليسرى (مُزعج) — نتخطّاها،
            // ونُبقي مؤشّرَي النجاح (أخضر)/الفشل (أحمر) القصيرَين اللذين يظهران بعد اكتمال الأمر فقط.
            SKColor color;
            switch (block.State)
            {
                case BlockState.Success: color = BlockSuccessColor; break;
                case BlockState.Failed: color = BlockFailedColor; break;
                default: continue;
            }

            float y0 = RowY(visStart - top);
            float y1 = RowY(visEnd - 1 - top) + cellH;
            if (float.IsNegativeInfinity(y0) || float.IsNegativeInfinity(y1)) continue;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = color;
            canvas.DrawRect(pad * 0.4f, y0, barW, y1 - y0, paint);
        }
    }

    /// <summary>
    /// بطاقات الكتل بنمط Warp: خلف سطر الأمر (رأس الكتلة = من StartLine إلى OutputStartLine) لكلّ
    /// كتلة، نرسم شريطاً مستطيلاً مستدير الحواف بطبقة رقيقة من لون المقدّمة — فيبدو كلّ أمر بطاقةً
    /// أفتح من مخرجاته. الكتلة المفتوحة أبرز قليلاً. تُتخطّى في الشاشة البديلة (لا كتل هناك).
    /// </summary>
    private void DrawBlockBands(SKCanvas canvas, SKPaint paint, ScreenSnapshot snap,
        int top, int rows, int total, float pad, float cellH, int pixelW)
    {
        if (snap.AltScreen || snap.Blocks is not { Count: > 0 }) return;

        var fg = ToSk(AnsiPalette.DefaultForeground);
        float inset = cellH * 0.28f;            // هامش رأسيّ للبطاقة حول سطر الأمر
        float radius = cellH * 0.32f;           // استدارة حواف البطاقة
        float sideMargin = pad * 0.5f;          // هامش جانبيّ بسيط عن حافّتي النافذة
        paint.Style = SKPaintStyle.Fill;

        foreach (var b in snap.Blocks)
        {
            // رأس الكتلة = سطر الأمر (وربّما أسطر متتابعة قبل بدء المخرجات).
            int startIndex = (int)(b.StartLine - snap.BaseLine);
            long headAbs = b.OutputStartLine > b.StartLine ? b.OutputStartLine : b.StartLine + 1;
            int headEnd = (int)(headAbs - snap.BaseLine);

            int visStart = Math.Max(startIndex, top);
            int visEnd = Math.Min(headEnd, top + rows);
            if (visEnd <= visStart) continue;

            float y0 = RowY(visStart - top);
            float y1 = RowY(visEnd - 1 - top) + cellH;
            if (float.IsNegativeInfinity(y0) || float.IsNegativeInfinity(y1)) continue;

            bool open = b.EndLine == long.MaxValue;
            paint.Color = fg.WithAlpha(open ? (byte)16 : (byte)9);
            canvas.DrawRoundRect(sideMargin, y0 - inset, pixelW - 2 * sideMargin,
                (y1 - y0) + 2 * inset, radius, radius, paint);
        }
    }

    /// <summary>يرسم سطراً واحداً: يمشي المقاطع متتبّعاً العمود (يحترم الأحرف العريضة = خليّتان).</summary>
    private void DrawLine(SKCanvas canvas, SKPaint paint, FrozenSpan[] spans, int lineIndex, float y,
        float pad, float cellW, float cellH, float ascent, SKFont fontN, SKFont fontB, SKFont fontI, SKFont fontBI)
    {
        int col = 0;
        foreach (var span in spans)
        {
            var style = span.Style;
            bool bold = (style.Attr & CharAttr.Bold) != 0;
            bool dim = (style.Attr & CharAttr.Dim) != 0;
            bool italic = (style.Attr & CharAttr.Italic) != 0;
            bool underline = (style.Attr & CharAttr.Underline) != 0;
            bool strike = (style.Attr & CharAttr.Strikethrough) != 0;
            bool inverse = (style.Attr & CharAttr.Inverse) != 0;
            bool link = span.Hyperlink != null;

            // حلّ ألوان المقدّمة/الخلفية.
            Color fgWpf = AnsiPalette.ResolveForeground(style.Fg, bold);
            bool hasBg = AnsiPalette.TryResolveBackground(style.Bg, out Color bgWpf);
            if (!hasBg) bgWpf = AnsiPalette.BackgroundColor;

            // Inverse: تبديل المقدّمة والخلفية.
            if (inverse)
            {
                (fgWpf, bgWpf) = (bgWpf, fgWpf);
                hasBg = true;   // بعد العكس صار للخلفية لون صريح
            }

            // Dim: مزج المقدّمة نحو الخلفية (~0.5).
            if (dim) fgWpf = AnsiPalette.Blend(fgWpf, bgWpf, 0.5);

            SKColor fg = ToSk(fgWpf);
            SKColor bg = ToSk(bgWpf);

            // اختيار الخطّ حسب bold/italic.
            SKFont font = bold ? (italic ? fontBI : fontB) : (italic ? fontI : fontN);

            // نمشي رونات المقطع (نقاط ترميز) لدعم أزواج البديل والعرض المزدوج.
            string text = span.Text;
            bool arabic = ContainsArabic(text);   // العربية تُرسَم مُشكَّلة (اتّصال+RTL) بعد الحلقة
            int spanStartCol = col;
            for (int i = 0; i < text.Length;)
            {
                int rune;
                int adv;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    rune = char.ConvertToUtf32(text[i], text[i + 1]);
                    adv = 2;
                }
                else
                {
                    rune = text[i];
                    adv = 1;
                }
                i += adv;

                int width = RuneWidth(rune);   // 1 أو 2 خلايا
                float x = pad + col * cellW;
                float rectW = width * cellW;

                // خلفية الخليّة: نرسمها فقط إن كانت غير افتراضيّة (لتظهر خلفية التيرمنال تحت الافتراضيّ).
                if (hasBg)
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = bg;
                    canvas.DrawRect(x, y, rectW, cellH, paint);
                }

                // تزيين البحث ثمّ التحديد خلف الغليف (التحديد فوق البحث).
                SKColor? searchBg = SearchBackground(lineIndex, col, width);
                if (searchBg.HasValue)
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = searchBg.Value;
                    canvas.DrawRect(x, y, rectW, cellH, paint);
                }
                if (IsSelected(lineIndex, col))
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = SelectionColor;
                    canvas.DrawRect(x, y, rectW, cellH, paint);
                }

                // الغليف نفسه (نتخطّى الفراغ + العربيّة التي تُرسَم مُشكَّلة لاحقاً). خطّ بديل إن لم يملك
                // الأساس الحرف (إيموجي/CJK) كي يظهر بدل المربّع الفارغ.
                if (!arabic && rune != ' ' && rune != 0)
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = fg;
                    string glyph = char.ConvertFromUtf32(rune);
                    canvas.DrawText(glyph, x, y + ascent, SKTextAlign.Left, GlyphFont(font, rune), paint);
                }

                // تسطير / خطّ وسط. الروابط التشعّبيّة تُسطَّر بلون الرابط دائماً.
                if (underline || link)
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = Math.Max(1f, cellH * 0.06f);
                    paint.Color = link ? HyperlinkColor : fg;
                    float uy = y + cellH - paint.StrokeWidth;
                    canvas.DrawLine(x, uy, x + rectW, uy, paint);
                }
                if (strike)
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = Math.Max(1f, cellH * 0.06f);
                    paint.Color = fg;
                    float sy = y + cellH * 0.5f;
                    canvas.DrawLine(x, sy, x + rectW, sy, paint);
                }

                col += width;
            }

            // العربيّة: ارسم المقطع مُشكَّلاً مرّة واحدة (اتّصال الحروف + RTL) بخطّ يدعمها.
            if (arabic)
            {
                var afont = GlyphFont(font, FirstArabicRune(text));
                var atf = afont.Typeface;
                if (atf != null)
                {
                    // نُحاذي النصّ لنهاية مدى المقطع (RTL) داخل خلاياه.
                    float runW = afont.MeasureText(text);
                    float spanRight = pad + col * cellW;
                    float sx = spanRight - runW;
                    if (sx < pad + spanStartCol * cellW) sx = pad + spanStartCol * cellW;
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = ToSk(AnsiPalette.ResolveForeground(span.Style.Fg,
                        (span.Style.Attr & CharAttr.Bold) != 0));
                    canvas.DrawShapedText(ShaperFor(atf), text, sx, y + ascent, afont, paint);
                }
            }
        }
    }

    /// <summary>هل الخليّة (lineIndex, col) ضمن مدى التحديد؟ (إحداثيّات فهارس snapshot.Lines).</summary>
    private bool IsSelected(int lineIndex, int col)
    {
        if (!_selStart.HasValue || !_selEnd.HasValue) return false;
        var s = _selStart.Value;
        var e = _selEnd.Value;
        if (lineIndex < s.Line || lineIndex > e.Line) return false;

        // سطر واحد: بين العمودين. أوّل سطر: من startCol لآخر السطر. آخر سطر: حتى endCol. الوسط: كامل.
        if (s.Line == e.Line) return col >= s.Col && col < e.Col;
        if (lineIndex == s.Line) return col >= s.Col;
        if (lineIndex == e.Line) return col < e.Col;
        return true;
    }

    /// <summary>لون خلفية البحث للخليّة (lineIndex, col) إن أصابتها مطابقة (الحاليّة أقوى)؛ وإلّا null.</summary>
    private SKColor? SearchBackground(int lineIndex, int col, int width)
    {
        var matches = _searchMatches;
        if (matches.Count == 0) return null;
        bool hit = false;
        for (int m = 0; m < matches.Count; m++)
        {
            var (line, startCol, length) = matches[m];
            if (line != lineIndex) continue;
            if (col >= startCol && col < startCol + length)
            {
                if (m == _searchCurrent) return SearchCurrentColor;   // الحاليّة أقوى دائماً
                hit = true;
            }
        }
        return hit ? SearchColor : (SKColor?)null;
    }

    /// <summary>يرسم المؤشّر حسب النمط المختار في الخليّة (العمود، الصفّ الظاهر).</summary>
    private void DrawCursor(SKCanvas canvas, int cursorCol, int visRow, float pad,
        float cellW, float cellH, float ascent, SKFont fontN)
    {
        float x = pad + cursorCol * cellW;
        float y = RowY(visRow);   // يحترم فجوات الكتل
        using var paint = new SKPaint { IsAntialias = true };
        SKColor cursorColor = ToSk(AnsiPalette.DefaultForeground);

        switch (CursorStyle)
        {
            case CursorStyle.Block:
                // مربّع مملوء + إعادة رسم الحرف (إن وُجد) بلون الخلفية (تباين معكوس).
                paint.Style = SKPaintStyle.Fill;
                paint.Color = cursorColor;
                canvas.DrawRect(x, y, cellW, cellH, paint);
                // ملاحظة: الحرف تحت المؤشّر مرسوم مسبقاً؛ نكتفي بالمربّع كتباين معكوس بسيط.
                break;
            case CursorStyle.Bar:
                paint.Style = SKPaintStyle.Fill;
                paint.Color = cursorColor;
                canvas.DrawRect(x, y, Math.Max(1f, cellW * 0.12f), cellH, paint);
                break;
            case CursorStyle.Underline:
                paint.Style = SKPaintStyle.Fill;
                paint.Color = cursorColor;
                canvas.DrawRect(x, y + cellH - Math.Max(1f, cellH * 0.1f), cellW, Math.Max(1f, cellH * 0.1f), paint);
                break;
        }
    }

    // ===== أدوات نصّ السطر (للتحديد ونسخه) =====

    /// <summary>نصّ السطر العاديّ (سَلسَلة نصوص المقاطع بلا أنماط).</summary>
    private static string LinePlainText(FrozenSpan[] spans)
    {
        if (spans.Length == 0) return "";
        var sb = new StringBuilder();
        foreach (var span in spans) sb.Append(span.Text);
        return sb.ToString();
    }

    /// <summary>خريطة عمود-معروض → فهرس-سلسلة لسطر واحد (تحترم الأحرف العريضة = عمودان لحرف واحد).</summary>
    private readonly struct ColumnMap
    {
        // stringIndexForColumn[displayCol] = فهرس أوّل حرف لتلك الخليّة في السلسلة العاديّة.
        private readonly int[] _stringIndexForColumn;
        public int DisplayWidth { get; }
        public int StringLength { get; }

        public ColumnMap(int[] stringIndexForColumn, int displayWidth, int stringLength)
        {
            _stringIndexForColumn = stringIndexForColumn;
            DisplayWidth = displayWidth;
            StringLength = stringLength;
        }

        /// <summary>يحوّل عموداً معروضاً إلى فهرس سلسلة (يقصّ خارج المدى إلى الحدود).</summary>
        public int ToStringIndex(int displayCol)
        {
            if (displayCol <= 0) return 0;
            if (displayCol >= DisplayWidth) return StringLength;
            return _stringIndexForColumn[displayCol];
        }
    }

    /// <summary>يبني خريطة العمود المعروض إلى فهرس السلسلة بالمشي على الرونات.</summary>
    private static ColumnMap BuildColumnMap(FrozenSpan[] spans)
    {
        string plain = LinePlainText(spans);
        var list = new List<int>();   // list[displayCol] = فهرس السلسلة لبداية تلك الخليّة
        int i = 0;
        while (i < plain.Length)
        {
            int rune;
            int adv;
            if (char.IsHighSurrogate(plain[i]) && i + 1 < plain.Length && char.IsLowSurrogate(plain[i + 1]))
            {
                rune = char.ConvertToUtf32(plain[i], plain[i + 1]);
                adv = 2;
            }
            else
            {
                rune = plain[i];
                adv = 1;
            }

            int w = RuneWidth(rune);
            for (int k = 0; k < w; k++) list.Add(i);   // كلا عمودَي الحرف العريض يشيران لبداية سلسلته
            i += adv;
        }
        return new ColumnMap(list.ToArray(), list.Count, plain.Length);
    }

    // ===== خطوط مُقاسة إلى دقّة البكسل =====

    // نُبقي مجموعة خطوط مُقاسة إن اختلف المقياس (يتجنّب إعادة إنشائها كلّ إطار).
    private float _scaledFor = -1f;
    private SKFont? _sN, _sB, _sI, _sBI;

    private (SKFont, SKFont, SKFont, SKFont) ScaledFonts(float dpiScale)
    {
        if (Math.Abs(dpiScale - 1f) < 0.001f && _font != null)
            return (_font, _fontBold!, _fontItalic!, _fontBoldItalic!);

        if (Math.Abs(dpiScale - _scaledFor) < 0.001f && _sN != null)
            return (_sN, _sB!, _sI!, _sBI!);

        float size = (float)_fontSize * dpiScale;
        _sN?.Dispose(); _sB?.Dispose(); _sI?.Dispose(); _sBI?.Dispose();
        _sN = new SKFont(_typeface, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _sB = new SKFont(_boldTypeface, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _sI = new SKFont(_typeface, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true, SkewX = -0.25f };
        _sBI = new SKFont(_boldTypeface, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true, SkewX = -0.25f };
        _scaledFor = dpiScale;
        _fallbackCache.Clear();   // الحجم تغيّر ⇒ الخطوط البديلة المُكاشة صارت بحجم قديم
        return (_sN, _sB, _sI, _sBI);
    }

    /// <summary>
    /// يعيد خطّ رسم نقطة الترميز: الأساس إن كان يملكها، وإلّا خطّاً بديلاً من نظام الخطوط
    /// (عربي/إيموجي/CJK) بنفس الحجم — كي تظهر بدل المربّع الفارغ. (بلا تشكيل HarfBuzz بعد:
    /// العربيّة تظهر بأشكال منفصلة لأنّ الشبكة خلويّة — تحسين لاحق ممكن عبر SKShaper.)
    /// </summary>
    private SKFont GlyphFont(SKFont baseFont, int rune)
    {
        var tf = baseFont.Typeface;
        if (tf != null && tf.GetGlyph(rune) != 0) return baseFont;   // الأساس يملك الحرف

        if (_fallbackCache.TryGetValue(rune, out var cached))
            return cached ?? baseFont;

        var ftf = SKFontManager.Default.MatchCharacter(rune);
        var fb = ftf != null
            ? new SKFont(ftf, baseFont.Size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true }
            : null;
        _fallbackCache[rune] = fb;
        return fb ?? baseFont;
    }

    /// <summary>مُشكِّل HarfBuzz مُكاش لـtypeface معيّن.</summary>
    private SKShaper ShaperFor(SKTypeface tf)
    {
        if (!_shapers.TryGetValue(tf, out var s)) { s = new SKShaper(tf); _shapers[tf] = s; }
        return s;
    }

    /// <summary>هل يحتوي النصّ حروفاً عربيّة (تحتاج تشكيلاً/اتّصالاً وRTL)؟</summary>
    private static bool ContainsArabic(string s)
    {
        foreach (char c in s)
            if (IsArabic(c)) return true;
        return false;
    }

    private static bool IsArabic(char c) =>
        (c >= '؀' && c <= 'ۿ') ||   // Arabic
        (c >= 'ݐ' && c <= 'ݿ') ||   // Arabic Supplement
        (c >= 'ࢠ' && c <= 'ࣿ') ||   // Arabic Extended-A
        (c >= 'ﭐ' && c <= '﷿') ||   // Presentation Forms-A
        (c >= 'ﹰ' && c <= '﻿');     // Presentation Forms-B

    private static int FirstArabicRune(string s)
    {
        foreach (char c in s) if (IsArabic(c)) return c;
        return s.Length > 0 ? s[0] : ' ';
    }

    // ===== أدوات مساعدة =====

    /// <summary>يحوّل لون WPF إلى لون Skia.</summary>
    private static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);

    /// <summary>
    /// عرض نقطة الترميز بالخلايا: 2 للأحرف العريضة (CJK/Hangul/kana/fullwidth/إيموجي) وإلّا 1.
    /// نسخة مصغّرة من فحص العرض في <c>TerminalScreen</c> لتطابق المحاذاة.
    /// </summary>
    private static int RuneWidth(int r)
    {
        if (r < 0x1100) return 1;
        if ((r >= 0x1100 && r <= 0x115F) ||   // Hangul Jamo
            (r >= 0x2E80 && r <= 0xA4CF) ||   // CJK radicals..Yi (subset)
            (r >= 0x3000 && r <= 0x303E) ||   // CJK symbols/punctuation
            (r >= 0x3041 && r <= 0x33FF) ||   // Hiragana..CJK symbols
            (r >= 0x3400 && r <= 0x4DBF) ||   // CJK Ext-A
            (r >= 0x4E00 && r <= 0x9FFF) ||   // CJK Unified
            (r >= 0xAC00 && r <= 0xD7A3) ||   // Hangul syllables
            (r >= 0xF900 && r <= 0xFAFF) ||   // CJK compat ideographs
            (r >= 0xFF00 && r <= 0xFF60) ||   // fullwidth forms
            (r >= 0xFFE0 && r <= 0xFFE6) ||   // fullwidth signs
            (r >= 0x1F300 && r <= 0x1FAFF) || // emoji/symbols
            (r >= 0x20000 && r <= 0x3FFFD))   // CJK Ext-B..
            return 2;
        return 1;
    }
}
