using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using TerminalLauncher.Services;
using TerminalLauncher.Theme;

namespace TerminalLauncher.Views;

/// <summary>
/// لوحة «ما الجديد» — بطاقة داكنة/فاتحة (تتبع الثيم) بلا إطار نظام، تُحلّل CHANGELOG.md وقت التشغيل
/// وتعرض تاريخ الإصدارات كاملاً: لكلّ نسخة ترويسة (تاريخ منسّق + شارة رقم Monospace) ثم صندوق المميّزات
/// (إن وُجد) فالأقسام (NEW/IMPROVED/FIXED) بعناوين مترجمة وبنود حرفية، وخطّ فاصل بين النسخ.
/// الاتجاه يتبع اللغة (RTL/LTR)، والأرقام لاتينية دائماً.
/// </summary>
public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow(string? version = null)
    {
        InitializeComponent();
        FlowDirection = Loc.Flow;
        HeaderTitle.Text = Loc.T("whatsnew.title");
        CloseBtn.ToolTip = Loc.T("common.close");
        _ = version; // النسخة لم تعد تُصفّي العرض: نعرض تاريخ الإصدارات كاملاً.
        Render();
        Closed += (_, _) => BlurOwner(false);
    }

    /// <summary>قبل أوّل رسم: تغطية مساحة الأداة (طبقة كاملة) وتضبيب ما خلف اللوحة.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CoverOwnerOrScreen();
        BlurOwner(true);
    }

    /// <summary>يجعل النافذة تغطّي مساحة الأداة المالكة (أو منطقة العمل) لتظهر الخلفية المعتّمة كاملةً.</summary>
    private void CoverOwnerOrScreen()
    {
        if (Owner != null && Owner.WindowState == WindowState.Normal &&
            Owner.ActualWidth > 0 && Owner.ActualHeight > 0)
        {
            Left = Owner.Left;
            Top = Owner.Top;
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left;
            Top = wa.Top;
            Width = wa.Width;
            Height = wa.Height;
        }
    }

    private Effect? _ownerPrevEffect;

    /// <summary>يضبّب محتوى الأداة خلف اللوحة (ويستعيده عند الإغلاق) — تأثير «ضبابية الخلفية».</summary>
    private void BlurOwner(bool on)
    {
        if (Owner?.Content is not UIElement content) return;
        if (on)
        {
            _ownerPrevEffect = content.Effect;
            content.Effect = new BlurEffect
            {
                Radius = 16,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance,
            };
        }
        else
        {
            content.Effect = _ownerPrevEffect;
        }
    }

    private static Brush Res(string key) =>
        Application.Current?.Resources[key] as Brush ?? Brushes.Gray;

    /// <summary>يبني اللوحة من كلّ مدخلات CHANGELOG.md (أو احتياط نصّي لو غاب الملف).</summary>
    private void Render()
    {
        var entries = Changelog.Load();   // كل الإصدارات (الأحدث أوّلاً كما في الملف)
        ContentHost.Children.Clear();

        if (entries.Count == 0)
        {
            ContentHost.Children.Add(new TextBlock
            {
                Text = Loc.T("whatsnew.empty"),
                Foreground = Res("Brush.TextMuted"),
                FontSize = 13.5,
                Margin = new Thickness(2, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        // نعرض تاريخ الإصدارات كاملاً (لا الأحدث فقط): لكل إصدار ترويسته (تاريخ + شارة نسخة)
        // ثم أقسامه، ويفصل بين كل إصدار وآخر خطّ رفيع. المحتوى داخل ScrollViewer قابل للتمرير.
        for (int i = 0; i < entries.Count; i++)
        {
            ContentHost.Children.Add(BuildEntryHeader(entries[i], i == 0));
            RenderEntrySections(entries[i]);
            if (i < entries.Count - 1)
                ContentHost.Children.Add(BuildSeparator());
        }
    }

    /// <summary>ترويسة إصدار داخل القائمة: التاريخ + شارة النسخة (Monospace) في سطر واحد.</summary>
    private FrameworkElement BuildEntryHeader(ChangelogEntry entry, bool first)
    {
        var grid = new Grid { Margin = new Thickness(0, first ? 2 : 18, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var date = new TextBlock
        {
            Text = FormatDate(entry.Date),
            Foreground = Res("Brush.Text"),
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            // يلتصق بالحافة البادئة (المقابلة لشارة الإصدار في العمود الآخر) بدل الطفو نحو المنتصف؛
            // المحاذاة تُحسَب بحسب اتجاه الشبكة فتنعكس تلقائياً بين RTL/LTR.
            HorizontalAlignment = HorizontalAlignment.Left,
            FlowDirection = FlowDirection.LeftToRight,   // إبقاء الأرقام لاتينية بترتيبها
        };
        Grid.SetColumn(date, 0);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 5, 11, 5),
            Background = Res("Brush.Surface"),
            BorderBrush = Res("Brush.Border"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = entry.Version,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Res("Brush.Accent"),
                FlowDirection = FlowDirection.LeftToRight,
            },
        };
        Grid.SetColumn(badge, 1);

        grid.Children.Add(date);
        grid.Children.Add(badge);
        return grid;
    }

    /// <summary>خطّ رفيع فاصل بين الإصدارات.</summary>
    private Border BuildSeparator() => new Border
    {
        Height = 1,
        Background = Res("Brush.Border"),
        Opacity = 0.7,
        Margin = new Thickness(0, 18, 0, 2),
    };

    /// <summary>يبني أقسام إصدار واحد: صندوق «المميّزات» (إن وُجد) ثم NEW/IMPROVED/FIXED.</summary>
    private void RenderEntrySections(ChangelogEntry entry)
    {
        var highlights = entry.Sections.FirstOrDefault(
            s => string.Equals(s.Key.Trim(), "HIGHLIGHTS", StringComparison.OrdinalIgnoreCase));
        if (highlights != null)
            ContentHost.Children.Add(BuildHighlightsBox(highlights));

        bool first = highlights == null;
        foreach (var section in entry.Sections)
        {
            if (section == highlights) continue;
            ContentHost.Children.Add(BuildSectionLabel(section.Key, first));
            first = false;
            foreach (var item in section.Items)
                ContentHost.Children.Add(BuildBullet(item));
        }
    }

    /// <summary>صندوق «أبرز المميّزات» — لكنة بارزة تلخّص أهمّ مميّزات النسخة أعلى قسمها.</summary>
    private Border BuildHighlightsBox(ChangelogSection section)
    {
        var panel = new StackPanel();

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        header.Children.Add(new TextBlock
        {
            Text = char.ConvertFromUtf32(0xE735),                    // نجمة (Segoe MDL2)
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = Res("Brush.Accent"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FlowDirection = FlowDirection.LeftToRight,        // الرمز لا ينعكس تحت RTL
        });
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("whatsnew.highlights"),
            Foreground = Res("Brush.Accent"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(header);

        foreach (var item in section.Items)
            panel.Children.Add(BuildBullet(item));

        return new Border
        {
            Background = Res("Brush.AccentSoft"),
            BorderBrush = Res("Brush.Accent"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 6),
            Margin = new Thickness(0, 2, 0, 8),
            Child = panel,
        };
    }

    /// <summary>عنوان قسم (NEW/IMPROVED/FIXED) — مفتاح مترجم بأحرف كبيرة خافتة.</summary>
    private TextBlock BuildSectionLabel(string key, bool first)
    {
        return new TextBlock
        {
            Text = SectionKeyToLabel(key),
            Foreground = Res("Brush.TextMuted"),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, first ? 2 : 20, 0, 8),
        };
    }

    /// <summary>سطر بند: مؤشّر لكنة + نص حرفي من الملف (يلتفّ عند الطول).</summary>
    private Border BuildBullet(string text)
    {
        var grid = new Grid { Margin = new Thickness(2, 0, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // مؤشّر مثلّث صغير — لا نثبّت اتجاهه فينعكس تلقائياً لليسار تحت RTL.
        var marker = new Path
        {
            Data = Geometry.Parse("M0,0 L5,3.5 L0,7 Z"),
            Fill = Res("Brush.TextMuted"),
            Width = 5,
            Height = 7,
            Stretch = Stretch.Fill,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 11, 0),
        };
        Grid.SetColumn(marker, 0);

        var tb = new TextBlock
        {
            Text = text,
            Foreground = Res("Brush.Text"),
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        };
        Grid.SetColumn(tb, 1);

        grid.Children.Add(marker);
        grid.Children.Add(tb);
        return new Border { Child = grid };
    }

    /// <summary>يحوّل مفتاح القسم في الملف إلى نصّه المترجم.</summary>
    private static string SectionKeyToLabel(string key) => key.Trim().ToUpperInvariant() switch
    {
        "NEW" => Loc.T("whatsnew.section.new"),
        "IMPROVED" => Loc.T("whatsnew.section.improved"),
        "FIXED" => Loc.T("whatsnew.section.fixed"),
        _ => key.Trim(),
    };

    /// <summary>تنسيق التاريخ ISO إلى صيغة مقروءة؛ يُبقي النص كما هو إن تعذّر التحليل.</summary>
    private static string FormatDate(string iso)
    {
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d))
            return d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        return iso;
    }

    private void ChromeGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ChromeGrid.Clip = new RectangleGeometry(new Rect(e.NewSize), 17, 17);

    /// <summary>نقر الخلفية المعتّمة خارج البطاقة يُغلق اللوحة.</summary>
    private void Scrim_MouseDown(object sender, MouseButtonEventArgs e) => Close();

    /// <summary>Esc يُغلق اللوحة.</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ===================== منطق العرض عند الإقلاع + إعادة الفتح يدوياً =====================

    /// <summary>
    /// يعرض اللوحة مرّة واحدة إن تغيّرت النسخة منذ آخر عرض (يُستدعى من MainWindow بعد الظهور).
    /// غير حاجب (Show، لا ShowDialog) كي لا يزاحم الإقلاع. يخزّن آخر نسخة عُرضت في الإعدادات ويحفظها.
    /// </summary>
    public static void ShowIfNew(Window? owner, AppSettings settings, SettingsStore store)
    {
        if (settings.LastWhatsNewVersion == AppVersion.Current) return;
        try
        {
            var w = new WhatsNewWindow(AppVersion.Current);
            if (owner != null) w.Owner = owner;
            w.Show();
            settings.LastWhatsNewVersion = AppVersion.Current;
            store.Save(settings);
        }
        catch { /* لا نُسقط الإقلاع بسبب لوحة عرض */ }
    }

    /// <summary>فتح يدوي (من زرّ «ما الجديد / حول») — حاجب (ShowDialog).</summary>
    public static void ShowManual(Window? owner, string? version = null)
    {
        var w = new WhatsNewWindow(version ?? AppVersion.Current);
        if (owner != null) w.Owner = owner;
        w.ShowDialog();
    }
}
