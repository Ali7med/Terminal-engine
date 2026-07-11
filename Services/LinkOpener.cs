using System;
using System.Diagnostics;
using System.IO;

namespace TerminalLauncher.Services;

/// <summary>
/// يفتح الأهداف القابلة للنقر (روابط ومسارات) بأمان عبر صدفة النظام.
/// كلّ فتح يمرّ بـ <see cref="ProcessStartInfo.UseShellExecute"/>=true (لا تمرير لـshell نصّيّ)،
/// مع تعقيم الإدخال والتحقّق من الوجود قبل الفتح.
/// </summary>
public static class LinkOpener
{
    /// <summary>
    /// يفتح هدفاً مكتشَفاً: عنوان ويب → المتصفّح؛ مجلّد موجود → Explorer؛ ملفّ موجود → المحرّر الافتراضيّ
    /// (ومع رقم سطر يُفتح بـ <c>code -g file:line</c> إن توفّر VS Code). يعيد <c>true</c> إن نُفِّذ فتحٌ.
    /// </summary>
    public static bool Open(LinkTarget target)
    {
        switch (target.Kind)
        {
            case LinkTargetKind.Url:
                return OpenUrl(target.Value);
            case LinkTargetKind.Path:
                return OpenPath(target.Value, target.Line);
            default:
                return false;
        }
    }

    /// <summary>
    /// يفتح رابط OSC 8 صريح (نصّ خام محمَّل بالخليّة): عنوان ويب → المتصفّح، ومسار <c>file://</c> أو
    /// مسار نظام → معالجة المسار. يعيد <c>true</c> إن نُفِّذ فتحٌ.
    /// </summary>
    public static bool OpenExplicit(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        uri = uri.Trim();

        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return OpenUrl(uri);

        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(uri, UriKind.Absolute, out var fileUri))
            return OpenPath(fileUri.LocalPath, null);

        // مخطّطات أخرى (mailto:, vscode: ...) نمرّرها كما هي لصدفة النظام.
        if (uri.Contains("://") || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return ShellOpen(uri);

        // بلا مخطّط: عاملها كمسار نظام.
        return OpenPath(uri, null);
    }

    /// <summary>يفتح عنوان ويب في المتصفّح الافتراضيّ بعد التحقّق من صحّته.</summary>
    private static bool OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        return ShellOpen(u.AbsoluteUri);
    }

    /// <summary>
    /// يفتح مساراً: مجلّد → Explorer؛ ملفّ مع رقم سطر → VS Code على السطر إن توفّر، وإلّا المحرّر الافتراضيّ.
    /// يتحقّق من الوجود أوّلاً (لا يفتح مسارات غير موجودة). يعيد <c>false</c> إن لم يوجد المسار.
    /// </summary>
    private static bool OpenPath(string path, int? line)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full;
        try { full = Path.GetFullPath(path.Trim()); }
        catch { return false; }

        if (Directory.Exists(full))
            return ShellOpen(full);

        if (File.Exists(full))
        {
            if (line is int n && n > 0 && TryOpenInVsCode(full, n))
                return true;
            return ShellOpen(full);   // المحرّر الافتراضيّ للامتداد
        }

        return false;   // لا مسار موجود ⇒ لا فتح
    }

    /// <summary>
    /// يفتح ملفّاً في VS Code على سطر محدّد عبر <c>code -g &lt;file&gt;:&lt;line&gt;</c>.
    /// يعيد <c>false</c> إن لم يكن <c>code</c> متاحاً على المسار (فيرتدّ المتّصل للمحرّر الافتراضيّ).
    /// </summary>
    private static bool TryOpenInVsCode(string file, int line)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "code",
                UseShellExecute = true,   // يحلّ code / code.cmd من PATH عبر الصدفة
            };
            psi.ArgumentList.Add("-g");
            psi.ArgumentList.Add($"{file}:{line}");
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;   // code غير مثبّت/غير على PATH
        }
    }

    /// <summary>يفتح هدفاً عبر صدفة النظام (<c>UseShellExecute</c>=true) دون تمرير لأيّ shell نصّيّ.</summary>
    private static bool ShellOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
