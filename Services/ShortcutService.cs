using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;
using TerminalLauncher.Theme;

namespace TerminalLauncher.Services;

/// <summary>فعل قابل لإسناد اختصار إليه.</summary>
/// <param name="Id">معرّف ثابت يُخزَّن في الإعدادات (لا يُترجَم ولا يتغيّر).</param>
/// <param name="DefaultGesture">الاختصار الافتراضيّ بصيغة <c>Ctrl+Shift+P</c>.</param>
/// <param name="LabelKey">مفتاح الترجمة لاسم الفعل المعروض.</param>
public sealed record ShortcutAction(string Id, string DefaultGesture, string LabelKey);

/// <summary>
/// سجلّ اختصارات التطبيق، وتحويلها بين الصيغة النصّيّة المخزَّنة وضغطات المفاتيح.
///
/// <para><b>لماذا سجلّ لا سلسلة <c>if</c> داخل معالج المفاتيح:</b> اختصار مكتوب في الكود لا يمكن
/// عرضه في الإعدادات، ولا تغييره، ولا كشف تعارضه مع غيره. السجلّ يجعل «ما الاختصارات الموجودة؟»
/// سؤالاً له جواب واحد في مكان واحد — وهو شرط أن يكون التخصيص ممكناً أصلاً.</para>
/// </summary>
public static class ShortcutService
{
    /// <summary>تبويب تيرمنال فارغ جديد.</summary>
    public const string NewTerminal = "terminal.new";

    /// <summary>انقسام عموديّ (جنباً إلى جنب).</summary>
    public const string SplitVertical = "pane.splitV";

    /// <summary>انقسام أفقيّ (فوق/تحت).</summary>
    public const string SplitHorizontal = "pane.splitH";

    /// <summary>إغلاق الجزء النشط.</summary>
    public const string ClosePane = "pane.close";

    /// <summary>لوحة الأوامر.</summary>
    public const string CommandPalette = "palette.open";

    /// <summary>إظهار/إخفاء لوحة الذكاء.</summary>
    public const string AiPanel = "ai.panel";

    /// <summary>تبديل وضع صندوق الأوامر (أمر ⇄ ذكاء).</summary>
    public const string AiComposerMode = "ai.composerMode";

    /// <summary>البحث في مخرجات التيرمنال.</summary>
    public const string SearchOutput = "output.search";

    /// <summary>فتح لوحة الإعدادات.</summary>
    public const string OpenSettings = "settings.open";

    /// <summary>قاموس الأوامر (مكتبة الأوامر الشخصيّة).</summary>
    public const string CommandDictionary = "dict.open";

    private static readonly ShortcutAction[] Registry =
    {
        new(NewTerminal,     "Ctrl+Shift+T", "keys.newTerminal"),
        new(SplitVertical,   "Ctrl+Shift+D", "keys.splitV"),
        new(SplitHorizontal, "Ctrl+Shift+E", "keys.splitH"),
        new(ClosePane,       "Ctrl+W",       "keys.closePane"),
        new(CommandPalette,  "Ctrl+Shift+P", "keys.palette"),
        new(CommandDictionary, "Ctrl+Shift+K", "keys.dictionary"),
        new(AiPanel,         "Ctrl+P",       "keys.aiPanel"),
        new(AiComposerMode,  "Ctrl+I",       "keys.aiMode"),
        new(SearchOutput,    "Ctrl+F",       "keys.search"),
        new(OpenSettings,    "Ctrl+,",       "keys.settings"),
    };

    /// <summary>
    /// أسماء معروضة لمفاتيح <c>Oem*</c>: المستخدم يبحث عن «,» على لوحته لا عن
    /// <c>OemComma</c> — وهو اسم داخليّ لا يقوله أحد.
    /// </summary>
    private static readonly (Key Key, string Label)[] KeyLabels =
    {
        (Key.OemComma, ","),
        (Key.OemPeriod, "."),
        // ‎«+» غير مدرَج عمداً: الصيغة تُقسَّم على '+' فلا يعود «Ctrl++» قابلاً للتحليل.
        (Key.OemMinus, "-"),
        (Key.OemQuestion, "/"),
        (Key.OemSemicolon, ";"),
        (Key.OemQuotes, "'"),
        (Key.OemOpenBrackets, "["),
        (Key.OemCloseBrackets, "]"),
        (Key.OemBackslash, "\\"),
        (Key.OemTilde, "`"),
    };

    /// <summary>كلّ الأفعال بترتيب العرض.</summary>
    public static IReadOnlyList<ShortcutAction> All => Registry;

    /// <summary>الاختصار الفعّال لفعل: ما خصّصه المستخدم، وإلّا الافتراضيّ.</summary>
    public static string GestureFor(AppSettings settings, string id)
    {
        if (settings.Shortcuts.TryGetValue(id, out string? custom) && custom.Length > 0) return custom;

        foreach (ShortcutAction action in Registry)
            if (action.Id == id) return action.DefaultGesture;

        return "";
    }

