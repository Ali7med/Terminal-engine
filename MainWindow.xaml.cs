using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using TerminalLauncher.Controls;
using TerminalLauncher.Interop;
using TerminalLauncher.Models;
using TerminalLauncher.Services;
using TerminalLauncher.Terminal;
using TerminalLauncher.Theme;
using Terminal.Storage;
using AppThemeMode = TerminalLauncher.Theme.ThemeMode;

namespace TerminalLauncher;

public partial class MainWindow : Window
{
    private readonly EntryStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly ProfileStore _profileStore = new();
    private readonly SessionStore _sessionStore = new(new AppDatabase());
    private readonly Services.ServerProfileService _serverProfiles =
        new(new global::Terminal.Storage.ServerProfileStore(new AppDatabase()));
    private Views.ServerMonitorWindow? _serverMonitorWindow;
    private readonly ObservableCollection<CommandEntry> _entries = new();
    private ProfilesData _profiles = new();
    private readonly AppSettings _settings;
    private bool _settingsOpen;
    private bool _sidebarPinned;   // لوحة الأوامر مثبَّتة مفتوحة (لا تُغلَق عند مغادرة الماوس)
    private bool _restoring;   // true أثناء استرجاع الجلسة كي لا يُثبِّت OpenTerminal لقطات وسطيّة
    private bool _syncingUi = true;   // يبقى true أثناء الإنشاء ليُتجاهَل ValueChanged/SelectionChanged المبكّر
    private CommandEntry? _editorTarget;
    private const int WindowCornerRadius = 6;
    private readonly List<Border> _themeCards = new();
    private const int ThemeFirstGroup = 6;     // عدد بطاقات الثيمات في اللوحة السريعة قبل «عرض الكلّ»
    private readonly List<Border> _bgSwatches = new();   // بطاقات معرض قوالب الخلفيّة (المفتاح في Tag)

    /// <summary>
    /// خيار خطّ: اسم للعرض + قيمة تُمرَّر لـ FontFamily (اسم نظام أو مرجع مورد مضمّن).
    /// <b>عام عمداً</b>: ربط WPF (DisplayMemberPath/SelectedValuePath) يفشل على الأنواع الخاصّة
    /// فيرتدّ إلى ToString() (كان يعرض السجلّ كاملاً). النوع العام يجعل الربط ينجح.
    /// </summary>
    public sealed record FontOption(string Display, string Value)
    {
        public override string ToString() => Display;
    }

    // خطوط أحاديّة المسافة شائعة + خطّ Claude المضمّن (مورد داخل الأداة).
    private static readonly FontOption[] FontChoices =
    {
        new("Cascadia Mono",           "Cascadia Mono"),
        new("Cascadia Code",           "Cascadia Code"),
        new("Consolas",                "Consolas"),
        new("JetBrains Mono",          "JetBrains Mono"),
        new("Courier New",             "Courier New"),
        new("Lucida Console",          "Lucida Console"),
        new("Claude (Anthropic Sans)", "./Assets/Fonts/#Anthropic Sans"),
    };

    // ألوان كتابة افتراضية جاهزة (#RRGGBB).
    private static readonly (string Name, string Hex)[] TextColorChoices =
    {
        ("فاتح",       "#D4D4D4"),
        ("أبيض",       "#FFFFFF"),
        ("أزرق فاتح",  "#C0CAF5"),
        ("أخضر",       "#9ECE6A"),
        ("كهرماني",    "#E0AF68"),
        ("رماديّ",     "#A9B1D6"),
    };

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsStore.Load();
        Loc.InitFromCode(_settings.Language);
        if (_settings.SyncThemeWithOs)   // المزامنة تتجاوز الثيم المختار عند الإقلاع
            _settings.ThemePresetId = ThemeManager.DefaultFor(IsOsLightTheme() ? AppThemeMode.Light : AppThemeMode.Dark);
        ThemeManager.Apply(_settings);
        ApplyDefaultForeground();

        // بروفايلات الصدفات: اكتشاف تلقائيّ + دمج المخصّصة المحفوظة (T-101).
        _profiles = _profileStore.Load();
        ShellCatalog.Initialize(_profiles.CustomProfiles, _profiles.DefaultProfileId);

        foreach (var e in _store.Load()) _entries.Add(e);
        EntriesList.ItemsSource = _entries;
        SidebarDots.ItemsSource = _entries;   // مختصرات الأوامر في الشريط المطويّ
        EditorShell.ItemsSource = ShellCatalog.All;

        BuildThemeCards();
        BuildFontChoices();
        BuildTextColorSwatches();
        BuildBackgroundGallery();
        SyncSettingsUi();
        UpdateHint();
        ApplyLanguage();
        ShowCategory("appearance");   // يطبّق رؤية الفئة الابتدائيّة (حدث Checked المبكّر يُتجاهَل)

        Loaded += (_, _) => ApplyRounding();
        Loaded += (_, _) => { RestoreSession(); ApplyBackground(); };
        // لوحة «ما الجديد» تلقائياً مرّة واحدة عند الترقية — بعد ظهور الواجهة (مغلّفة بـ try/catch داخلها).
        Loaded += (_, _) => Views.WhatsNewWindow.ShowIfNew(this, _settings, _settingsStore);
        SizeChanged += (_, _) => ApplyRounding();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    // اختصارات مستوى النافذة (تُلتقط قبل عناصر الصدفة الداخلية عبر tunneling).
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;

        // Esc: يغلق لوحة الأوامر إن كانت مفتوحة.
        if (e.Key == Key.Escape && _commandPaletteOpen)
        {
            CloseCommandPalette();
            e.Handled = true;
            return;
        }

        if (ctrl && shift)
        {
            switch (e.Key)
            {
                case Key.T: OpenEmptyTerminal(); e.Handled = true; return;            // تيرمنال فارغ جديد
                case Key.D: SplitActivePane(Orientation.Vertical); e.Handled = true; return;   // انقسام عمودي (جنباً لجنب)
                case Key.E: SplitActivePane(Orientation.Horizontal); e.Handled = true; return; // انقسام أفقي (فوق/تحت)
                case Key.P: OpenCommandPalette(); e.Handled = true; return;           // لوحة الأوامر
            }
        }

