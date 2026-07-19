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
using System.Windows.Threading;
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
    private readonly ProjectStore _projectStore = new();
    private readonly TagStore _tagStore = new();
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
    private bool _sidebarExpanded = true;   // الشريط الجانبيّ موسَّع (لوحة مشاريع) أم مطويّ (أيقونات)
    private bool _restoring;   // true أثناء استرجاع الجلسة كي لا يُثبِّت OpenTerminal لقطات وسطيّة
    private bool _syncingUi = true;   // يبقى true أثناء الإنشاء ليُتجاهَل ValueChanged/SelectionChanged المبكّر
    private string? _tagFilter;   // فلتر التاك النشط في لوحة المشاريع (null = الكل)
    private string? _colorPickerTarget;   // اسم التاك الجاري تغيير لونه في المنتقي
    private bool _colorPickerForNew;      // المنتقي مفتوح لاختيار لون تاك جديد (لا لتعديل قائم)
    private const int WindowCornerRadius = 10;
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

    // ألوان كتابة افتراضية جاهزة (#RRGGBB). "auto" = يتبع الثيم (فاتح على الداكن، داكن على الفاتح).
    private static readonly (string Name, string Hex)[] TextColorChoices =
    {
        ("تلقائيّ (حسب الثيم)", "auto"),
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

        // التاكات (تُحمَّل قبل المشاريع كي تُحلّ الألوان) ثمّ المشاريع.
        TagService.Initialize(_tagStore.Load());
        TagService.Changed += OnTagsChanged;
        ProjectService.Initialize(_projectStore.Load());
        ProjectService.Changed += OnProjectsChanged;

        foreach (var e in _store.Load()) _entries.Add(e);
        MigrateEntriesToProjects();      // ترحيل الأوامر القديمة إلى مشاريع (مرّة واحدة — V1)
        MigrateProjectColorsToTags();    // ترحيل لون كلّ مشروع إلى تاك (مرّة واحدة — V2)
        RefreshProjectsList();           // «لوحة المشاريع» + نقاط الشريط
        BuildTagFilterBar();             // شريط فلترة التاكات
        BuildThemeCards();
        BuildFontChoices();
        BuildTextColorSwatches();
        BuildBackgroundGallery();
        SyncSettingsUi();
        UpdateHint();
        EntriesList.ContextMenu = BuildProjectContextMenu();
        SetSidebarExpanded(_settings.SidebarExpanded);   // يعود الشريط كما تركه المستخدم
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

        // خطوط الواجهة (FontManager) — قيَم منفصلة عن خطّ التيرمنال أعلاه
        FontUiFamilyBox.Text = FontManager.Current.UiFont;
        FontMonoFamilyBox.Text = FontManager.Current.MonoFont;
        UiSizeSlider.Value = FontManager.Current.UiSize;
        UiSizeValue.Text = FontManager.Current.UiSize.ToString("0.#");
        MenuSizeSlider.Value = FontManager.Current.MenuSize;
        MenuSizeValue.Text = FontManager.Current.MenuSize.ToString("0.#");
        TableSizeSlider.Value = FontManager.Current.TableSize;
        TableSizeValue.Text = FontManager.Current.TableSize.ToString("0.#");
        RadiusSlider.Value = FontManager.Current.CornerRadius;
        RadiusValue.Text = FontManager.Current.CornerRadius.ToString("0.#");
        FontJsonPathText.Text = FontManager.ConfigPath;

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
            // الشريط يحمل أيقونات فقط (لا نصّاً دقيقاً)، فيكفيه ظلّ خفيف يفصله عن الخلفيّة بدل لوح
            // شبه معتِم — هذا ما يعطي الواجهة إحساس الطبقة الواحدة الهادئة.
            double a = Math.Clamp(Math.Max(0.28, _settings.BackgroundOpacity - 0.45), 0.15, 0.60);
            var c = ThemeManager.BackgroundColor;
            SidebarRail.Background = new SolidColorBrush(
                Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B));
        }
        else
        {
            SidebarRail.Background = Brushes.Transparent;   // خلفيّة الثيم تكفي — لا لوح فوق لوح
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
                BorderThickness = new Thickness(3),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = hex,
                ToolTip = name,
            };
            // «تلقائيّ» يعرض لون نصّ الثيم الحاليّ (يتتبّع الثيم عبر DynamicResource)؛ غيره لون ثابت.
            if (string.Equals(hex, "auto", StringComparison.OrdinalIgnoreCase))
                dot.SetResourceReference(Border.BackgroundProperty, "Brush.Text");
            else
                dot.Background = new SolidColorBrush(ParseColor(hex));
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

    // ===== خطوط الواجهة (FontManager) — تُحفَظ في fonts.json وتُطبَّق حيّاً على الموارد =====

    /// <summary>
    /// يعدّل إعدادات الخطّ ويطبّقه فوراً، ويؤجّل الكتابة للقرص قليلاً: سحب المنزلق يُطلق عشرات النبضات
    /// في الثانية، وكتابة <c>fonts.json</c> في كلّ نبضة تُثقل واجهة المستخدم بلا داعٍ.
    /// </summary>
    private void CommitFont(Action<FontSettings> set)
    {
        if (_syncingUi) return;
        set(FontManager.Current);
        FontManager.Apply();
        _fontSaveDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _fontSaveDebounce.Tick -= FontSaveTick;
        _fontSaveDebounce.Tick += FontSaveTick;
        _fontSaveDebounce.Stop();
        _fontSaveDebounce.Start();
    }

    private DispatcherTimer? _fontSaveDebounce;

    private void FontSaveTick(object? sender, EventArgs e)
    {
        _fontSaveDebounce?.Stop();
        FontManager.Save();
    }

    private void UiSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (UiSizeValue != null) UiSizeValue.Text = e.NewValue.ToString("0.#");
        CommitFont(s => s.UiSize = e.NewValue);
    }

    private void MenuSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MenuSizeValue != null) MenuSizeValue.Text = e.NewValue.ToString("0.#");
        CommitFont(s => s.MenuSize = e.NewValue);
    }

    private void TableSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TableSizeValue != null) TableSizeValue.Text = e.NewValue.ToString("0.#");
        CommitFont(s => s.TableSize = e.NewValue);
    }

    private void RadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RadiusValue != null) RadiusValue.Text = e.NewValue.ToString("0.#");
        CommitFont(s => s.CornerRadius = e.NewValue);
    }

    private void FontUiFamily_LostFocus(object sender, RoutedEventArgs e)
        => CommitFont(s => s.UiFont = FontUiFamilyBox.Text?.Trim() ?? "");
    private void FontUiFamily_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { CommitFont(s => s.UiFont = FontUiFamilyBox.Text?.Trim() ?? ""); e.Handled = true; } }

    private void FontMonoFamily_LostFocus(object sender, RoutedEventArgs e)
        => CommitFont(s => s.MonoFont = FontMonoFamilyBox.Text?.Trim() ?? "");
    private void FontMonoFamily_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { CommitFont(s => s.MonoFont = FontMonoFamilyBox.Text?.Trim() ?? ""); e.Handled = true; } }

    /// <summary>يفتح محرّر الإعدادات الذكيّ المدمج (تلوين + تنسيق + تحقّق حيّ) على config.json.</summary>
    private void OpenConfigEditor_Click(object sender, RoutedEventArgs e)
    {
        FontManager.Save();   // اكتب الحالة الراهنة قبل الفتح كي يرى المحرّر أحدث القيم
        var win = new Views.ConfigEditorWindow { Owner = this };
        win.ShowDialog();
        // المحرّر يطبّق ويعيد التحميل عند الحفظ؛ نزامن اللوحة لتعكس أيّ تغيير.
        SyncSettingsUi();
    }

    /// <summary>يعيد قراءة config.json من القرص ويطبّقه (بعد تعديل يدويّ) ثمّ يزامن اللوحة.</summary>
    private void ApplyFontJson_Click(object sender, RoutedEventArgs e)
    {
        FontManager.ReloadAndApply();
        SyncSettingsUi();
    }

    private void ResetFont_Click(object sender, RoutedEventArgs e)
    {
        FontManager.ResetToDefaults();
        SyncSettingsUi();
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

    /// <summary>يضبط لون الكتابة الافتراضي في لوحة ANSI: اختيار المستخدم الصريح، وإلّا لون يتباين مع خلفيّة الثيم.</summary>
    private void ApplyDefaultForeground() => AnsiPalette.DefaultForeground = ThemeManager.ResolveTerminalForeground(_settings);

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
        ThemeManager.Apply(_settings);   // يحدّث خلفيّة/أساس/لون كتابة لوحة ANSI حسب وضع الثيم
        UpdateThemeSelection();
        UpdateTextColorSelection();       // مؤشّر لون الكتابة يتبع الثيم في الوضع التلقائيّ
        ApplyFontToAllTabs();             // إعادة رسم التيرمنالات بلون/أساس الثيم الجديد فوراً
        ApplyBackground();                // يحدّث طبقات التعتيم (الشريط الجانبيّ/الرأس) بألوان الثيم الجديد
        BuildBackgroundGallery();         // مصغّرات النقوش تتبع الثيم — نعيد بناءها كي لا تبقى بألوانه القديمة
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
        UpdateTextColorSelection();
        ApplyFontToAllTabs();             // إعادة رسم التيرمنالات بلون/أساس الثيم الجديد فوراً
        ApplyBackground();                // يحدّث طبقات التعتيم بألوان الثيم الجديد
        BuildBackgroundGallery();
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
            menu.FlowDirection = Loc.Flow;   // الأيقونة تتبع اتّجاه الواجهة (يمين في العربيّة)
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
        // نافذة مستقلّة (بلا Owner) كي لا تبقى دائماً فوق النافذة الرئيسة — يتحكّم المستخدم بترتيبها.
        _serverMonitorWindow = new Views.ServerMonitorWindow(_serverProfiles);
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
        SidebarHeader.Text = Loc.T("sidebar.projects");
        SidebarMenuBtn.ToolTip = Loc.T("sidebar.projects");
        RunBtn.Content = Loc.T("proj.open");
        HintLine1.Text = Loc.T("hint.title");
        HintLine2.Text = Loc.T("hint.empty");
        HintLine3.Text = Loc.T("hint.palette");
        HintLine4.Text = Loc.T("hint.splitV");
        HintLine5.Text = Loc.T("hint.splitH");
        SidebarSettingsCaption.Text  = Loc.T("settings.title");
        SidebarSettingsText.Text     = Loc.T("settings.title");
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

        // لوحة المشاريع + الدوك
        SidebarSearch.Tag = Loc.T("proj.search");
        HeaderSearch.Tag  = Loc.T("hdr.search");
        QuickNewCommand.Content = Loc.T("proj.cmd.new");
        QuickAddCurrent.Content = Loc.T("quick.addCurrent");
        QuickCmdEmpty.Text = Loc.T("quick.empty");
        QuickEditProject.ToolTip = Loc.T("proj.edit");
        ProjEditNameLabel.Text = Loc.T("proj.field.name");
        ProjEditTagsLabel.Text = Loc.T("proj.field.tags");
        ProjEditFolderLabel.Text = Loc.T("proj.field.folder");
        ProjEditShellLabel.Text = Loc.T("proj.field.shell");
        ProjEditCancelBtn.Content = Loc.T("editor.cancel");
        ProjEditSaveBtn.Content = Loc.T("editor.save");
        if (_quickProject != null && QuickDock.Visibility == Visibility.Visible) BuildQuickDock();
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

    private Project? Selected => EntriesList.SelectedItem as Project;

    /// <summary>يعيد بناء «لوحة المشاريع» (قائمة المشاريع + نقاط الشريط) مصفّاةً بنصّ البحث.</summary>
    private void RefreshProjectsList()
    {
        string q = SidebarSearch.Text?.Trim() ?? "";
        var items = ProjectService.All.AsEnumerable();
        if (_tagFilter != null)
            items = items.Where(p => p.HasTag(_tagFilter));
        if (q.Length > 0)
            items = items.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                     || (p.Folder?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        EntriesList.ItemsSource = items.ToList();
        SidebarDots.ItemsSource = ProjectService.All.ToList();   // نقاط الشريط = المشاريع
    }

    /// <summary>
    /// ترحيل لمرّة واحدة: الأوامر المحفوظة القديمة (CommandEntry الموسومة) → أوامر داخل مشاريعها.
    /// يستنتج فولدر المشروع من أكثر مسارات أوامره شيوعاً، ويصفّر تجاوز الأوامر المطابقة له.
    /// </summary>
    private void MigrateEntriesToProjects()
    {
        if (_settings.ProjectsMigratedV1) return;
        const string general = "عام";
        foreach (var e in _entries)
        {
            var steps = ProjectService.SplitSteps(e.Command);
            if (steps.Count == 0) continue;
            string tag = string.IsNullOrWhiteSpace(e.PrimaryTag) ? general : e.PrimaryTag!;
            var proj = ProjectService.GetOrCreateSilent(tag);
            string key = string.Join("\n", steps);
            if (proj.Commands.Any(c => c.DedupKey == key)) continue;
            proj.Commands.Add(new ProjectCommand
            {
                Label = e.Name ?? "",
                Steps = steps,
                Folder = string.IsNullOrWhiteSpace(e.Path) ? null : e.Path,
            });
            if (string.IsNullOrWhiteSpace(proj.Shell) && !string.IsNullOrWhiteSpace(e.Shell))
                proj.Shell = e.Shell;
        }
        foreach (var proj in ProjectService.All)
        {
            if (proj.Commands.Count == 0) continue;
            if (string.IsNullOrWhiteSpace(proj.Folder))
            {
                var top = proj.Commands.Where(c => !string.IsNullOrWhiteSpace(c.Folder))
                    .GroupBy(c => c.Folder!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count()).FirstOrDefault();
                if (top != null) proj.Folder = top.Key;
            }
            foreach (var c in proj.Commands)
                if (!string.IsNullOrWhiteSpace(c.Folder) && string.Equals(c.Folder, proj.Folder, StringComparison.OrdinalIgnoreCase))
                    c.Folder = null;
        }
        _settings.ProjectsMigratedV1 = true;
        _projectStore.Save(ProjectService.All);
        SaveSettings();
    }

    /// <summary>ترحيل V2 لمرّة واحدة: لون كلّ مشروع قديم → تاك بنفس الاسم واللون، مُسنَد للمشروع.</summary>
    private void MigrateProjectColorsToTags()
    {
        if (_settings.ProjectsTagMigratedV2) return;
        foreach (var p in ProjectService.All)
        {
            if (p.Tags.Count > 0) continue;                    // له تاكات أصلاً
            if (string.IsNullOrWhiteSpace(p.Color)) continue;  // بلا لون قديم
            TagService.GetOrCreateSilent(p.Name, p.Color);     // تاك بنفس اسم/لون المشروع
            p.Tags = new List<string> { p.Name };
            p.Color = "";                                      // إزالة اللون المهجور
        }
        _settings.ProjectsTagMigratedV2 = true;
        _tagStore.Save(TagService.All);
        _projectStore.Save(ProjectService.All);
        SaveSettings();
    }

    /// <summary>يُحدَّث عند تغيّر التاكات (إضافة/تلوين/حذف): يحفظ ويعيد رسم الألوان والشريط.</summary>
    private void OnTagsChanged()
    {
        _tagStore.Save(TagService.All);
        RefreshProjectsList();
        BuildTagFilterBar();
        if (_quickProject != null && QuickDock.Visibility == Visibility.Visible) BuildQuickDock();
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } proj) OpenQuickDock(proj.Name);
        else ShowAlert("تنبيه", "اختر مشروعاً من القائمة أولاً.");
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } proj) OpenQuickDock(proj.Name);   // اللوحة دائمة — لا تُطوى عند الفتح
    }

    // يحدّد العنصر تحت المؤشّر قبل ظهور القائمة السياقية.
    private void EntriesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EntriesList, (DependencyObject)e.OriginalSource) is ListBoxItem item)
            item.IsSelected = true;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => OpenProjectEditor(null);

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } proj) OpenProjectEditor(proj);
        else ShowAlert("تنبيه", "اختر مشروعاً لتعديله.");
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } proj) return;
        int n = proj.Commands.Count;
        string msg = n > 0
            ? string.Format(Loc.T("dlg.proj.msgN"), proj.Name, n)
            : string.Format(Loc.T("dlg.proj.msg"), proj.Name);
        ShowConfirm(Loc.T("dlg.proj.title"), msg,
            new[] { (Loc.T("dlg.cancel"), "cancel", Views.DialogButtonKind.Neutral), (Loc.T("dlg.delete"), "delete", Views.DialogButtonKind.Danger) },
            key =>
            {
                if (key != "delete") return;
                if (string.Equals(_quickProject, proj.Name, StringComparison.OrdinalIgnoreCase)) CloseQuickDock();
                ProjectService.Remove(proj.Name);   // يُطلِق OnProjectsChanged → حفظ + إعادة رسم
                RefreshProjectsList();
            });
    }

    // ===== رقاقات التاكات (مشتركة: محرّر المشروع) =====

    /// <summary>رقاقة تاك (نقطة لون + اسم). في وضع التبديل تعكس/تضبط إسناد التاك للمشروع قيد التحرير.</summary>
    private Border MakeTagChip(string name, string colorHex, bool active, bool toggle)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(colorHex); } catch { color = Colors.Gray; }

        var dot = new Border
        {
            Width = 9, Height = 9, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(dot);
        content.Children.Add(text);

        var chip = new Border
        {
            Child = content,
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = name,
        };
        ApplyChipActiveLook(chip, active, color);

        if (toggle)
            chip.MouseLeftButtonUp += ProjEditTagChip_Click;
        return chip;
    }

    /// <summary>يضبط مظهر الرقاقة حسب حالتها (نشطة = خلفيّة/إطار بلون المشروع، خاملة = سطح محايد).</summary>
    private void ApplyChipActiveLook(Border chip, bool active, Color color)
    {
        if (active)
        {
            chip.Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
            chip.BorderBrush = new SolidColorBrush(color);
        }
        else
        {
            chip.Background = (Brush)FindResource("Brush.Surface2");
            chip.BorderBrush = (Brush)FindResource("Brush.Border");
        }
    }

    private void EditorCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // ===== فلترة التاكات في لوحة المشاريع =====

    /// <summary>يبني شريط فلترة التاكات: «الكل» + رقاقة لكلّ تاك. يُخفى إن لا تاكات.</summary>
    private void BuildTagFilterBar()
    {
        ProjectFilterBar.Children.Clear();
        var tags = TagService.All;
        if (tags.Count == 0)
        {
            ProjectFilterScroll.Visibility = Visibility.Collapsed;
            _tagFilter = null;
            return;
        }

        ProjectFilterBar.Children.Add(MakeFilterChip(null, Loc.T("proj.filter.all"), null, _tagFilter is null));
        foreach (var t in tags)
        {
            Color color;
            try { color = (Color)ColorConverter.ConvertFromString(t.Color); } catch { color = Colors.Gray; }
            bool active = string.Equals(_tagFilter, t.Name, StringComparison.OrdinalIgnoreCase);
            ProjectFilterBar.Children.Add(MakeFilterChip(t.Name, t.Name, color, active));
        }
        ProjectFilterScroll.Visibility = Visibility.Visible;
    }

    /// <summary>رقاقة فلتر تاك واحدة (اسم التاك في Tag؛ null = «الكل»).</summary>
    private Border MakeFilterChip(string? name, string label, Color? color, bool active)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (color is { } c)
            content.Children.Add(new Border
            {
                Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        // لون النصّ صريح لا موروث: الرقاقة تُلوَّن بلون التاك، فالوراثة قد تعطي نصّاً بلون الخلفيّة نفسها.
        content.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11.5,
            Foreground = (Brush)FindResource("Brush.Text"),
        });

        var chip = new Border
        {
            Child = content,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = name,
        };
        ApplyChipActiveLook(chip, active, color ?? ((SolidColorBrush)FindResource("Brush.Accent")).Color);
        chip.MouseLeftButtonUp += ProjectFilterChip_Click;

        // رقاقة تاك فعليّ (لا «الكل») تحمل قائمة سياق (تغيير اللون + حذف التاك).
        if (name != null)
        {
            var menu = new ContextMenu();
            var recolor = new MenuItem { Header = "🎨  تغيير اللون" };
            recolor.Click += (_, _) => OpenColorPicker(name);
            var del = new MenuItem { Header = $"🗑  حذف التاك «{name}»" };
            del.Click += (_, _) => DeleteTag(name);
            menu.Items.Add(recolor);
            menu.Items.Add(del);
            chip.ContextMenu = menu;
        }
        return chip;
    }

    /// <summary>يحذف تاكاً بعد تأكيد: يفكّ ربطه من كلّ المشاريع ثمّ يزيله.</summary>
    private void DeleteTag(string name)
    {
        int linked = ProjectService.All.Count(p => p.HasTag(name));
        string msg = linked > 0
            ? string.Format(Loc.T("dlg.tag.msgN"), name, linked)
            : string.Format(Loc.T("dlg.tag.msg"), name);
        ShowConfirm(Loc.T("dlg.tag.title"), msg,
            new[] { (Loc.T("dlg.cancel"), "cancel", Views.DialogButtonKind.Neutral), (Loc.T("dlg.delete"), "delete", Views.DialogButtonKind.Danger) },
            key =>
            {
                if (key != "delete") return;
                if (string.Equals(_tagFilter, name, StringComparison.OrdinalIgnoreCase)) _tagFilter = null;
                ProjectService.RemoveTagFromAll(name);   // يُطلِق OnProjectsChanged
                TagService.Remove(name);                 // يُطلِق OnTagsChanged (حفظ + إعادة رسم الشريط)
            });
    }

    // ===== حوار تأكيد مخصّص (بديل MessageBox) =====

    /// <summary>
    /// يعرض حوار تأكيد مخصّصاً (<see cref="Views.AppDialog"/>) بأزرار ديناميكيّة، ويستدعي <paramref name="onResult"/>
    /// بمفتاح الزرّ المختار. الإلغاء (Escape/زرّ إلغاء) لا يستدعي شيئاً — على المستدعي تضمين مفتاح «cancel».
    /// </summary>
    private void ShowConfirm(string title, string message,
        (string Label, string Key, Views.DialogButtonKind Kind)[] options, Action<string> onResult)
    {
        var key = Views.AppDialog.Confirm(this, title, message, options);
        if (key != null) onResult(key);
    }

    /// <summary>تنبيه مخصّص بزرّ واحد (بديل MessageBox ذي OK) — يُستعمل عبر الأداة.</summary>
    private void ShowAlert(string title, string message) => Views.AppDialog.Alert(this, title, message);

    // ===== منتقي لون التاك =====

    /// <summary>يفتح منتقي اللون لتاك قائم: يبني رقاقات اللوحة، يملأ اللون الحاليّ، ويُبقي اللوحة مفتوحة.</summary>
    private void OpenColorPicker(string name)
    {
        _colorPickerForNew = false;
        _colorPickerTarget = name;
        if (!_sidebarExpanded) SetSidebarExpanded(true);   // المنتقي يعمل على اللوحة — نوسّعها أوّلاً

        ColorPickerTitle.Text = $"لون التاك «{name}»";
        ShowColorPicker(TagService.Find(name)?.Color ?? "");
    }

    /// <summary>يفتح المنتقي لاختيار لون تاك جديد (وضع الإنشاء داخل محرّر المشروع).</summary>
    private void OpenColorPickerForNew()
    {
        _colorPickerForNew = true;
        _colorPickerTarget = null;
        ColorPickerTitle.Text = "لون التاك الجديد";
        ShowColorPicker(_projNewTagColor);
    }

    /// <summary>يملأ رقاقات اللوحة (مع إبراز اللون الحاليّ) ويفتح المنبثقة.</summary>
    private void ShowColorPicker(string current)
    {
        ColorHexInput.Text = current;
        ColorSwatchPanel.Children.Clear();
        foreach (var hex in TagService.Palette)
            ColorSwatchPanel.Children.Add(MakeColorSwatch(hex, string.Equals(hex, current, StringComparison.OrdinalIgnoreCase)));
        ColorPickerPopup.IsOpen = true;
    }

    /// <summary>مربّع لون في المنتقي — نقره يُطبّق اللون على المشروع الهدف.</summary>
    private Border MakeColorSwatch(string hex, bool selected)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(hex); } catch { color = Colors.Gray; }
        var swatch = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 6, 6),
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(selected ? 3 : 1),
            BorderBrush = selected ? (Brush)FindResource("Brush.Text") : (Brush)FindResource("Brush.Border"),
            Cursor = Cursors.Hand,
            Tag = hex,
            ToolTip = hex,
        };
        swatch.MouseLeftButtonUp += (s, _) =>
        {
            if ((s as Border)?.Tag is string h) ApplyProjectColor(h);
        };
        return swatch;
    }

    private void ColorHexApply_Click(object sender, RoutedEventArgs e) => ApplyHexFromInput();

    private void ColorHexInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyHexFromInput(); e.Handled = true; }
    }

    /// <summary>يطبّق لوناً مخصّصاً من حقل الإدخال (يتحقّق من صلاحيّته أوّلاً).</summary>
    private void ApplyHexFromInput()
    {
        string hex = ColorHexInput.Text.Trim();
        if (hex.Length == 0) return;
        if (hex[0] != '#') hex = "#" + hex;
        try { _ = (Color)ColorConverter.ConvertFromString(hex); }
        catch
        {
            ShowAlert("تنبيه", "لون غير صالح. استعمل الصيغة #RRGGBB.");
            return;
        }
        ApplyProjectColor(hex);
    }

    /// <summary>يطبّق اللون المختار: لتاك جديد (يحدّث المعاينة) أو لتاك قائم (يحفظ ويعيد الرسم).</summary>
    private void ApplyProjectColor(string hex)
    {
        if (_colorPickerForNew)
        {
            _projNewTagColor = hex;
            UpdateNewTagColorDot();
            ColorPickerPopup.IsOpen = false;
            return;
        }
        if (_colorPickerTarget is not { } name) return;
        TagService.SetColor(name, hex);   // يُطلِق OnTagsChanged (حفظ + إعادة رسم)
        ColorPickerPopup.IsOpen = false;
    }

    private void ColorPicker_Closed(object sender, EventArgs e)
    {
        _colorPickerTarget = null;
        _colorPickerForNew = false;
    }

    /// <summary>نقر رقاقة فلتر التاك: يبدّل الفلتر ويعيد بناء قائمة المشاريع.</summary>
    private void ProjectFilterChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border chip) return;
        string? name = chip.Tag as string;
        _tagFilter = string.Equals(_tagFilter, name, StringComparison.OrdinalIgnoreCase) ? null : name;
        BuildTagFilterBar();
        RefreshProjectsList();
    }

    // ===== لوحة أوامر المشروع السريعة: تطفو على حافة التيرمنال، تُنفَّذ بالنقر في التيرمنال النشط =====

    private string? _quickProject;                       // المشروع المعروض في اللوحة (null = مطويّة)
    private readonly TranslateTransform _quickDockTransform = new();

    /// <summary>يفتح اللوحة على مشروع (أو يطويها إن null/مجهول)، يبنيها ويُظهرها بانزلاق.</summary>
    private void OpenQuickDock(string? projectName)
    {
        if (ProjectService.Find(projectName) is not { } proj) { CloseQuickDock(); return; }
        _quickProject = proj.Name;
        // لم تعد الحصريّة لازمة: لوحة المشاريع مرسوّة في عمودها والدوك يطفو فوق التيرمنال — لا تداخل.
        BuildQuickDock();
        ShowQuickDock(true);
    }

    private void CloseQuickDock()
    {
        _quickProject = null;
        ShowQuickDock(false);
    }

    private void QuickDockClose_Click(object sender, RoutedEventArgs e) => CloseQuickDock();

    /// <summary>
    /// النقر داخل منطقة التيرمنال يُغلق دوك الأوامر الطافي كي يتفرّغ العرض. لوحة المشاريع الجانبيّة
    /// دائمة فلا تتأثّر. لا نُعلّم الحدث مُعالَجاً فيصل النقر للتيرمنال (تركيز/تحديد) طبيعيّاً.
    /// </summary>
    private void TerminalTabs_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (QuickDock.Visibility == Visibility.Visible) CloseQuickDock();
    }

    /// <summary>إظهار/إخفاء اللوحة بانزلاق أفقيّ خفيف + تلاشٍ (كلوحة الأوامر الجانبيّة).</summary>
    private void ShowQuickDock(bool show)
    {
        QuickDock.RenderTransform = _quickDockTransform;
        if (show)
        {
            if (QuickDock.Visibility == Visibility.Visible) return;
            QuickDock.Visibility = Visibility.Visible;
            var dur = TimeSpan.FromMilliseconds(160);
            QuickDock.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
            _quickDockTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(18, 0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            if (QuickDock.Visibility != Visibility.Visible) return;
            var dur = TimeSpan.FromMilliseconds(140);
            var fade = new DoubleAnimation(1, 0, dur);
            fade.Completed += (_, _) => { if (QuickDock.Opacity == 0) QuickDock.Visibility = Visibility.Collapsed; };
            QuickDock.BeginAnimation(OpacityProperty, fade);
            _quickDockTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, 18, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        }
    }

    private ProjectCommand? _editingCmd;   // أمر قيد التحرير المباشر في الدوك (null = لا شيء)
    private bool _addingCmd;                // إضافة أمر جديد (محرّر فارغ بأعلى القائمة)
    private static readonly FontFamily Mdl2 = new("Segoe MDL2 Assets");

    /// <summary>يبني محتوى الدوك للمشروع النشط: الرأس (اسم/فولدر)، رقاقات التبديل، والأوامر (+ محرّر مباشر).</summary>
    private void BuildQuickDock()
    {
        if (ProjectService.Find(_quickProject) is not { } proj) { CloseQuickDock(); return; }
        var color = ProjectService.ColorOf(proj) ?? ((SolidColorBrush)FindResource("Brush.Accent")).Color;
        QuickDockDot.Background = new SolidColorBrush(color);
        QuickDockTitle.Text = proj.Name;
        QuickDockPath.Text = string.IsNullOrWhiteSpace(proj.Folder) ? Loc.T("proj.noFolder") : proj.Folder;

        // رقاقات تبديل المشاريع
        QuickDockChips.Children.Clear();
        foreach (var p in ProjectService.All)
        {
            var c = ProjectService.ColorOf(p) ?? color;
            bool active = string.Equals(p.Name, proj.Name, StringComparison.OrdinalIgnoreCase);
            QuickDockChips.Children.Add(MakeQuickChip(p.Name, c, active));
        }

        // الأوامر (مع محرّر مباشر بدل الصفّ قيد التحرير، أو بأعلى القائمة عند الإضافة)
        QuickCmdPanel.Children.Clear();
        if (_addingCmd) QuickCmdPanel.Children.Add(BuildCommandEditor(null, proj, color));
        foreach (var cmd in proj.Commands)
            QuickCmdPanel.Children.Add(_editingCmd == cmd
                ? BuildCommandEditor(cmd, proj, color)
                : MakeCommandRow(cmd, color, proj));
        QuickCmdEmpty.Visibility = (proj.Commands.Count == 0 && !_addingCmd) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>رقاقة تبديل مشروع داخل الدوك (تعيد بناءه على المشروع المنقور).</summary>
    private Border MakeQuickChip(string name, Color color, bool active)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Border
        {
            Width = 7, Height = 7, CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var chipLabel = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        chipLabel.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        content.Children.Add(chipLabel);
        var chip = new Border
        {
            Child = content,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 5, 5),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = name,
        };
        ApplyChipActiveLook(chip, active, color);
        chip.MouseLeftButtonUp += (_, _) => OpenQuickDock(name);
        return chip;
    }

    /// <summary>صفّ أمر: أيقونة تشغيل + الاسم + شارة عدد الخطوات + تعديل/حذف؛ النقر ينفّذه (تنفيذ ذكيّ).</summary>
    private Border MakeCommandRow(ProjectCommand cmd, Color color, Project proj)
    {
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var play = new TextBlock
        {
            Text = "", FontFamily = Mdl2, Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0),
        };
        var label = new TextBlock
        {
            Text = cmd.Display, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("Brush.Text"),
        };
        Grid.SetColumn(label, 1);
        play.SetResourceReference(TextBlock.FontSizeProperty, "Size.Ui");
        label.SetResourceReference(TextBlock.FontSizeProperty, "Size.Ui");
        top.Children.Add(play);
        top.Children.Add(label);

        if (cmd.IsMultiStep)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 0, 6, 1),
                Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{cmd.Steps.Count} {Loc.T("proj.steps")}", FontSize = 9,
                    Foreground = new SolidColorBrush(color),
                },
            };
            Grid.SetColumn(badge, 2);
            top.Children.Add(badge);
        }

        var edit = new TextBlock
        {
            Text = "", FontFamily = Mdl2, FontSize = 11, Foreground = (Brush)FindResource("Brush.TextMuted"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0),
            Cursor = Cursors.Hand, Opacity = 0, ToolTip = Loc.T("proj.cmd.edit"),
        };
        Grid.SetColumn(edit, 3);
        edit.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; _addingCmd = false; _editingCmd = cmd; BuildQuickDock(); };

        var del = new TextBlock
        {
            Text = "", FontFamily = Mdl2, FontSize = 11, Foreground = (Brush)FindResource("Brush.TextMuted"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand, Opacity = 0, ToolTip = Loc.T("quick.remove"),
        };
        Grid.SetColumn(del, 4);
        del.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; ProjectService.RemoveCommand(proj.Name, cmd); };
        top.Children.Add(edit);
        top.Children.Add(del);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(new TextBlock
        {
            Text = string.Join("   ↵   ", cmd.Steps), FontSize = 9.5,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FlowDirection = FlowDirection.LeftToRight, Foreground = (Brush)FindResource("Brush.TextMuted"),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0),
        });

        var row = new Border
        {
            Child = stack,
            Background = (Brush)FindResource("Brush.Surface"),
            BorderBrush = (Brush)FindResource("Brush.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 5),
            Cursor = Cursors.Hand,
        };
        row.MouseEnter += (_, _) => { row.BorderBrush = new SolidColorBrush(color); edit.Opacity = 1; del.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.BorderBrush = (Brush)FindResource("Brush.Border"); edit.Opacity = 0; del.Opacity = 0; };
        row.MouseLeftButtonUp += (_, _) => RunProjectCommand(proj, cmd);
        return row;
    }

    /// <summary>محرّر أمر مباشر داخل الدوك: اسم + خطوات (سطر لكلّ خطوة) + فولدر اختياريّ + حفظ/إلغاء.</summary>
    private Border BuildCommandEditor(ProjectCommand? cmd, Project proj, Color color)
    {
        var panel = new StackPanel();
        var labelBox = new TextBox
        {
            Text = cmd?.Label ?? "", FontSize = 11, Margin = new Thickness(0, 0, 0, 6),
            Tag = Loc.T("proj.cmd.labelHint"), VerticalContentAlignment = VerticalAlignment.Center,
        };
        var stepsBox = new TextBox
        {
            Text = cmd?.StepsText ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            MinHeight = 50, FontSize = 10.5, FlowDirection = FlowDirection.LeftToRight,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Tag = Loc.T("proj.cmd.stepsHint"),
        };
        var folderGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var folderBox = new TextBox
        {
            Text = cmd?.Folder ?? "", FontSize = 10.5, FlowDirection = FlowDirection.LeftToRight,
            VerticalContentAlignment = VerticalAlignment.Center, Tag = Loc.T("proj.cmd.folderHint"),
        };
        var browse = new Button
        {
            Content = "", FontFamily = Mdl2, Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(9, 4, 9, 4), ToolTip = Loc.T("proj.folder.browse"),
        };
        Grid.SetColumn(browse, 1);
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog();
            if (!string.IsNullOrWhiteSpace(folderBox.Text)) { try { dlg.InitialDirectory = folderBox.Text; } catch { } }
            if (dlg.ShowDialog() == true) folderBox.Text = dlg.FolderName;
        };
        folderGrid.Children.Add(folderBox);
        folderGrid.Children.Add(browse);

        var buttons = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        var save = new Button
        {
            Content = Loc.T("editor.save"), Style = (Style)FindResource("AccentButton"),
            Padding = new Thickness(14, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button
        {
            Content = Loc.T("editor.cancel"), Padding = new Thickness(12, 5, 12, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        panel.Children.Add(labelBox);
        panel.Children.Add(stepsBox);
        panel.Children.Add(folderGrid);
        panel.Children.Add(buttons);

        cancel.Click += (_, _) => { _addingCmd = false; _editingCmd = null; BuildQuickDock(); };
        save.Click += (_, _) =>
        {
            string steps = stepsBox.Text, lbl = labelBox.Text, folder = folderBox.Text;
            _addingCmd = false; _editingCmd = null;
            if (cmd == null)
            {
                bool ok = ProjectService.AddCommand(proj.Name, steps, lbl, folder);   // Changed → إعادة بناء
                if (!ok) { BuildQuickDock(); NotificationService.Secondary(Loc.T("quick.duplicate"), NotificationType.Info); }
            }
            else ProjectService.UpdateCommand(cmd, lbl, steps, folder);
        };

        var box = new Border
        {
            Child = panel,
            Background = (Brush)FindResource("Brush.Bg"),
            BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
        };
        labelBox.Loaded += (_, _) => labelBox.Focus();
        return box;
    }

    /// <summary>
    /// تنفيذ ذكيّ لأمر مشروع: إن كان التيرمنال النشط في فولدر الأمر نفسه ينفّذ خطواته فيه؛ وإلّا يفتح
    /// تيرمنالاً جديداً في الفولدر (بصدفة المشروع) ويشغّلها. الخطوات تُنفَّذ بالتوالي.
    /// </summary>
    private void RunProjectCommand(Project proj, ProjectCommand cmd)
    {
        if (cmd.Steps.Count == 0) return;
        string folder = !string.IsNullOrWhiteSpace(cmd.Folder) ? cmd.Folder! : proj.Folder;
        string shell = !string.IsNullOrWhiteSpace(proj.Shell) ? proj.Shell : ShellCatalog.DefaultKey;

        var activeEntry = (TerminalTabs.SelectedItem as TabItem)?.Tag as CommandEntry;
        var activeView = ActiveContainer?.ActiveView;
        bool sameFolder = activeView != null && activeEntry != null && PathEquals(activeEntry.Path, folder);

        if (sameFolder)
            foreach (var step in cmd.Steps) activeView!.RunCommand(step);
        else
            OpenTerminal(new CommandEntry
            {
                Name = string.IsNullOrWhiteSpace(cmd.Label) ? proj.Name : cmd.Label,
                Path = folder,
                Shell = shell,
                Command = string.Join("\n", cmd.Steps),   // OnCoreData يقسّمها وينفّذها بالتوالي
            });
    }

    /// <summary>مقارنة مسارين بعد التطبيع (فواصل موحّدة، بلا فراغ/شرطة نهائيّة، غير حسّاسة للحالة).</summary>
    private static bool PathEquals(string? a, string? b)
    {
        static string N(string? s) => (s ?? "").Trim().Replace('/', '\\').TrimEnd('\\');
        return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>«أمر جديد»: يفتح محرّراً فارغاً بأعلى قائمة الأوامر.</summary>
    private void QuickNewCommand_Click(object sender, RoutedEventArgs e)
    {
        if (_quickProject is null) return;
        _editingCmd = null; _addingCmd = true;
        BuildQuickDock();
    }

    /// <summary>«أضف الأمر الحاليّ»: يلتقط آخر أمر نُفِّذ في التيرمنال النشط ويضيفه للمشروع بعد فحص التكرار.</summary>
    private void QuickAddCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_quickProject is null) return;
        var last = ActiveContainer?.ActiveView?.LastCommand;
        if (string.IsNullOrWhiteSpace(last))
        {
            NotificationService.Secondary(Loc.T("quick.noCurrent"), NotificationType.Warning);
            return;
        }
        bool added = ProjectService.AddCommand(_quickProject, last);   // Changed → حفظ + إعادة بناء
        NotificationService.Secondary(
            added ? Loc.T("quick.added") : Loc.T("quick.duplicate"),
            added ? NotificationType.Success : NotificationType.Info);
    }

    /// <summary>يفتح محرّر المشروع النشط (اسم/لون/فولدر/صدفة).</summary>
    private void QuickEditProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectService.Find(_quickProject) is { } proj) OpenProjectEditor(proj);
    }

    // ===== محرّر المشروع (اسم/باث/صدفة/تاكات) =====

    private Project? _projEditTarget;
    private readonly List<string> _projEditTags = new();   // تاكات المشروع قيد التحرير
    private string _projNewTagColor = "#C96442";           // لون التاك الجديد المختار

    /// <summary>يفتح محرّر المشروع (null = مشروع جديد).</summary>
    private void OpenProjectEditor(Project? proj)
    {
        _projEditTarget = proj;
        ProjEditTitle.Text = proj is null ? Loc.T("proj.new") : Loc.T("proj.edit");
        ProjEditName.Text = proj?.Name ?? "";
        ProjEditFolder.Text = proj?.Folder ?? "";
        ProjEditShell.ItemsSource = ShellCatalog.All;
        ProjEditShell.SelectedItem = ShellCatalog.Get(string.IsNullOrWhiteSpace(proj?.Shell) ? null : proj!.Shell);
        _projEditTags.Clear();
        if (proj != null) _projEditTags.AddRange(proj.Tags);
        ProjEditNewTag.Text = "";
        _projNewTagColor = TagService.NextAutoColor;
        UpdateNewTagColorDot();
        BuildProjEditTagChips();
        ProjectEditorOverlay.Visibility = Visibility.Visible;
        ProjEditName.Focus();
        ProjEditName.SelectAll();
    }

    /// <summary>يعكس لون التاك الجديد المختار على النقطة الملوّنة بجانب حقل الإضافة.</summary>
    private void UpdateNewTagColorDot()
    {
        try { ProjNewTagColorDot.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_projNewTagColor)); }
        catch { ProjNewTagColorDot.Background = (Brush)FindResource("Brush.Accent"); }
    }

    /// <summary>يبني رقاقات التاكات في محرّر المشروع (كل تاك معروف رقاقة تبديل؛ النشطة = مسنَدة للمشروع).</summary>
    private void BuildProjEditTagChips()
    {
        ProjEditTagPanel.Children.Clear();
        foreach (var t in TagService.All)
        {
            bool active = _projEditTags.Any(x => string.Equals(x, t.Name, StringComparison.OrdinalIgnoreCase));
            ProjEditTagPanel.Children.Add(MakeTagChip(t.Name, t.Color, active, toggle: true));
        }
        if (TagService.All.Count == 0)
            ProjEditTagPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("proj.noTags"), Foreground = (Brush)FindResource("Brush.TextMuted"),
                FontSize = 11, Margin = new Thickness(2),
            });
    }

    /// <summary>نقر رقاقة تاك في المحرّر: يعكس إسناد التاك للمشروع قيد التحرير.</summary>
    private void ProjEditTagChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string name }) return;
        int i = _projEditTags.FindIndex(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _projEditTags.RemoveAt(i);
        else _projEditTags.Add(name);
        BuildProjEditTagChips();
    }

    private void ProjEditAddTag_Click(object sender, RoutedEventArgs e) => AddProjEditTag();
    private void ProjEditNewTag_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddProjEditTag(); e.Handled = true; }
    }

    /// <summary>ينشئ تاكاً جديداً باللون المختار ويُسنِده للمشروع قيد التحرير.</summary>
    private void AddProjEditTag()
    {
        string name = ProjEditNewTag.Text.Trim();
        if (name.Length == 0) return;
        TagService.GetOrCreate(name, _projNewTagColor);   // يُطلِق OnTagsChanged (حفظ + إعادة رسم)
        if (!_projEditTags.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
            _projEditTags.Add(name);
        ProjEditNewTag.Text = "";
        _projNewTagColor = TagService.NextAutoColor;
        UpdateNewTagColorDot();
        BuildProjEditTagChips();
    }

    private void ProjNewTagColorDot_Click(object sender, MouseButtonEventArgs e) => OpenColorPickerForNew();

    private void ProjEditBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("proj.folder.browse") };
        if (!string.IsNullOrWhiteSpace(ProjEditFolder.Text) && Directory.Exists(ProjEditFolder.Text))
            dlg.InitialDirectory = ProjEditFolder.Text;
        if (dlg.ShowDialog() == true) ProjEditFolder.Text = dlg.FolderName;
    }

    private void ProjEditCancel_Click(object sender, RoutedEventArgs e) => ProjectEditorOverlay.Visibility = Visibility.Collapsed;
    private void ProjectEditorOverlay_MouseDown(object sender, MouseButtonEventArgs e) => ProjectEditorOverlay.Visibility = Visibility.Collapsed;

    private void ProjEditSave_Click(object sender, RoutedEventArgs e)
    {
        string name = ProjEditName.Text.Trim();
        if (name.Length == 0) { ShowAlert("تنبيه", "أدخل اسم المشروع."); return; }
        string shellKey = (ProjEditShell.SelectedItem as ShellDef)?.Key ?? "";
        string folder = ProjEditFolder.Text.Trim();

        if (_projEditTarget is null)
        {
            if (ProjectService.Find(name) != null) { ShowAlert("تنبيه", "اسم المشروع مستعمَل."); return; }
            ProjectService.Create(name);
        }
        else
        {
            string old = _projEditTarget.Name;
            if (!string.Equals(old, name, StringComparison.OrdinalIgnoreCase))
            {
                if (ProjectService.Find(name) != null) { ShowAlert("تنبيه", "اسم المشروع مستعمَل."); return; }
                ProjectService.Rename(old, name);
                if (string.Equals(_quickProject, old, StringComparison.OrdinalIgnoreCase)) _quickProject = name;
            }
        }
        ProjectService.SetFolder(name, folder);
        ProjectService.SetShell(name, shellKey);
        ProjectService.SetTags(name, _projEditTags);

        ProjectEditorOverlay.Visibility = Visibility.Collapsed;
        OpenQuickDock(name);   // افتح لوحة المشروع لإضافة أوامره
    }

    /// <summary>يُحدَّث عند تغيّر المشاريع (إضافة/تعديل/حذف): يحفظ ويعيد رسم القوائم.</summary>
    private void OnProjectsChanged()
    {
        _projectStore.Save(ProjectService.All);
        RefreshProjectsList();   // «لوحة المشاريع» + نقاط الشريط
        // إعادة بناء لوحة الأوامر السريعة إن كانت مفتوحة (لالتقاط أوامر/ألوان محدَّثة).
        if (_quickProject != null && QuickDock.Visibility == Visibility.Visible) BuildQuickDock();
    }

    /// <summary>يعيد رسم شارات الشريط الجانبيّ وقائمة الأوامر (لالتقاط ألوان/وسوم محدَّثة).</summary>
    private void RefreshCommandVisuals()
    {
        SidebarDots.Items.Refresh();
        EntriesList.Items.Refresh();
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

    /// <summary>كم بروفايلاً يظهر قبل «عرض الكل» — قائمة قصيرة تُقرأ بلمحة.</summary>
    private const int ProfileMenuPreviewCount = 5;

    /// <summary>سهم القائمة بجانب زرّ «+»: أوّل خمسة بروفايلات + توسيع + إدارتها (T-101.4).</summary>
    private void NewTabDropDown_Click(object sender, RoutedEventArgs e)
        => ShowProfilesMenu(sender as UIElement, showAll: false);

    /// <summary>
    /// يبني قائمة البروفايلات: افتراضاً أوّل <see cref="ProfileMenuPreviewCount"/> فقط مع بند
    /// «عرض الكل (N)» يعيد بناءها كاملةً في المكان نفسه — فلا تطول القائمة بلا داعٍ.
    /// </summary>
    private void ShowProfilesMenu(UIElement? target, bool showAll)
    {
        var menu = new ContextMenu { FlowDirection = Loc.Flow, PlacementTarget = target };
        var available = ShellCatalog.Profiles.Where(p => p.Available).ToList();
        var shown = showAll ? available : available.Take(ProfileMenuPreviewCount).ToList();

        foreach (var p in shown)
        {
            var captured = p.Id;
            var item = new MenuItem { Header = p.DisplayLabel };
            item.Click += (_, _) => OpenTerminalForProfile(captured);
            menu.Items.Add(item);
        }

        if (shown.Count < available.Count)
        {
            var more = new MenuItem
            {
                Header = string.Format(Loc.T("profiles.showAll"), available.Count),
                Foreground = (Brush)FindResource("Brush.TextMuted"),
            };
            more.Click += (_, _) => ShowProfilesMenu(target, showAll: true);
            menu.Items.Add(more);
        }

        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = Loc.T("profiles.manage") };
        manage.Click += (_, _) => OpenProfileManager();
        menu.Items.Add(manage);

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
        else ShowAlert("بروفايلات الصدفات", "اختر بروفايلاً مخصّصاً لتعديله (الصدفات المكتشَفة غير قابلة للتعديل).");
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ShellProfile { IsBuiltIn: false } p)
        {
            ShowAlert(Loc.T("dlg.profile.pickTitle"), Loc.T("dlg.profile.pickMsg"));
            return;
        }
        ShowConfirm(Loc.T("dlg.profile.title"), string.Format(Loc.T("dlg.profile.msg"), p.Name),
            new[] { (Loc.T("dlg.cancel"), "cancel", Views.DialogButtonKind.Neutral), (Loc.T("dlg.delete"), "delete", Views.DialogButtonKind.Danger) },
            key =>
            {
                if (key != "delete") return;
                _profiles.CustomProfiles.RemoveAll(x => x.Id == p.Id);
                if (_profiles.DefaultProfileId == p.Id) _profiles.DefaultProfileId = null;
                _profileStore.Save(_profiles);
                ShellCatalog.Initialize(_profiles.CustomProfiles, _profiles.DefaultProfileId);
                RefreshProfileManager();
            });
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
            ShowAlert("تنبيه", "أدخل اسماً للبروفايل.");
            return;
        }
        string exe = ProfileEditorExe.Text.Trim();
        string args = ProfileEditorArgs.Text.Trim();
        if (exe.Length == 0 && args.Length == 0)
        {
            ShowAlert("تنبيه", "أدخل ملفّاً تنفيذيّاً أو سطر أمر (وسائط).");
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

    /// <summary>
    /// يفتح الأمر في تبويب جديد يحمل حاوية أجزاء بجزء واحد ويعيد التبويب المُنشأ (يستعمله الاسترجاع
    /// لإعادة تطبيق لون التبويب). <paramref name="sessionId"/> يُمرَّر عند الاسترجاع.
    /// </summary>
    private TabItem OpenTerminal(CommandEntry entry, string? sessionId = null)
    {
        var container = new TerminalPaneContainer(CreateTerminalView(entry, sessionId));
        var tab = new TabItem { Content = container, Header = BuildHeader(entry.Name, out var closeButton), Tag = entry };
        container.Emptied += _ => CloseTab(tab);
        closeButton.Click += (_, _) => CloseTab(tab);
        tab.ContextMenu = BuildTabContextMenu(tab);
        TerminalTabs.Items.Add(tab);
        TerminalTabs.SelectedItem = tab;
        UpdateHint();
        if (!_restoring) SaveSession();   // نُثبّت اللقطة فور فتح التبويب (لا تعتمد على الإغلاق السليم)
        return tab;
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

    // ===== قائمة المشروع السياقيّة (خيارات المشروع) =====

    /// <summary>
    /// قائمة صفّ المشروع بالزرّ الأيمن: فتح · نسخ الاسم/الباث · فتح في المستكشف · تسمية/تعديل/تكرار ·
    /// حذف · صفّ ألوان. تُبنى مرّةً وتعمل على <see cref="Selected"/> — الصفّ يُحدَّد قبل الفتح.
    /// </summary>
    private ContextMenu BuildProjectContextMenu()
    {
        var menu = new ContextMenu();

        MenuItem Item(string key, Action<Project> act, string? gesture = null, bool danger = false)
        {
            var mi = new MenuItem { Header = Loc.T(key), InputGestureText = gesture ?? "" };
            if (danger) mi.Style = (Style)FindResource("MenuItem.Danger");
            mi.Click += (_, _) => { if (Selected is { } p) act(p); };
            menu.Items.Add(mi);
            return mi;
        }

        Item("ctx.open", p => OpenQuickDock(p.Name), "↵");
        Item("projctx.openNew", p => OpenProjectInNewTab(p));
        menu.Items.Add(new Separator());

        Item("projctx.copyName", p => CopyToClipboard(p.Name));
        var copyPath = Item("projctx.copyPath", p => CopyToClipboard(p.Folder));
        var explorer = Item("projctx.explorer", p => RevealInExplorer(p.Folder));
        menu.Items.Add(new Separator());

        Item("projctx.rename", RenameProject);
        Item("ctx.edit", OpenProjectEditor, "E");
        Item("projctx.duplicate", DuplicateProject);
        menu.Items.Add(new Separator());

        Item("dlg.delete", _ => DeleteButton_Click(this, new RoutedEventArgs()), "Del", danger: true);
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildColorRow(menu, hex =>
        {
            if (Selected is { } p) { ProjectService.SetColor(p.Name, hex); RefreshProjectsList(); }
        }));

        // بلا باث ⇒ نسخ الباث وفتح المستكشف بلا معنى.
        menu.Opened += (s, e) =>
        {
            bool hasFolder = !string.IsNullOrWhiteSpace(Selected?.Folder);
            copyPath.IsEnabled = hasFolder;
            explorer.IsEnabled = hasFolder;
        };
        return menu;
    }

    /// <summary>يفتح المشروع في تبويب تيرمنال جديد مباشرةً (بلا المرور بلوحة الأوامر).</summary>
    private void OpenProjectInNewTab(Project p)
        => OpenTerminal(new CommandEntry { Name = p.Name, Shell = p.Shell, Path = p.Folder });

    /// <summary>يفتح مجلّد المشروع في مستكشف ويندوز (يتجاهل الباث غير الموجود بصمت).</summary>
    private static void RevealInExplorer(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe", Arguments = $"\"{folder}\"", UseShellExecute = true,
            });
        }
        catch { /* لا مستكشف — تجاهل */ }
    }

    /// <summary>يعيد تسمية المشروع بعد سؤال المستخدم (الاسم المكرّر يُرفَض داخل ProjectService).</summary>
    private void RenameProject(Project p)
    {
        string? name = Views.AppDialog.Prompt(this, Loc.T("projctx.rename"), Loc.T("projctx.rename.msg"),
            p.Name, Loc.T("dlg.save"));
        if (name is null || name == p.Name) return;
        ProjectService.Rename(p.Name, name);
        RefreshProjectsList();
    }

    /// <summary>ينسخ المشروع بكلّ أوامره باسم «الاسم — نسخة» (اسم فريد عند التكرار).</summary>
    private void DuplicateProject(Project p)
    {
        string baseName = $"{p.Name} — {Loc.T("projctx.copy.suffix")}";
        string name = baseName;
        for (int i = 2; ProjectService.Find(name) != null; i++) name = $"{baseName} {i}";

        var copy = ProjectService.Create(name);
        copy.Folder = p.Folder;
        copy.Shell  = p.Shell;
        copy.Color  = p.Color;
        copy.Tags   = new List<string>(p.Tags);
        copy.Commands = p.Commands
            .Select(c => new ProjectCommand { Label = c.Label, Steps = new List<string>(c.Steps), Folder = c.Folder })
            .ToList();
        ProjectService.NotifyChanged();   // Create حفظ الاسم فقط — الحقول أعلاه تحتاج حفظاً ثانياً
    }

    // ===== قائمة التبويب السياقيّة (خيارات النافذة) =====

    /// <summary>
    /// قائمة التبويب بالزرّ الأيمن: تسمية/تكرار/فصل · نسخ العنوان والمجلّد · ترتيب · إغلاق (هو/الآخرون/
    /// ما بعده) · صفّ ألوان. تُبنى مرّةً لكلّ تبويب وتُحدَّث حالتها عند كلّ فتح (Opened) — فالمواضع تتغيّر.
    /// </summary>
    private ContextMenu BuildTabContextMenu(TabItem tab)
    {
        var menu = new ContextMenu { FlowDirection = Loc.Flow };

        MenuItem Item(string key, Action act)
        {
            var mi = new MenuItem { Header = Loc.T(key) };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
            return mi;
        }

        Item("tabctx.rename", () => RenameTab(tab));
        Item("tabctx.duplicate", () => DuplicateTab(tab));
        Item("tabctx.detach", () => DetachTab(tab));
        menu.Items.Add(new Separator());

        Item("tabctx.copyTitle", () => CopyToClipboard(HeaderTitle(tab) ?? ""));
        Item("tabctx.copyCwd", () => CopyToClipboard((tab.Tag as CommandEntry)?.Path ?? ""));
        menu.Items.Add(new Separator());

        var moveStart = Item("tabctx.moveStart", () => MoveTab(tab, -1));
        var moveEnd   = Item("tabctx.moveEnd",   () => MoveTab(tab, +1));
        menu.Items.Add(new Separator());

        Item("tabctx.close", () => CloseTab(tab));
        var closeOthers = Item("tabctx.closeOthers", () => CloseOtherTabs(tab));
        var closeAfter  = Item("tabctx.closeAfter",  () => CloseTabsAfter(tab));
        menu.Items.Add(new Separator());

        menu.Items.Add(BuildColorRow(menu, hex => SetTabColor(tab, hex)));

        // الحالة تتغيّر مع ترتيب التبويبات وعددها، فتُحسَب عند كلّ فتح لا مرّةً عند البناء.
        menu.Opened += (_, _) =>
        {
            int i = TerminalTabs.Items.IndexOf(tab), n = TerminalTabs.Items.Count;
            moveStart.IsEnabled   = i > 0;
            moveEnd.IsEnabled     = i >= 0 && i < n - 1;
            closeOthers.IsEnabled = n > 1;
            closeAfter.IsEnabled  = i >= 0 && i < n - 1;
        };
        return menu;
    }

    /// <summary>
    /// صفّ ألوان أفقيّ داخل قائمة سياقيّة: دائرة «بلا لون» ثمّ لوحة التاكات. النقر يطبّق ويغلق القائمة.
    /// يُستعمل لتلوين التبويب والمشروع معاً — نفس اللغة البصريّة في الموضعين.
    /// </summary>
    private MenuItem BuildColorRow(ContextMenu owner, Action<string> apply)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 2, 2) };

        Border Dot(string hex, bool none)
        {
            var b = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
                ToolTip = none ? Loc.T("tabctx.color.none") : hex,
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("Brush.Hairline"),
                Background = none ? Brushes.Transparent : ColorBrush(hex),
            };
            if (none)
                b.Child = new TextBlock
                {
                    Text = "✕", FontSize = 9,
                    Foreground = (Brush)FindResource("Brush.TextMuted"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            b.MouseLeftButtonUp += (_, _) => { apply(none ? "" : hex); owner.IsOpen = false; };
            return b;
        }

        row.Children.Add(Dot("", none: true));
        foreach (var hex in TagService.Palette) row.Children.Add(Dot(hex, none: false));

        // العنصر نفسه ليس قابلاً للنقر — الرقاقات وحدها تتلقّى النقر.
        return new MenuItem { Header = row, StaysOpenOnClick = true, Focusable = false };
    }

    /// <summary>فرشاة مجمَّدة من hex، ورماديّة عند لون غير صالح.</summary>
    private static Brush ColorBrush(string hex)
    {
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
        catch { return Brushes.Gray; }
    }

    /// <summary>نسخ نصّ للحافظة بأمان (الحافظة قد تكون مقفولة من تطبيق آخر).</summary>
    private static void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); } catch { /* الحافظة مشغولة — تجاهل بصمت */ }
    }

    /// <summary>يعيد تسمية التبويب (يحدّث الرأس فقط — الأمر المحفوظ لا يتغيّر).</summary>
    private void RenameTab(TabItem tab)
    {
        string current = HeaderTitle(tab) ?? "";
        string? name = Views.AppDialog.Prompt(this, Loc.T("tabctx.rename"), Loc.T("tabctx.rename.msg"),
            current, Loc.T("dlg.save"));
        if (name is null) return;
        if (tab.Header is StackPanel p && p.Children.OfType<TextBlock>().FirstOrDefault() is { } tb)
            tb.Text = name;
        SaveSession();
    }

    /// <summary>يفتح تبويباً جديداً بنفس أمر التبويب الحاليّ (جلسة جديدة — لا يُنسخ تاريخ الجلسة).</summary>
    private void DuplicateTab(TabItem tab)
    {
        if (tab.Tag is not CommandEntry entry) return;
        var copy = OpenTerminal(new CommandEntry
        {
            Name = entry.Name, Shell = entry.Shell, Path = entry.Path, Command = entry.Command,
        });
        SetTabColor(copy, TabColor(tab));   // النسخة ترث لون الأصل
    }

    /// <summary>يفصل الجزء النشط في التبويب إلى نافذة مستقلّة (نفس مسار زرّ الفصل).</summary>
    private void DetachTab(TabItem tab)
    {
        if ((tab.Content as TerminalPaneContainer)?.ActiveView is { } view) DetachViewToWindow(view);
    }

    /// <summary>ينقل التبويب خطوةً في الاتّجاه المعطى مع إبقائه محدَّداً.</summary>
    private void MoveTab(TabItem tab, int delta)
    {
        int i = TerminalTabs.Items.IndexOf(tab);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= TerminalTabs.Items.Count) return;
        TerminalTabs.Items.Remove(tab);
        TerminalTabs.Items.Insert(j, tab);
        TerminalTabs.SelectedItem = tab;
        SaveSession();
    }

    /// <summary>يغلق كلّ التبويبات عدا المعطى.</summary>
    private void CloseOtherTabs(TabItem keep)
    {
        foreach (var other in TerminalTabs.Items.OfType<TabItem>().Where(t => t != keep).ToList())
            CloseTab(other);
    }

    /// <summary>يغلق التبويبات التالية للمعطى (من الأبعد للأقرب كي لا تتزحزح المواضع).</summary>
    private void CloseTabsAfter(TabItem tab)
    {
        int i = TerminalTabs.Items.IndexOf(tab);
        if (i < 0) return;
        for (int k = TerminalTabs.Items.Count - 1; k > i; k--)
            if (TerminalTabs.Items[k] is TabItem t) CloseTab(t);
    }

    /// <summary>
    /// يلوّن نقطة رأس التبويب (أو يخفيها عند لون فارغ) ويثبّت اللقطة كي يبقى اللون بعد إعادة التشغيل.
    /// الـ hex يُحفَظ في <c>Tag</c> النقطة — مصدر الحقيقة عند القراءة، لا لون الفرشاة.
    /// </summary>
    private void SetTabColor(TabItem tab, string hex)
    {
        if (TabColorDot(tab) is not { } dot) return;
        dot.Tag = hex ?? "";
        if (string.IsNullOrEmpty(hex)) dot.Visibility = Visibility.Collapsed;
        else
        {
            dot.Background = ColorBrush(hex);
            dot.Visibility = Visibility.Visible;
        }
        if (!_restoring) SaveSession();
    }

    /// <summary>لون التبويب المحفوظ (فارغ = بلا لون).</summary>
    private static string TabColor(TabItem tab) => TabColorDot(tab)?.Tag as string ?? "";

    /// <summary>نقطة اللون في رأس التبويب (أوّل Border في الرأس — راجع <see cref="BuildHeader"/>).</summary>
    private static Border? TabColorDot(TabItem tab)
        => tab.Header is StackPanel p ? p.Children.OfType<Border>().FirstOrDefault() : null;

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
        // نقطة لون التبويب: مخفيّة حتّى يختار المستخدم لوناً من قائمة التبويب. تسبق العنوان بصرياً
        // لكنّ قراءة العنوان تبحث عن أوّل TextBlock فلا تتأثّر بترتيب الأبناء.
        panel.Children.Add(new Border
        {
            Width = 7, Height = 7, CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        });
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

    // ===== الشريط الجانبيّ الدائم: لوحة مشاريع مرسوّة تُطوى إلى شريط أيقونيّ =====

    /// <summary>عرض العمود في الحالتين (بكسل).</summary>
    private const double SidebarExpandedWidth = 280;
    private const double SidebarCollapsedWidth = 60;

    /// <summary>يبدّل بين اللوحة الموسَّعة والشريط الأيقونيّ. يعيد حالة التوسيع الجديدة.</summary>
    private bool ToggleSidebarExpanded()
    {
        SetSidebarExpanded(!_sidebarExpanded);
        return _sidebarExpanded;
    }

    private void SidebarMenu_Click(object sender, RoutedEventArgs e) => ToggleSidebarExpanded();

    /// <summary>
    /// يوسّع/يطوي الشريط الجانبيّ: العمود يتغيّر عرضه واللوحة تتبادل الظهور مع الشريط الأيقونيّ، مع
    /// تلاشٍ خفيف. الحالة تُحفَظ في الإعدادات فتعود كما تركها المستخدم في التشغيل التالي.
    /// </summary>
    private void SetSidebarExpanded(bool expanded, bool focusSearch = false)
    {
        _sidebarExpanded = expanded;
        SidebarCol.Width = new GridLength(expanded ? SidebarExpandedWidth : SidebarCollapsedWidth);
        SidebarPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        SidebarRail.Visibility  = expanded ? Visibility.Collapsed : Visibility.Visible;

        var target = expanded ? (UIElement)SidebarPanel : SidebarRail;
        target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        if (expanded && focusSearch) SidebarSearch.Focus();

        if (_settings.SidebarExpanded != expanded)
        {
            _settings.SidebarExpanded = expanded;
            SaveSettings();
        }
    }

    /// <summary>نقر مربّع مشروع في الشريط المطويّ: يفتح دوك أوامره.</summary>
    private void SidebarDot_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Project proj) return;
        OpenQuickDock(proj.Name);
    }

    /// <summary>يصفّي قائمة الأوامر المحفوظة حسب نصّ البحث (الاسم/الباث/الأمر).</summary>
    private void SidebarSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshProjectsList();
        // مرآة عكسيّة: البحث من داخل اللوحة يبقى ظاهراً في حبّة الرأس، فلا يختلف الحقلان.
        if (HeaderSearch.Text != SidebarSearch.Text) HeaderSearch.Text = SidebarSearch.Text;
    }

    /// <summary>
    /// حبّة البحث في شريط العنوان: تعكس نصّها على بحث اللوحة الجانبيّة (مصدر التصفية الوحيد) وتفتح
    /// اللوحة عند أوّل حرف — بلا سحب التركيز من الحقل الذي يكتب فيه المستخدم.
    /// </summary>
    private void HeaderSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SidebarSearch.Text != HeaderSearch.Text) SidebarSearch.Text = HeaderSearch.Text;
        if (HeaderSearch.Text.Length > 0 && !_sidebarExpanded) SetSidebarExpanded(true);
    }

    /// <summary>التركيز على حبّة البحث يوسّع لوحة المشاريع (النتائج تظهر فور الكتابة).</summary>
    private void HeaderSearch_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_sidebarExpanded) SetSidebarExpanded(true);
    }

    /// <summary>يحوّل عجلة الماوس/الباد إلى سكرول أفقيّ لصفوف الرقاقات (الدوك + شريط التاكات).</summary>
    private void HorizontalChips_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv && e.Delta != 0)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
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
                    if (entry.IsTransient) continue;   // تبويب لحظيّ (شِل حاوية) — لا يُحفَظ ولا يُسترجَع
                    // نلتقط جلسة الجزء النشط (معرّفها + آخر أمر) لاسترجاعها وإعادة تنفيذ آخر أمر.
                    var view = (tab.Content as TerminalPaneContainer)?.ActiveView;
                    tabs.Add(new TabSnapshot(HeaderTitle(tab) ?? entry.Name, entry.Shell, entry.Path,
                        view?.SessionId, view?.LastCommand, TabColor(tab)));
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
                var tab = OpenTerminal(new CommandEntry
                {
                    Name = t.Title,
                    Shell = t.ShellKey ?? ShellCatalog.DefaultKey,
                    Path = t.WorkingDirectory ?? "",
                    Command = t.LastCommand ?? "",
                }, t.SessionId);
                SetTabColor(tab, t.Color ?? "");   // لون التبويب يعود كما تركه المستخدم
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

    /// <summary>يستخرج نصّ عنوان التبويب من رأسه المبنيّ (أوّل TextBlock فيه — لا موضع ثابت).</summary>
    private static string? HeaderTitle(TabItem tab)
        => tab.Header is StackPanel panel
           ? panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text : null;

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
