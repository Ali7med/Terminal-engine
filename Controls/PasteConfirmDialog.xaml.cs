using System;
using System.Text.RegularExpressions;
using System.Windows;
using TerminalLauncher.Services;

namespace TerminalLauncher.Controls;

/// <summary>
/// نافذة تحذير قبل اللصق (T-104.5): تعرض النصّ الملصوق كاملاً وتطلب تأكيداً صريحاً.
/// تُفتح فقط عندما يكون النصّ متعدّد الأسطر أو يحوي نمطاً خطراً (انظر <see cref="LooksDangerous"/>).
/// كلّ نصوصها معرَّبة عبر <see cref="Loc.T"/>.
/// </summary>
public partial class PasteConfirmDialog : Window
{
    /// <summary>أنماط أوامر تدميريّة تُطلق نافذة التأكيد حتى لو كان النصّ سطراً واحداً.</summary>
    private static readonly Regex[] DangerPatterns =
    {
        new(@"\brm\s+-[a-zA-Z]*r[a-zA-Z]*f|\brm\s+-[a-zA-Z]*f[a-zA-Z]*r", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bdel\s+/[sS]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bformat\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Remove-Item\b[^\r\n]*-Recurse", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bdd\s+if=", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private PasteConfirmDialog(string text, bool danger)
    {
        InitializeComponent();
        Title = Loc.T("paste.title");
        PasteButton.Content = Loc.T("paste.confirm");
        CancelButton.Content = Loc.T("paste.cancel");
        FlowDirection = Loc.Flow;

        int lineCount = CountLines(text);
        string prefix = danger ? Loc.T("paste.warnDanger") : Loc.T("paste.warnMulti");
        WarningText.Text = $"{prefix}  ({lineCount} {Loc.T("paste.lines")})";

        PreviewText.Text = text;
    }

    /// <summary>هل يحوي النصّ نمطاً خطراً معروفاً؟</summary>
    public static bool LooksDangerous(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var p in DangerPatterns)
            if (p.IsMatch(text)) return true;
        return false;
    }

    /// <summary>هل النصّ متعدّد الأسطر (يحوي سطراً حقيقياً غير ذيليّ)؟</summary>
    public static bool IsMultiLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // نُطبّع نهايات الأسطر ثمّ نقصّ الفراغ الذيليّ: لصق سطر واحد ينتهي بـ \n لا يُعدّ متعدّداً.
        string trimmed = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        return trimmed.Contains('\n');
    }

    /// <summary>
    /// يقرّر هل يحتاج النصّ نافذة تأكيد قبل اللصق. النصّ الآمن ذو السطر الواحد يمرّ بلا نافذة.
    /// </summary>
    public static bool RequiresConfirmation(string text)
        => IsMultiLine(text) || LooksDangerous(text);

    /// <summary>
    /// يعرض نافذة التأكيد (متزامنة) ويعيد <c>true</c> إن وافق المستخدم على اللصق.
    /// </summary>
    public static bool Confirm(string text, Window? owner)
    {
        var dlg = new PasteConfirmDialog(text, LooksDangerous(text));
        if (owner != null && !ReferenceEquals(owner, dlg)) dlg.Owner = owner;
        return dlg.ShowDialog() == true;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        string norm = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        int n = 1;
        foreach (char c in norm) if (c == '\n') n++;
        return n;
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