        // Ctrl+W: إغلاق الجزء النشط (لا يصطدم بـ Ctrl+W في الصدفة لأنّه يُلتقط هنا أوّلاً).
        if (ctrl && !shift && e.Key == Key.W)
        {
            CloseActivePane();
            e.Handled = true;
        }
    }

    // ===== إطار النافذة =====

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        bool max = WindowState == WindowState.Maximized;
        RootBorder.Margin = new Thickness(max ? 8 : 0);
        RootBorder.CornerRadius = new CornerRadius(max ? 0 : WindowCornerRadius);   // زوايا قائمة عند التكبير
        MaxButton.Content = max ? "" : "";
    }

    private void ApplyRounding()
    {
        if (WindowState == WindowState.Maximized) WindowEffects.ClearRoundedCorners(this);
        else WindowEffects.ApplyRoundedCorners(this, WindowCornerRadius);
    }

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ===== المظهر =====

    private void SaveSettings() => _settingsStore.Save(_settings);

    private void SyncSettingsUi()
    {
        _syncingUi = true;
        SyncOsCheck.IsChecked = _settings.SyncThemeWithOs;
        UpdateThemeSelection();

        FontSizeSlider.Value = _settings.TerminalFontSize;
        FontSizeValue.Text = ((int)_settings.TerminalFontSize).ToString();
        FontFamilyCombo.SelectedValue = _settings.FontFamily;
        UpdateTextColorSelection();

        BgOpacitySlider.Value = _settings.BackgroundOpacity;
        BgOpacityValue.Text = _settings.BackgroundOpacity.ToString("0.00");
        UpdateBackgroundUi();
        UpdateBackgroundSelection();
        _syncingUi = false;
    }

    // ===== الخلفيّة: صورة النافذة + شفافيّة التيرمنال =====

    /// <summary>يحدّث عناصر واجهة الخلفيّة (نصّ المسار + تفعيل زرّ الإزالة) من الإعدادات.</summary>
    private void UpdateBackgroundUi()
    {
        bool has = !string.IsNullOrWhiteSpace(_settings.BackgroundImagePath);
        BgPathText.Text = has ? System.IO.Path.GetFileName(_settings.BackgroundImagePath) : "";
        BgClearButton.IsEnabled = has;
    }

    /// <summary>
    /// يطبّق خلفيّة النافذة حسب <see cref="AppSettings.BackgroundKind"/>: صورة/لون مصمت/تدرّج/نقش/خلفيّة الثيم.
    /// أيّ خلفيّة ≠ "theme" تجعل التيرمنال شبه شفّاف (بشفافيّة الإعدادات) لتظهر خلفه. القيم غير الصالحة تعود للثيم.
    /// </summary>
    private void ApplyBackground()
    {
        bool nonTheme = false;
        switch (_settings.BackgroundKind)
        {
            case "image":
                nonTheme = TryApplyImageBackground();
                break;
            case "solid":
                nonTheme = TryApplySolidBackground(_settings.BackgroundValue);
                break;
            case "gradient":
            case "pattern":
                nonTheme = TryApplyTemplateBackground(_settings.BackgroundValue);
                break;
            default:   // "theme" أو أيّ قيمة مجهولة
                RootBorder.SetResourceReference(Border.BackgroundProperty, "Brush.Bg");
                break;
        }
        if (!nonTheme && _settings.BackgroundKind != "theme")
            RootBorder.SetResourceReference(Border.BackgroundProperty, "Brush.Bg");   // احتياط عند فشل القالب

        // شفافيّة التيرمنال: تُطبَّق لأيّ خلفيّة غير خلفيّة الثيم (صورة/لون/تدرّج/نقش)؛ وإلّا معتِم تماماً.
        double alpha = nonTheme ? Math.Clamp(_settings.BackgroundOpacity, 0.30, 1.00) : 1.0;
        SetTerminalContentBorderTransparent(nonTheme);
        SetSidebarScrim(nonTheme);
        SetAppHeaderScrim(nonTheme);
        PushBackgroundAlphaToAllTabs(alpha);
    }

    /// <summary>
    /// يجعل شريط الأوامر الجانبيّ زجاجيّاً فوق الخلفيّة المخصّصة كي تظهر الصورة خلفه، لكن بعتامة أعلى
    /// من التيرمنال (شفافيّة أقلّ) كي تبقى الأوامر مقروءة؛ وعند خلفيّة الثيم يعود معتِماً بلون <c>Brush.Bg</c>.
    /// </summary>
    private void SetSidebarScrim(bool nonTheme)
    {
        if (nonTheme)
        {
            // عتامة الشريط الجانبيّ = شفافيّة التيرمنال + هامش، بحدٍّ أدنى 0.85 كي تبقى الكتابة واضحة.
            double a = Math.Clamp(Math.Max(0.85, _settings.BackgroundOpacity + 0.15), 0.30, 1.00);
            var c = ThemeManager.BackgroundColor;
            SidebarRail.Background = new SolidColorBrush(
                Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B));
        }
        else
        {
            SidebarRail.SetResourceReference(Border.BackgroundProperty, "Brush.Bg");
        }
    }

    /// <summary>يُفعِّل طبقة تعتيم شريط عنوان التطبيق فوق الخلفيّة المخصّصة (كي يبقى العنوان/الأزرار ظاهرين).</summary>
    private void SetAppHeaderScrim(bool nonTheme)
    {
        if (nonTheme) AppHeaderScrim.SetResourceReference(Border.BackgroundProperty, "Brush.HeaderScrim");
        else AppHeaderScrim.Background = Brushes.Transparent;
    }

    /// <summary>يطبّق صورة الخلفيّة (UniformToFill، تُحمَّل بلا قفل الملفّ). يعيد true عند النجاح.</summary>
    private bool TryApplyImageBackground()
    {
        string path = _settings.BackgroundImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // يفكّ قفل الملفّ بعد التحميل
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            RootBorder.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            return true;
        }
        catch { return false; }
    }

    /// <summary>يطبّق لوناً مصمتاً من hex. يعيد true عند صلاحيّة اللون.</summary>
    private bool TryApplySolidBackground(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            RootBorder.Background = new SolidColorBrush(color);
            return true;
        }
        catch { return false; }
    }

    /// <summary>يطبّق قالب تدرّج/نقش بمعرّفه. يعيد true عند وجود القالب.</summary>
    private bool TryApplyTemplateBackground(string id)
    {
        var tpl = ThemeManager.ResolveBackground(id);
        if (tpl is null) return false;
        RootBorder.Background = tpl.CreateBrush();
        return true;
    }

    /// <summary>يُشفِّف (أو يعيد) خلفيّة حدود محتوى الـ TabControl (داخل قالبه) لتظهر صورة النافذة خلف التيرمنال.</summary>
    private void SetTerminalContentBorderTransparent(bool transparent)
    {
        if (TerminalTabs.Template?.FindName("TerminalContentBorder", TerminalTabs) is Border b)
        {
            if (transparent) b.Background = Brushes.Transparent;
            else b.SetResourceReference(Border.BackgroundProperty, "Brush.TerminalBg");
        }
    }

    /// <summary>شفافيّة الخلفيّة الحاليّة: شفافيّة الإعدادات لأيّ خلفيّة غير الثيم (صورة/لون/تدرّج/نقش)، وإلّا 1 (معتِم).</summary>
    private double CurrentBackgroundAlpha()
        => IsNonThemeBackgroundActive() ? Math.Clamp(_settings.BackgroundOpacity, 0.30, 1.00) : 1.0;

    /// <summary>هل الخلفيّة الحاليّة فعليّاً غير خلفيّة الثيم (خلفيّة مخصّصة صالحة)؟</summary>
    private bool IsNonThemeBackgroundActive() => _settings.BackgroundKind switch
    {
        "image"   => !string.IsNullOrWhiteSpace(_settings.BackgroundImagePath) && File.Exists(_settings.BackgroundImagePath),
        "solid"   => !string.IsNullOrWhiteSpace(_settings.BackgroundValue),
        "gradient" or "pattern" => ThemeManager.ResolveBackground(_settings.BackgroundValue) is not null,
        _ => false,
    };

    /// <summary>يدفع شفافيّة الخلفيّة إلى كلّ الأجزاء المفتوحة (تكرار على التبويبات ثمّ أجزائها).</summary>
    private void PushBackgroundAlphaToAllTabs(double alpha)
    {
        foreach (var item in TerminalTabs.Items)
            if (item is TabItem { Content: TerminalPaneContainer container })
                foreach (var view in container.AllViews)
                    view.SetBackgroundAlpha(alpha);
    }

    private void BgChooseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("bg.choose"),
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*",
        };
        if (!string.IsNullOrWhiteSpace(_settings.BackgroundImagePath))
        {
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(_settings.BackgroundImagePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            }
            catch { }
        }
        if (dlg.ShowDialog() == true)
        {
            _settings.BackgroundImagePath = dlg.FileName;
            _settings.BackgroundKind = "image";
            _settings.BackgroundValue = "";
            AddCustomImage(dlg.FileName);   // تُحفَظ في المعرض كمصغّرة قابلة لإعادة الاختيار
            UpdateBackgroundUi();
            BuildBackgroundGallery();       // لإظهار المصغّرة الجديدة فوراً
            UpdateBackgroundSelection();
            ApplyBackground();
            SaveSettings();
        }
    }

    private void BgClearButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.BackgroundImagePath = "";
        // إزالة الصورة تعود لخلفيّة الثيم (ما لم يكن قالبٌ آخر مختاراً).
        if (_settings.BackgroundKind == "image")
        {
            _settings.BackgroundKind = "theme";
            _settings.BackgroundValue = "";
        }
        UpdateBackgroundUi();
        UpdateBackgroundSelection();
        ApplyBackground();
        SaveSettings();
    }

    private void BgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi) return;
        _settings.BackgroundOpacity = Math.Round(e.NewValue, 2);
        BgOpacityValue.Text = _settings.BackgroundOpacity.ToString("0.00");
        // يؤثّر فقط حين تكون خلفيّة غير الثيم نشطة؛ ApplyBackground يقرّر الشفافيّة الفعليّة.
        ApplyBackground();
        SaveSettings();
    }

    // ===== معرض قوالب الخلفيّة =====

    /// <summary>
    /// يبني معرض القوالب: بطاقة «خلفية الثيم» أوّلاً، ثم بطاقة معاينة لكلّ قالب مضمّن (لون/تدرّج/نقش).
    /// Tag لكلّ بطاقة = "kind|value" (مثل "theme|"، "solid|#101012"، "gradient|grad-ocean").
    /// </summary>
    private void BuildBackgroundGallery()
    {
        BgGalleryPanel.Children.Clear();
        _bgSwatches.Clear();

        // بطاقة خلفيّة الثيم (تعود للسلوك الافتراضيّ).
        var themeBrush = new SolidColorBrush(ThemeManager.BackgroundColor);
        _bgSwatches.Add(AddBgSwatch("theme", "", themeBrush, Loc.T("bg.themeDefault")));

        foreach (var tpl in ThemeManager.BackgroundTemplates)
            _bgSwatches.Add(AddBgSwatch(tpl.SettingKind, tpl.SettingValue, tpl.CreateBrush(),
                Loc.Current == AppLang.Ar ? tpl.NameAr : tpl.NameEn));

        // صور المستخدم المرفوعة: مصغّرات قابلة لإعادة الاختيار (أحدثها أوّلاً؛ نقرة يمين تحذفها).
        foreach (var path in _settings.CustomBackgroundImages.ToList())
        {
            var sw = AddImageSwatch(path);
            if (sw != null) _bgSwatches.Add(sw);
        }
    }

    /// <summary>ينشئ مصغّرة معاينة لصورة مستخدم مرفوعة (تُحمَّل بحجم صغير)، أو null إن تعذّر تحميلها.</summary>
    private Border? AddImageSwatch(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;      // يفكّ قفل الملفّ بعد التحميل
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = 140;                      // مصغّرة خفيفة الذاكرة
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            brush.Freeze();

            var swatch = AddBgSwatch("image", path, brush, System.IO.Path.GetFileName(path));
            swatch.MouseRightButtonUp += CustomImageSwatch_RightClick;   // نقرة يمين = إزالة من المعرض
            return swatch;
        }
        catch { return null; }
    }

    /// <summary>يضيف صورة مستخدم إلى مقدّمة قائمة المرفوعات (بلا تكرار، بحدّ أعلى معقول).</summary>
    private void AddCustomImage(string path)
    {
        _settings.CustomBackgroundImages.RemoveAll(
            p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _settings.CustomBackgroundImages.Insert(0, path);
        const int max = 12;
        if (_settings.CustomBackgroundImages.Count > max)
            _settings.CustomBackgroundImages.RemoveRange(max, _settings.CustomBackgroundImages.Count - max);
    }

    /// <summary>نقرة يمين على مصغّرة صورة مستخدم: تحذفها من المعرض (وتعود لخلفيّة الثيم إن كانت النشطة).</summary>
    private void CustomImageSwatch_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string tag }) return;
        int bar = tag.IndexOf('|');
        if (bar < 0) return;
        string path = tag[(bar + 1)..];

        _settings.CustomBackgroundImages.RemoveAll(
            p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

        if (_settings.BackgroundKind == "image" &&
            string.Equals(_settings.BackgroundImagePath, path, StringComparison.OrdinalIgnoreCase))
        {
            _settings.BackgroundImagePath = "";
            _settings.BackgroundKind = "theme";
            _settings.BackgroundValue = "";
            UpdateBackgroundUi();
            ApplyBackground();
        }
        BuildBackgroundGallery();
        UpdateBackgroundSelection();
        SaveSettings();
        e.Handled = true;
    }

    /// <summary>ينشئ بطاقة معاينة واحدة في المعرض ويُعيدها (للتتبّع في <see cref="_bgSwatches"/>).</summary>
    private Border AddBgSwatch(string kind, string value, Brush preview, string tip)
    {
        var swatch = new Border
        {
            Width = 68,
            Height = 46,
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(0, 0, 8, 8),
            Background = preview,
            BorderThickness = new Thickness(2.5),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Tag = $"{kind}|{value}",
            ToolTip = tip,
            SnapsToDevicePixels = true,
        };
        swatch.MouseLeftButtonUp += BgSwatch_Click;
        BgGalleryPanel.Children.Add(swatch);
        return swatch;
    }

    /// <summary>يبرز البطاقة الموافقة للخلفيّة الحاليّة بإطار لون التمييز.</summary>
    private void UpdateBackgroundSelection()
    {
        string current = _settings.BackgroundKind == "image"
            ? $"image|{_settings.BackgroundImagePath}"
            : $"{_settings.BackgroundKind}|{_settings.BackgroundValue}";
        var accent = (Brush)FindResource("Brush.Accent");
        foreach (var s in _bgSwatches)
            s.BorderBrush = (string?)s.Tag == current ? accent : Brushes.Transparent;
    }

    private void BgSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string tag }) return;
        int bar = tag.IndexOf('|');
        if (bar < 0) return;
        string kind = tag[..bar];
        string value = tag[(bar + 1)..];

        if (kind == "image")
        {
            // مصغّرة صورة مستخدم: تُطبَّق كخلفيّة صورة (المسار في BackgroundImagePath).
            _settings.BackgroundImagePath = value;
            _settings.BackgroundKind = "image";
            _settings.BackgroundValue = "";
            UpdateBackgroundUi();
        }
        else
        {
            // اختيار قالب/لون/ثيم يلغي صورة الخلفيّة النشطة (تبقى في المعرض).
            _settings.BackgroundKind = kind;
            _settings.BackgroundValue = value;
        }
        UpdateBackgroundSelection();
        ApplyBackground();
        SaveSettings();
    }

    // ===== الخطّ + لون الكتابة =====

    private void BuildFontChoices() => FontFamilyCombo.ItemsSource = FontChoices;

    private void BuildTextColorSwatches()
    {
        TextColorPanel.Children.Clear();
        foreach (var (name, hex) in TextColorChoices)
        {
            var dot = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(ParseColor(hex)),
                BorderThickness = new Thickness(3),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = hex,
                ToolTip = name,
            };
            dot.MouseLeftButtonUp += TextColorSwatch_Click;
            TextColorPanel.Children.Add(dot);
        }
    }

    private void UpdateTextColorSelection()
    {
        foreach (var child in TextColorPanel.Children)
            if (child is Border dot && dot.Tag is string hex)
                dot.BorderBrush = string.Equals(hex, _settings.DefaultForeground, StringComparison.OrdinalIgnoreCase)
                    ? (Brush)FindResource("Brush.Text") : Brushes.Transparent;
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi) return;
        _settings.TerminalFontSize = Math.Round(e.NewValue);
        FontSizeValue.Text = ((int)_settings.TerminalFontSize).ToString();
        ApplyFontToAllTabs();
        SaveSettings();
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi) return;
        if (FontFamilyCombo.SelectedValue is string family)
        {
            _settings.FontFamily = family;
            ApplyFontToAllTabs();
            SaveSettings();
        }
    }

    private void TextColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border dot && dot.Tag is string hex)
        {
            _settings.DefaultForeground = hex;
            ApplyDefaultForeground();
            UpdateTextColorSelection();
            ApplyFontToAllTabs();   // إعادة البناء تلتقط اللون الجديد
            SaveSettings();
        }
    }

    /// <summary>يضبط لون الكتابة الافتراضي في لوحة ANSI من الإعدادات.</summary>
    private void ApplyDefaultForeground() => AnsiPalette.DefaultForeground = ParseColor(_settings.DefaultForeground);

    /// <summary>يطبّق نوع/حجم الخطّ الحاليّ (ويعيد البناء بلون الكتابة الجديد) على كل الأجزاء المفتوحة.</summary>
    private void ApplyFontToAllTabs()
    {
        foreach (var item in TerminalTabs.Items)
            if (item is TabItem { Content: TerminalPaneContainer container })
                foreach (var view in container.AllViews)
                    view.ApplyFontSettings(_settings.TerminalFontSize, _settings.FontFamily);
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0xD4, 0xD4, 0xD4); }
    }

    // ===== بطاقات الثيمات (معاينة تيرمنال مصغّرة ملوّنة بالثيم — على طراز Warp) =====

    /// <summary>فرشاة مصمتة مجمَّدة (لعناصر البطاقة الثابتة لكلّ ثيم).</summary>
    private static SolidColorBrush SB(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>
    /// يبني بطاقات الثيمات: المجموعة الأولى (<see cref="ThemeFirstGroup"/>) في اللوحة السريعة داخل
    /// الإعدادات، وكلّ الثيمات في القائمة المنبثقة (تُفتح بزرّ «عرض الكلّ»). كلاهما WrapPanel (عدّة بالسطر).
    /// </summary>
    private void BuildThemeCards()
    {
        ThemeCardPanel.Children.Clear();
        ThemesOverlayPanel.Children.Clear();
        _themeCards.Clear();

        var presets = ThemeManager.Presets;
        for (int i = 0; i < presets.Length; i++)
        {
            if (i < ThemeFirstGroup) ThemeCardPanel.Children.Add(BuildOneThemeCard(presets[i]));
            ThemesOverlayPanel.Children.Add(BuildOneThemeCard(presets[i]));
        }

        ThemeMoreButton.Visibility = presets.Length > ThemeFirstGroup ? Visibility.Visible : Visibility.Collapsed;
        ThemeMoreButton.Content = $"{Loc.T("settings.showAllThemes")} ({presets.Length})";
    }

    /// <summary>
    /// يبني بطاقة ثيم واحدة: معاينة تيرمنال مصغَّرة مُلوَّنة بألوان الثيم (خلفية تيرمنال الثيم + أسطر
    /// أوامر ملوّنة + فاصل + مؤشّر بلون اللكنة) والاسم تحتها، بعرض ثابت للتراصّ عدّةً في السطر (WrapPanel).
    /// </summary>
    private FrameworkElement BuildOneThemeCard(ThemeManager.ThemePreset p)
    {
        var mono = new FontFamily("Cascadia Mono, Consolas");
        bool rtl = Loc.Current == AppLang.Ar;

        var mock = new StackPanel { FlowDirection = FlowDirection.LeftToRight };

        var l1 = new TextBlock { FontFamily = mono, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 3) };
        l1.Inlines.Add(new System.Windows.Documents.Run("ls") { Foreground = SB(p.Text) });
        mock.Children.Add(l1);

        var l2 = new TextBlock { FontFamily = mono, FontSize = 10.5 };
        l2.Inlines.Add(new System.Windows.Documents.Run("dir  ") { Foreground = SB(p.Swatches[0]) });
        l2.Inlines.Add(new System.Windows.Documents.Run("executable  ")
            { Foreground = SB(p.Swatches.Length > 1 ? p.Swatches[1] : p.Accent) });
        l2.Inlines.Add(new System.Windows.Documents.Run("file") { Foreground = SB(p.TextMuted) });
        mock.Children.Add(l2);

        mock.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 11, 0, 0), Background = SB(p.Border) });
        mock.Children.Add(new Border
        {
            Width = 3, Height = 14, Margin = new Thickness(0, 9, 0, 0),
            CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = SB(p.Accent),
        });

        var card = new Border
        {
            Background = SB(p.TerminalBg),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(2),
            BorderBrush = SB(p.Border),
            Padding = new Thickness(13, 11, 13, 12),
            Cursor = Cursors.Hand,
            Tag = p.Id,
            Child = mock,
            SnapsToDevicePixels = true,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        card.MouseLeftButtonUp += ThemeCard_Click;

        var name = new TextBlock
        {
            Text = rtl ? p.NameAr : p.NameEn,
            FontSize = 12,
            Margin = new Thickness(2, 6, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

        var entry = new StackPanel { Width = 196, Margin = new Thickness(0, 0, 12, 14) };
        entry.Children.Add(card);
        entry.Children.Add(name);

        _themeCards.Add(card);
        return entry;
    }

    private void ThemeMoreButton_Click(object sender, RoutedEventArgs e) => OpenThemesOverlay();

    private void OpenThemesOverlay()
    {
        UpdateThemeSelection();   // يبرِز الثيم الحاليّ داخل القائمة
        ThemesOverlay.Visibility = Visibility.Visible;
    }

    private void CloseThemesOverlay_Click(object sender, RoutedEventArgs e)
        => ThemesOverlay.Visibility = Visibility.Collapsed;

    private void ThemesOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        => ThemesOverlay.Visibility = Visibility.Collapsed;

    /// <summary>يبدّل ظهور شريط الأوامر المحفوظة من زرّ الهدر (يعيد استعمال منطق التوسيع).</summary>
    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e) => ToggleSidebarExpanded();

    private void UpdateThemeSelection()
    {
        var accent = (Brush)FindResource("Brush.Accent");
        bool locked = _settings.SyncThemeWithOs;   // عند المزامنة تُعطَّل بطاقات الاختيار

        foreach (var card in _themeCards)
        {
            var p = ThemeManager.Resolve((string?)card.Tag);
            bool selected = (string?)card.Tag == _settings.ThemePresetId;
            // خلفيّة/ألوان البطاقة ثابتة (معاينة الثيم)؛ يتغيّر إطارها فقط عند الاختيار.
            card.BorderBrush = selected ? accent : SB(p.Border);
            // التعتيم/التعطيل عند مزامنة النظام يشمل الحاوية (بطاقة + اسم).
            if (card.Parent is UIElement entry)
            {
                entry.Opacity = locked ? 0.5 : 1.0;
                entry.IsHitTestVisible = !locked;
            }
        }
    }

    private void ThemeCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border card && card.Tag is string id)
        {
            SetPreset(id);
            // اختيار ثيم من القائمة المنبثقة يطبّقه ويُغلقها.
            if (ThemesOverlay.Visibility == Visibility.Visible)
                ThemesOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>يطبّق ثيماً بمعرّفه، يحفظه، ويحدّث الواجهة.</summary>
    private void SetPreset(string id)
    {
        _settings.ThemePresetId = id;
        ThemeManager.Apply(_settings);
        UpdateThemeSelection();
        SaveSettings();
    }

    // مزامنة الفاتح/الداكن مع نظام ويندوز.
    private void SyncOsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingUi) return;
        _settings.SyncThemeWithOs = SyncOsCheck.IsChecked == true;
        if (_settings.SyncThemeWithOs)
            _settings.ThemePresetId = ThemeManager.DefaultFor(IsOsLightTheme() ? AppThemeMode.Light : AppThemeMode.Dark);
        ThemeManager.Apply(_settings);
        UpdateThemeSelection();
        SaveSettings();
    }

    /// <summary>يقرأ وضع ويندوز (فاتح/داكن) من الريجستري؛ الافتراضي داكن عند التعذّر.</summary>
    private static bool IsOsLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    // ===== لوحة الإعدادات =====

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_settingsOpen) SyncSettingsUi();   // يعكس تغييرات التكبير (Ctrl +/-) في المنزلق
        ToggleSettings(!_settingsOpen);
    }
    /// <summary>فتح لوحة «ما الجديد / حول» يدوياً (حاجب) من شريط العنوان.</summary>
    private void WhatsNewButton_Click(object sender, RoutedEventArgs e)
        => Views.WhatsNewWindow.ShowManual(this);

    /// <summary>يفتح قائمة أدوات النظام المنسدلة تحت زرّ الأدوات (قابلة للتوسّع بأدوات مستقبليّة).</summary>
    private void ToolsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ServerMonitorButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ServerMonitorButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>يفتح نافذة «مراقب الخوادم» (نافذة واحدة تُعاد للمقدّمة إن كانت مفتوحة).</summary>
    private void ServerMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_serverMonitorWindow is { IsLoaded: true })
        {
            if (_serverMonitorWindow.WindowState == WindowState.Minimized)
                _serverMonitorWindow.WindowState = WindowState.Normal;
            _serverMonitorWindow.Activate();
            return;
        }
        _serverMonitorWindow = new Views.ServerMonitorWindow(_serverProfiles) { Owner = this };
        _serverMonitorWindow.Closed += (_, _) => _serverMonitorWindow = null;
        _serverMonitorWindow.Show();
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => ToggleSettings(false);
    private void SettingsOverlay_MouseDown(object sender, MouseButtonEventArgs e) => ToggleSettings(false);
    private void SettingsPanel_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>يبدّل الفئة المعروضة في شاشة الإعدادات (يُظهِر لوحة الفئة المختارة ويُخفي البقيّة).</summary>
    private void CategoryNav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string cat }) ShowCategory(cat);
    }

    /// <summary>
    /// يُظهِر لوحات الفئة المختارة ويُخفي البقيّة. أصناف مدموجة: «المظهر والخلفية» يُظهر المظهر+الخلفية؛
    /// «اللغة والخطّ» يُظهر اللغة+الخطّ. يُستدعى أيضاً صراحةً بعد بناء الشجرة لأنّ حدث Checked المبكّر
    /// (من IsChecked="True" في XAML) يُطلَق قبل وجود اللوحات فيُتجاهَل، فتبقى لوحة الخلفيّة مطويّة أوّل فتح.
    /// </summary>
    private void ShowCategory(string cat)
    {
        if (CatAppearance is null) return;   // أثناء التحميل قبل بناء الشجرة
        CatAppearance.Visibility = cat == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        CatBackground.Visibility = cat == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        CatLanguage.Visibility   = cat == "language"   ? Visibility.Visible : Visibility.Collapsed;
        CatFont.Visibility       = cat == "language"   ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== اللغة =====

    private void ArToggle_Click(object sender, RoutedEventArgs e) => SetLanguage(AppLang.Ar);
    private void EnToggle_Click(object sender, RoutedEventArgs e) => SetLanguage(AppLang.En);

    private void SetLanguage(AppLang lang)
    {
        if (Loc.Current == lang) { ApplyLanguage(); return; }
        Loc.Set(lang);
        _settings.Language = Loc.Code;
        _settingsStore.Save(_settings);
        ApplyLanguage();
    }

    /// <summary>يطبّق اللغة الحاليّة: اتجاه النافذة (RTL/LTR) + نصوص العناصر المرئيّة.</summary>
    private void ApplyLanguage()
    {
        FlowDirection = Loc.Flow;
        SettingsContent.FlowDirection = Loc.Flow;   // محتوى الإعدادات (وزرّ الإغلاق) يتبع اللغة

        Title = Loc.T("app.title");
        TitleText.Text = Loc.T("app.title");
        SidebarHeader.Text = Loc.T("sidebar.saved");
        SidebarSearch.Tag = Loc.T("sidebar.search");
        SidebarMenuBtn.ToolTip = Loc.T("sidebar.saved");
        RunBtn.Content = Loc.T("btn.run");
        HintLine1.Text = Loc.T("hint.pick");
        HintLine2.Text = Loc.T("hint.empty");
        SettingsTitle.Text = Loc.T("settings.title");
        // أسماء فئات التنقّل
        NavAppearance.Content = Loc.T("settings.appearanceBg");
        NavBackground.Content = Loc.T("settings.background");
        NavLanguage.Content = Loc.T("settings.langFont");
        NavFont.Content = Loc.T("settings.font");
        AppearanceLabel.Text = Loc.T("settings.theme");
        ThemeHint.Text = Loc.T("settings.themehint");
        SyncOsCheck.Content = Loc.T("settings.syncos");
        BuildThemeCards();          // الأسماء تتبع اللغة
        UpdateThemeSelection();
        BuildBackgroundGallery();   // أسماء القوالب تتبع اللغة
        UpdateBackgroundSelection();
        LangLabel.Text = Loc.T("settings.language");
        FontSizeLabel.Text = Loc.T("settings.fontsize");
        FontFamilyLabel.Text = Loc.T("settings.fontfamily");
        TextColorLabel.Text = Loc.T("settings.textcolor");
        BgTemplatesLabel.Text = Loc.T("bg.templates");
        BgCustomLabel.Text = Loc.T("bg.custom");
        BgChooseButton.Content = Loc.T("bg.choose");
        BgClearButton.Content = Loc.T("bg.clear");
        BgOpacityLabel.Text = Loc.T("bg.opacity");
        AutoSaveLabel.Text = Loc.T("settings.autosave");
        NewTabButton.ToolTip = Loc.T("tip.newtab");
        SettingsButton.ToolTip = Loc.T("tip.settings");
        WhatsNewButton.ToolTip = Loc.T("tip.whatsnew");
        ServerMonitorButton.ToolTip = Loc.T("tools.menu");
        ToolServerMonitor.Header = Loc.T("tools.serverMonitor");
        ToolsMenu.FlowDirection = Loc.Flow;
        SidebarToggleButton.ToolTip = Loc.T("tip.toggleSidebar");
        ThemesOverlayTitle.Text = Loc.T("settings.allThemes");
        ArToggle.IsChecked = Loc.Current == AppLang.Ar;
        EnToggle.IsChecked = Loc.Current == AppLang.En;
    }

    private void ToggleSettings(bool open)
    {
        _settingsOpen = open;
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var scale = new DoubleAnimation
        {
            To = open ? 1.0 : 0.96,
            Duration = duration,
            EasingFunction = ease,
        };
        var fade = new DoubleAnimation
        {
            To = open ? 1.0 : 0.0,
            Duration = duration,
            EasingFunction = ease,
        };

        if (open)
        {
            SettingsOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            fade.Completed += (_, _) => { if (!_settingsOpen) SettingsOverlay.Visibility = Visibility.Collapsed; };
        }
        SettingsPanelTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        SettingsPanelTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        SettingsOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // ===== الشريط الجانبي: إجراءات =====

    private CommandEntry? Selected => EntriesList.SelectedItem as CommandEntry;

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } entry) OpenTerminal(entry);
        else MessageBox.Show("اختر أمراً من القائمة أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } entry) OpenTerminal(entry);
    }

    // يحدّد العنصر تحت المؤشّر قبل ظهور القائمة السياقية.
    private void EntriesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EntriesList, (DependencyObject)e.OriginalSource) is ListBoxItem item)
            item.IsSelected = true;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } entry) OpenEditor(entry);
        else MessageBox.Show("اختر أمراً لتعديله.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } entry)
        {
            _entries.Remove(entry);
            _store.Save(_entries);
        }
    }

    // ===== نافذة الإضافة/التعديل =====

    private void OpenEditor(CommandEntry? entry)
    {
        _editorTarget = entry;
        EditorTitle.Text = entry is null ? "إضافة أمر" : "تعديل أمر";
        EditorName.Text = entry?.Name ?? "";
        EditorPath.Text = entry?.Path ?? "";
        EditorCommand.Text = entry?.Command ?? "";
        EditorShell.SelectedItem = ShellCatalog.Get(entry?.Shell);
        EditorOverlay.Visibility = Visibility.Visible;
        EditorName.Focus();
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e) => EditorOverlay.Visibility = Visibility.Collapsed;
    private void EditorOverlay_MouseDown(object sender, MouseButtonEventArgs e) => EditorOverlay.Visibility = Visibility.Collapsed;
    private void EditorCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void EditorBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "اختر المجلد" };
        if (!string.IsNullOrWhiteSpace(EditorPath.Text) && Directory.Exists(EditorPath.Text))
            dlg.InitialDirectory = EditorPath.Text;
        if (dlg.ShowDialog() == true) EditorPath.Text = dlg.FolderName;
    }

    private void SaveEditor_Click(object sender, RoutedEventArgs e)
    {
        var name = EditorName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("أدخل اسماً للأمر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string shellKey = (EditorShell.SelectedItem as ShellDef)?.Key ?? "cmd";
        var target = _editorTarget;
        if (target is null)
        {
            target = new CommandEntry();
            _entries.Add(target);
        }
        target.Name = name;
        target.Path = EditorPath.Text.Trim();
        target.Command = EditorCommand.Text.Trim();
        target.Shell = shellKey;

        _store.Save(_entries);
        EntriesList.Items.Refresh();
        EntriesList.SelectedItem = target;
        EditorOverlay.Visibility = Visibility.Collapsed;
    }

    // ===== التيرمنالات =====

    /// <summary>يفتح تيرمنالاً فارغاً (بلا أمر محفوظ) بصدفة الجزء النشط الحاليّ أو البروفايل الافتراضيّ.</summary>
    private void OpenEmptyTerminal()
    {
        string shell = ActiveContainer?.ActiveView?.CurrentShellKey ?? ShellCatalog.DefaultKey;
        OpenTerminalForProfile(shell);
    }

    /// <summary>يفتح تيرمنالاً فارغاً ببروفايل صدفة محدّد (اسم التاب = اسم البروفايل).</summary>
    private void OpenTerminalForProfile(string profileId)
    {
        var profile = ShellCatalog.GetProfile(profileId);
        string name = profile?.Name ?? "تيرمنال";
        OpenTerminal(new CommandEntry { Name = name, Shell = profileId });
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => OpenEmptyTerminal();

    /// <summary>سهم القائمة بجانب زرّ «+»: يعرض كل البروفايلات (مكتشَفة + مخصّصة) + إدارتها (T-101.4).</summary>
    private void NewTabDropDown_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { FlowDirection = Loc.Flow };
        foreach (var p in ShellCatalog.Profiles)
        {
            if (!p.Available) continue;
            var captured = p.Id;
            var item = new MenuItem { Header = p.DisplayLabel };
            item.Click += (_, _) => OpenTerminalForProfile(captured);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = Loc.T("profiles.manage") };
        manage.Click += (_, _) => OpenProfileManager();
        menu.Items.Add(manage);

        menu.PlacementTarget = sender as UIElement;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ===== إدارة بروفايلات الصدفات (T-101.4) =====

    private ShellProfile? _profileEditorTarget;

    /// <summary>يفتح نافذة إدارة البروفايلات (قائمة + افتراضيّ + إضافة/تعديل/حذف).</summary>
    private void OpenProfileManager()
    {
        RefreshProfileManager();
        ProfileManagerOverlay.Visibility = Visibility.Collapsed;   // إعادة ضبط قبل الإظهار
        ProfileManagerOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>يعيد بناء قائمة البروفايلات وكومبو الافتراضيّ من الكتالوج الحاليّ.</summary>
    private void RefreshProfileManager()
    {
        _syncingUi = true;
        ProfileList.ItemsSource = null;
        ProfileList.ItemsSource = ShellCatalog.Profiles;
        DefaultProfileCombo.ItemsSource = null;
        DefaultProfileCombo.ItemsSource = ShellCatalog.Profiles;
        DefaultProfileCombo.SelectedValue = ShellCatalog.DefaultKey;
        _syncingUi = false;
    }

    private void ProfileManagerOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        => ProfileManagerOverlay.Visibility = Visibility.Collapsed;

    private void ProfileManagerClose_Click(object sender, RoutedEventArgs e)
        => ProfileManagerOverlay.Visibility = Visibility.Collapsed;

    private void DefaultProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi) return;
        if (DefaultProfileCombo.SelectedValue is string id)
        {
            _profiles.DefaultProfileId = id;
            _profileStore.Save(_profiles);
            ShellCatalog.Initialize(_profiles.CustomProfiles, _profiles.DefaultProfileId);
        }
    }

    private void ProfileAdd_Click(object sender, RoutedEventArgs e) => OpenProfileEditor(null);

    private void ProfileEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is ShellProfile { IsBuiltIn: false } p) OpenProfileEditor(p);
        else MessageBox.Show("اختر بروفايلاً مخصّصاً لتعديله (الصدفات المكتشَفة غير قابلة للتعديل).",
            "بروفايلات الصدفات", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is ShellProfile { IsBuiltIn: false } p)
        {
            _profiles.CustomProfiles.RemoveAll(x => x.Id == p.Id);
            if (_profiles.DefaultProfileId == p.Id) _profiles.DefaultProfileId = null;
            _profileStore.Save(_profiles);
            ShellCatalog.Initialize(_profiles.CustomProfiles, _profiles.DefaultProfileId);
            EditorShell.ItemsSource = ShellCatalog.All;
            RefreshProfileManager();
        }
        else MessageBox.Show("اختر بروفايلاً مخصّصاً لحذفه.", "بروفايلات الصدفات",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>يفتح محرّر البروفايل المخصّص (إضافة إن كان null، أو تعديل نسخة).</summary>
    private void OpenProfileEditor(ShellProfile? profile)
    {
        _profileEditorTarget = profile;
        ProfileEditorTitle.Text = profile is null ? "بروفايل مخصّص جديد" : "تعديل بروفايل";
        ProfileEditorName.Text = profile?.Name ?? "";
        ProfileEditorExe.Text = profile?.ExePath ?? "";
        ProfileEditorArgs.Text = profile?.Arguments ?? "";
        ProfileEditorWorkDir.Text = profile?.WorkingDirectory ?? "";
        ProfileEditorEnv.Text = profile is null ? "" : EnvToText(profile.EnvironmentVariables);
        ProfileEditorOverlay.Visibility = Visibility.Visible;
        ProfileEditorName.Focus();
    }

    private void ProfileEditorOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        => ProfileEditorOverlay.Visibility = Visibility.Collapsed;

    private void ProfileEditorCancel_Click(object sender, RoutedEventArgs e)
        => ProfileEditorOverlay.Visibility = Visibility.Collapsed;

    private void ProfileEditorSave_Click(object sender, RoutedEventArgs e)
    {
        string name = ProfileEditorName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("أدخل اسماً للبروفايل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string exe = ProfileEditorExe.Text.Trim();
        string args = ProfileEditorArgs.Text.Trim();
        if (exe.Length == 0 && args.Length == 0)
        {
            MessageBox.Show("أدخل ملفّاً تنفيذيّاً أو سطر أمر (وسائط).", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = _profileEditorTarget;
        if (target is null)
        {
            target = new ShellProfile { IsBuiltIn = false };
            _profiles.CustomProfiles.Add(target);
        }
        target.Name = name;
        target.Icon = "★";
        target.ExePath = exe.Length > 0 ? exe : null;
        target.Arguments = args.Length > 0 ? args : null;
        target.WorkingDirectory = ProfileEditorWorkDir.Text.Trim() is { Length: > 0 } wd ? wd : null;
        target.EnvironmentVariables = ParseEnv(ProfileEditorEnv.Text);
        // صدفات يونكس المألوفة تستعمل \n؛ نُبقي \r لصدفات ويندوز (الافتراض).
        target.Newline = exe.Contains("bash", StringComparison.OrdinalIgnoreCase)
            || exe.Contains("wsl", StringComparison.OrdinalIgnoreCase) ? "\n" : "\r";

        _profileStore.Save(_profiles);
        ShellCatalog.Initialize(_profiles.CustomProfiles, _profiles.DefaultProfileId);
        EditorShell.ItemsSource = ShellCatalog.All;   // يُحدِّث كومبو الصدفة في محرّر الأوامر
        RefreshProfileManager();
        ProfileEditorOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>يحوّل قاموس متغيّرات البيئة إلى نصّ (سطر لكل KEY=VALUE).</summary>
    private static string EnvToText(Dictionary<string, string> env)
        => string.Join(Environment.NewLine, env.Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>يحلّل نصّ متغيّرات البيئة (سطر لكل KEY=VALUE) إلى قاموس (يتجاهل الأسطر الفارغة/بلا =).</summary>
    private static Dictionary<string, string> ParseEnv(string text)
    {
        var dict = new Dictionary<string, string>();
        foreach (var line in text.Split('\n'))
        {
            string t = line.Trim();
            int eq = t.IndexOf('=');
            if (eq <= 0) continue;
            string key = t[..eq].Trim();
            string val = t[(eq + 1)..].Trim();
            if (key.Length > 0) dict[key] = val;
        }
        return dict;
    }

    /// <summary>حاوية الأجزاء للتبويب النشط (أو null إن لا تبويب).</summary>
    private TerminalPaneContainer? ActiveContainer
        => (TerminalTabs.SelectedItem as TabItem)?.Content as TerminalPaneContainer;

    /// <summary>ينشئ عرض تيرمنال جديداً مطبّقاً عليه تفضيلات الخطّ الحاليّة. <paramref name="sessionId"/> يُمرَّر عند الاسترجاع.</summary>
    private TerminalTabView CreateTerminalView(CommandEntry entry, string? sessionId = null)
    {
        var view = new TerminalTabView(entry, () => _store.Save(_entries), ToggleSidebarExpanded,
            _settings.TerminalFontSize, OnTerminalFontSizeChanged, () => _settings.AiAssistantEnabled, sessionId);
        view.ApplyFontSettings(_settings.TerminalFontSize, _settings.FontFamily);   // نوع الخطّ + لون الكتابة الحاليّان
        view.SetBackgroundAlpha(CurrentBackgroundAlpha());   // شفافيّة الخلفيّة الحاليّة (إن كانت صورة نشطة)
        view.DetachRequested += DetachViewToWindow;   // زرّ الفصل → نافذة مستقلّة
        return view;
    }

    /// <summary>
    /// يفصل عرض تيرمنال إلى نافذة مستقلّة: يجده في حاويته، ينزعه دون إنهاء جلسته (الحاوية تطوى/التبويب
    /// يُزال إن كان آخر جزء)، ثم يستضيفه في <see cref="TerminalHostWindow"/>. الجلسة الحيّة تُنقَل كما هي.
    /// </summary>
    private void DetachViewToWindow(TerminalTabView view)
    {
        TerminalPaneContainer? owner = null;
        string title = "تيرمنال";
        foreach (var item in TerminalTabs.Items)
            if (item is TabItem { Content: TerminalPaneContainer c } tab && c.AllViews.Contains(view))
            {
                owner = c;
                title = HeaderTitle(tab) ?? title;
                break;
            }
        if (owner is null || !owner.DetachView(view)) return;   // نزع من الشجرة (قد يطلق Emptied → CloseTab)
        UpdateHint();

        var host = new TerminalHostWindow(view, title);
        host.Show();
        view.FocusTerminal();
    }

    /// <summary>يفتح الأمر في تبويب جديد يحمل حاوية أجزاء بجزء واحد. <paramref name="sessionId"/> يُمرَّر عند الاسترجاع.</summary>
    private void OpenTerminal(CommandEntry entry, string? sessionId = null)
    {
        var container = new TerminalPaneContainer(CreateTerminalView(entry, sessionId));
        var tab = new TabItem { Content = container, Header = BuildHeader(entry.Name, out var closeButton), Tag = entry };
        container.Emptied += _ => CloseTab(tab);
        closeButton.Click += (_, _) => CloseTab(tab);
        TerminalTabs.Items.Add(tab);
        TerminalTabs.SelectedItem = tab;
        UpdateHint();
        if (!_restoring) SaveSession();   // نُثبّت اللقطة فور فتح التبويب (لا تعتمد على الإغلاق السليم)
    }

    /// <summary>يغلق التبويب: ينهي كل جلسات أجزائه أولاً (تنظيف الـ PTY) ثم يزيله.</summary>
    private void CloseTab(TabItem tab)
    {
        if (tab.Content is TerminalPaneContainer container)
            foreach (var view in container.AllViews)
                view.CloseSession(deleteHistory: true);   // إغلاق المستخدم للتاب يمسح تاريخ جلساته
        TerminalTabs.Items.Remove(tab);
        UpdateHint();
        // إصلاح: تثبيت اللقطة فور الإغلاق كي لا يعود التبويب المغلَق عند التشغيل التالي حتى لو
        // أُنهي البرنامج بلا إغلاق سليم (كان يُحفَظ في OnClosing فقط، فيبقى المغلَق في لقطة سابقة).
        if (!_restoring) SaveSession();
    }

    // ===== التقسيمات (Ctrl+Shift+D عمودي · Ctrl+Shift+E أفقي · Ctrl+W إغلاق الجزء) =====

    /// <summary>يقسم الجزء النشط في التبويب النشط (أو يفتح تبويباً جديداً إن لا يوجد).</summary>
    private void SplitActivePane(Orientation orientation)
    {
        var container = ActiveContainer;
        if (container?.ActiveView is { } active)
            container.Split(orientation, CreateTerminalView(new CommandEntry
            {
                Name = "تيرمنال",
                Shell = active.CurrentShellKey,
            }));
        else
            OpenEmptyTerminal();   // لا تبويب مفتوح: التقسيم يبدأ بتيرمنال جديد
    }

    /// <summary>يغلق الجزء النشط (وينهي الجلسة)؛ الحاوية تغلق التبويب إن كان آخر جزء.</summary>
    private void CloseActivePane()
    {
        ActiveContainer?.CloseActivePane();
        UpdateHint();
    }

    private void UpdateHint()
        => EmptyHintCard.Visibility = TerminalTabs.HasItems ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>يُحفظ حجم خطّ التيرمنال (Ctrl +/-) ليبقى بين التشغيلات وللتبويبات الجديدة.</summary>
    private void OnTerminalFontSizeChanged(double size)
    {
        _settings.TerminalFontSize = size;
        SaveSettings();
    }

    private StackPanel BuildHeader(string title, out Button closeButton)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? "تيرمنال" : title,
            VerticalAlignment = VerticalAlignment.Center,
        });
        closeButton = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("TabCloseButton"),
            ToolTip = "إغلاق التاب",
        };
        panel.Children.Add(closeButton);
        return panel;
    }

    // ===== لوحة الأوامر المحفوظة: شريط مطويّ يفتح لوحةً بالمرور/النقر =====

    private readonly TranslateTransform _sidebarFlyoutTransform = new();

    /// <summary>يبدّل تثبيت لوحة الأوامر (يُستدعى من زرّ شريط العنوان وزرّ التوسيع في التيرمنال). يعيد حالة الفتح.</summary>
    private bool ToggleSidebarExpanded()
    {
        _sidebarPinned = !_sidebarPinned;
        ShowSidebarFlyout(_sidebarPinned);
        return _sidebarPinned;
    }

    private void SidebarRail_MouseEnter(object sender, MouseEventArgs e) => ShowSidebarFlyout(true);
    private void SidebarMenu_Click(object sender, RoutedEventArgs e) => ToggleSidebarExpanded();
    private void SidebarFlyout_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_sidebarPinned) ShowSidebarFlyout(false);
    }

    /// <summary>يفتح/يغلق لوحة الأوامر بانزلاق + تلاشٍ خفيف (≈150مي).</summary>
    private void ShowSidebarFlyout(bool show)
    {
        SidebarFlyout.RenderTransform = _sidebarFlyoutTransform;
        if (show)
        {
            if (SidebarFlyout.Visibility == Visibility.Visible) return;
            SidebarFlyout.Visibility = Visibility.Visible;
            var dur = TimeSpan.FromMilliseconds(150);
            SidebarFlyout.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
            _sidebarFlyoutTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-14, 0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            SidebarSearch.Focus();
        }
        else
        {
            if (SidebarFlyout.Visibility != Visibility.Visible) return;
            var dur = TimeSpan.FromMilliseconds(130);
            var fade = new DoubleAnimation(1, 0, dur);
            fade.Completed += (_, _) => { if (SidebarFlyout.Opacity == 0) SidebarFlyout.Visibility = Visibility.Collapsed; };
            SidebarFlyout.BeginAnimation(OpacityProperty, fade);
            _sidebarFlyoutTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, -14, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        }
    }

    /// <summary>نقر مختصر أمر في الشريط المطويّ: يشغّله مباشرةً ويُغلق اللوحة (إن لم تكن مثبَّتة).</summary>
    private void SidebarDot_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CommandEntry entry) return;
        EntriesList.SelectedItem = entry;
        if (!_sidebarPinned) ShowSidebarFlyout(false);
        OpenTerminal(entry);
    }

    /// <summary>يصفّي قائمة الأوامر المحفوظة حسب نصّ البحث (الاسم/الباث/الأمر).</summary>
    private void SidebarSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(EntriesList.ItemsSource);
        if (view is null) return;
        string q = SidebarSearch.Text?.Trim() ?? "";
        view.Filter = q.Length == 0 ? null : o =>
            o is CommandEntry c &&
            ((c.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
             || (c.Path?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
             || (c.Command?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    // ===== لوحة الأوامر (Ctrl+Shift+P) =====

    private bool _commandPaletteOpen;
    private readonly List<CommandPaletteItem> _paletteSource = new();

    /// <summary>الأفعال الثابتة + الأوامر المحفوظة كمصدر موحّد قابل للتصفية.</summary>
    private void BuildPaletteSource()
    {
        _paletteSource.Clear();
        _paletteSource.Add(new CommandPaletteItem
        {
            Icon = "", Title = "تبويب جديد", Hint = "Ctrl+Shift+T",
            Invoke = OpenEmptyTerminal,
        });
        _paletteSource.Add(new CommandPaletteItem
        {
            Icon = "", Title = "انقسام عمودي (جنباً لجنب)", Hint = "Ctrl+Shift+D",
            Invoke = () => SplitActivePane(Orientation.Vertical),
        });
        _paletteSource.Add(new CommandPaletteItem
        {
            Icon = "", Title = "انقسام أفقي (فوق/تحت)", Hint = "Ctrl+Shift+E",
            Invoke = () => SplitActivePane(Orientation.Horizontal),
        });
        _paletteSource.Add(new CommandPaletteItem
        {
            Icon = "", Title = "إغلاق الجزء", Hint = "Ctrl+W",
            Invoke = CloseActivePane,
        });
        _paletteSource.Add(new CommandPaletteItem
        {
            Icon = "", Title = _settings.Mode == AppThemeMode.Dark ? "المظهر: فاتح" : "المظهر: داكن",
            Invoke = () => SetPreset(ThemeManager.DefaultFor(
                _settings.Mode == AppThemeMode.Dark ? AppThemeMode.Light : AppThemeMode.Dark)),
        });
        _paletteSource.Add(new CommandPaletteItem
        {
            Icon = "", Title = "الإعدادات",
            Invoke = () => ToggleSettings(true),
        });

        foreach (var entry in _entries)
        {
            var captured = entry;
            _paletteSource.Add(new CommandPaletteItem
            {
                Icon = "", Title = captured.Name, Hint = captured.Path,
                Invoke = () => OpenTerminal(captured),
            });
        }
    }

    private void OpenCommandPalette()
    {
        BuildPaletteSource();
        _commandPaletteOpen = true;
        CommandPaletteInput.Text = "";
        FilterPalette("");
        CommandPaletteOverlay.Visibility = Visibility.Visible;

        var anim = new DoubleAnimation
        {
            From = -24, To = 0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        CommandPaletteTransform.BeginAnimation(TranslateTransform.YProperty, anim);

        CommandPaletteInput.Focus();
    }

    private void CloseCommandPalette()
    {
        _commandPaletteOpen = false;
        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
    }

    private void CommandPaletteOverlay_MouseDown(object sender, MouseButtonEventArgs e) => CloseCommandPalette();
    private void CommandPaletteCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>تصفية Contains غير حسّاسة لحالة الأحرف على العنوان (والمسار للأوامر المحفوظة).</summary>
    private void FilterPalette(string term)
    {
        term = term.Trim();
        var filtered = new List<CommandPaletteItem>();
        foreach (var item in _paletteSource)
        {
            if (term.Length == 0
                || item.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.Hint.Contains(term, StringComparison.OrdinalIgnoreCase))
                filtered.Add(item);
        }
        CommandPaletteList.ItemsSource = filtered;
        if (filtered.Count > 0) CommandPaletteList.SelectedIndex = 0;
    }

    private void CommandPaletteInput_TextChanged(object sender, TextChangedEventArgs e)
        => FilterPalette(CommandPaletteInput.Text);

    private void CommandPaletteInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int count = CommandPaletteList.Items.Count;
        switch (e.Key)
        {
            case Key.Down:
                if (count > 0)
                    CommandPaletteList.SelectedIndex = (CommandPaletteList.SelectedIndex + 1) % count;
                CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                if (count > 0)
                    CommandPaletteList.SelectedIndex = (CommandPaletteList.SelectedIndex - 1 + count) % count;
                CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                InvokeSelectedPaletteItem();
                e.Handled = true;
                break;
            case Key.Escape:
                CloseCommandPalette();
                e.Handled = true;
                break;
        }
    }

    private void CommandPaletteList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => InvokeSelectedPaletteItem();

    private void InvokeSelectedPaletteItem()
    {
        var item = CommandPaletteList.SelectedItem as CommandPaletteItem;
        CloseCommandPalette();
        item?.Invoke();
    }

    // ===== حفظ/استرجاع الجلسة (التبويبات المفتوحة عبر التشغيلات) (T-109) =====

    /// <summary>يحفظ التبويبات المفتوحة الحاليّة كلقطة جلسة واحدة (يستبدل الأقدم لمنع النموّ).</summary>
    private void SaveSession()
    {
        try
        {
            var tabs = new List<TabSnapshot>();
            foreach (var item in TerminalTabs.Items)
                if (item is TabItem { Tag: CommandEntry entry } tab)
                {
                    // نلتقط جلسة الجزء النشط (معرّفها + آخر أمر) لاسترجاعها وإعادة تنفيذ آخر أمر.
                    var view = (tab.Content as TerminalPaneContainer)?.ActiveView;
                    tabs.Add(new TabSnapshot(HeaderTitle(tab) ?? entry.Name, entry.Shell, entry.Path,
                        view?.SessionId, view?.LastCommand));
                }

            // لقطة واحدة أحدث دائماً: نمسح ثم نحفظ (أو نمسح فقط إن لا تبويبات).
            _sessionStore.Clear();
            if (tabs.Count > 0)
                _sessionStore.Save(new SessionSnapshot(tabs));
        }
        catch
        {
            // أخطاء التخزين يجب ألّا تعطّل الإغلاق.
        }
    }

    /// <summary>يستعيد آخر جلسة محفوظة عند بدء التشغيل (تلقائيّ، بلا حوار)؛ لا يفعل شيئاً إن كانت تبويبات مفتوحة.</summary>
    private void RestoreSession()
    {
        try
        {
            if (TerminalTabs.HasItems) return;   // لا نكدّس فوق تبويبات موجودة
            var snapshot = _sessionStore.LoadLatest();
            if (snapshot is null || snapshot.Tabs.Count == 0) return;

            _restoring = true;
            foreach (var t in snapshot.Tabs)
            {
                // نُعيد الجلسة بمعرّفها نفسه (فيبقى تاريخها للأسهم) ونضع آخر أمر في Command كي يُعاد تنفيذه.
                OpenTerminal(new CommandEntry
                {
                    Name = t.Title,
                    Shell = t.ShellKey ?? ShellCatalog.DefaultKey,
                    Path = t.WorkingDirectory ?? "",
                    Command = t.LastCommand ?? "",
                }, t.SessionId);
            }
        }
        catch
        {
            // أخطاء التخزين يجب ألّا تعطّل بدء التشغيل.
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>يستخرج نصّ عنوان التبويب من رأسه المبنيّ (StackPanel ← TextBlock).</summary>
    private static string? HeaderTitle(TabItem tab)
        => tab.Header is StackPanel { Children.Count: > 0 } panel
           && panel.Children[0] is TextBlock tb ? tb.Text : null;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveSession();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var item in TerminalTabs.Items)
            if (item is TabItem { Content: TerminalPaneContainer container })
                foreach (var view in container.AllViews)
                    view.CloseSession();
        base.OnClosed(e);
    }
}