    /// <summary>هل هذا الاختصار مخصّص من المستخدم (لا الافتراضيّ)؟</summary>
    public static bool IsCustom(AppSettings settings, string id)
        => settings.Shortcuts.TryGetValue(id, out string? custom) && custom.Length > 0;

    /// <summary>هل تطابق الضغطة اختصار هذا الفعل؟</summary>
    public static bool Matches(AppSettings settings, string id, Key key, ModifierKeys mods)
        => TryParse(GestureFor(settings, id), out Key k, out ModifierKeys m)
        && k == key && m == Normalize(mods);

    /// <summary>الفعل المطابق للضغطة، أو null إن لا اختصار لها.</summary>
    public static string? Match(AppSettings settings, Key key, ModifierKeys mods)
    {
        foreach (ShortcutAction action in Registry)
            if (Matches(settings, action.Id, key, mods)) return action.Id;

        return null;
    }

    /// <summary>
    /// الفعل الذي يملك هذا الاختصار حاليّاً (عدا <paramref name="exceptId"/>)، أو null.
    /// يُستعمَل لكشف التعارض <b>قبل</b> الإسناد — اختصاران لفعلين يجعل أحدهما ميّتاً بصمت.
    /// </summary>
    public static string? Owner(AppSettings settings, string gesture, string exceptId)
    {
        foreach (ShortcutAction action in Registry)
            if (action.Id != exceptId
                && string.Equals(GestureFor(settings, action.Id), gesture, StringComparison.OrdinalIgnoreCase))
                return action.Id;

        return null;
    }

    /// <summary>اسم الفعل المترجَم من معرّفه.</summary>
    public static string LabelFor(string id)
    {
        foreach (ShortcutAction action in Registry)
            if (action.Id == id) return Loc.T(action.LabelKey);

        return id;
    }

    /// <summary>يسند اختصاراً لفعل. النصّ الفارغ يعيده إلى الافتراضيّ.</summary>
    public static void Assign(AppSettings settings, string id, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) settings.Shortcuts.Remove(id);
        else settings.Shortcuts[id] = gesture.Trim();
    }

    /// <summary>يعيد كلّ الاختصارات إلى الافتراضيّ.</summary>
    public static void ResetAll(AppSettings settings) => settings.Shortcuts.Clear();

    /// <summary>يصوغ ضغطة بصيغة قابلة للتخزين والعرض معاً: <c>Ctrl+Shift+P</c>.</summary>
    public static string Format(Key key, ModifierKeys mods)
    {
        mods = Normalize(mods);

        var sb = new StringBuilder();
        if ((mods & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
        if ((mods & ModifierKeys.Alt) != 0) sb.Append("Alt+");
        if ((mods & ModifierKeys.Shift) != 0) sb.Append("Shift+");
        sb.Append(LabelOf(key));

        return sb.ToString();
    }

    /// <summary>
    /// يحلّل صيغة مخزَّنة. يعيد false لصيغة تالفة — فتُهمَل ويعود الافتراضيّ بدل أن يفقد الفعل
    /// اختصاره كلّيّاً بسبب حرف مكتوب خطأً في ملفّ الإعدادات.
    /// </summary>
    public static bool TryParse(string? gesture, out Key key, out ModifierKeys mods)
    {
        key = Key.None;
        mods = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        foreach (string part in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = part.Trim();
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control": mods |= ModifierKeys.Control; continue;
                case "alt": mods |= ModifierKeys.Alt; continue;
                case "shift": mods |= ModifierKeys.Shift; continue;
            }

            Key labelled = KeyOf(token);
            if (labelled != Key.None) { key = labelled; continue; }

            if (!Enum.TryParse(token, ignoreCase: true, out Key parsed)) return false;
            key = parsed;
        }

        return key != Key.None;
    }

    /// <summary>هل هذا المفتاح مُعدِّل وحده؟ (لا يصلح اختصاراً بذاته.)</summary>
    public static bool IsModifier(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin
        or Key.System;

    /// <summary>الاسم المعروض لمفتاح: الرمز المطبوع على اللوحة إن وُجد، وإلّا اسم التعداد.</summary>
    private static string LabelOf(Key key)
    {
        foreach ((Key candidate, string label) in KeyLabels)
            if (candidate == key) return label;

        return key.ToString();
    }

    /// <summary>عكس <see cref="LabelOf"/> — يعيد <see cref="Key.None"/> إن لم يكن رمزاً معروفاً.</summary>
    private static Key KeyOf(string token)
    {
        foreach ((Key candidate, string label) in KeyLabels)
            if (label == token) return candidate;

        return Key.None;
    }
    /// <summary>مفتاح ويندوز لا يدخل المطابقة — لا يُلتقط أصلاً على مستوى التطبيق.</summary>
    private static ModifierKeys Normalize(ModifierKeys mods)
        => mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
}
