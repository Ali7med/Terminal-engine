using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace TerminalLauncher.Services;

/// <summary>
/// تسجيل كلمةٍ قصيرة تفتح التطبيق من شريط عنوان مستكشف ويندوز أو حوار «تشغيل» — كما يفعل
/// <c>cmd</c>: تكتبها في مجلد فيُفتح التطبيق <b>في ذلك المجلد</b>.
///
/// <para><b>كيف تعمل:</b> ويندوز يحلّ الكلمة المكتوبة بلا مسار عبر مفتاح <c>App Paths</c>. نكتب
/// المفتاح تحت <c>HKEY_CURRENT_USER</c> لا <c>HKEY_LOCAL_MACHINE</c>: الأوّل لا يحتاج صلاحيّة
/// مسؤول، ويكفي لأنّ التسجيل يخصّ مستخدماً واحداً على أيّ حال.</para>
///
/// <para><b>ولماذا المجلد يصل:</b> المستكشف يستدعي التشغيل ومجلدُه الحاليّ هو مجلد العمل، فيقرؤه
/// التطبيق من <see cref="Environment.CurrentDirectory"/>. لا حاجة إلى وسائط.</para>
/// </summary>
public static class ExplorerLaunch
{
    private const string AppPathsRoot = @"Software\Microsoft\Windows\CurrentVersion\App Paths";

    /// <summary>الكلمة الافتراضيّة المقترَحة.</summary>
    public const string DefaultKeyword = "hrt";

    /// <summary>مسار ملفّ التطبيق التنفيذيّ الحاليّ.</summary>
    private static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>
    /// هل الكلمة صالحة لاسم أمر؟ حروف وأرقام وشرطة فقط — أيّ محرف آخر يجعل ويندوز يعاملها مساراً
    /// أو يرفضها، فالرفض هنا أوضح من تسجيلٍ لا يعمل.
    /// </summary>
    public static bool IsValidKeyword(string? keyword)
        => !string.IsNullOrWhiteSpace(keyword)
        && keyword.Length <= 24
        && keyword.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');

    /// <summary>هل هذه الكلمة مسجَّلة الآن وتشير إلى ملفّ هذا التطبيق؟</summary>
    public static bool IsRegistered(string keyword)
    {
        if (!IsValidKeyword(keyword)) return false;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{AppPathsRoot}\{keyword}.exe");
            string? target = key?.GetValue(null) as string;
            return !string.IsNullOrWhiteSpace(target)
                && string.Equals(target.Trim('"'), ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>يسجّل الكلمة. يعيد رسالة الخطأ عند الفشل، أو null عند النجاح.</summary>
    public static string? Register(string keyword)
    {
        if (!IsValidKeyword(keyword)) return Loc.T("launch.errKeyword");
        if (ExePath.Length == 0 || !File.Exists(ExePath)) return Loc.T("launch.errExe");

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey($@"{AppPathsRoot}\{keyword}.exe");
            key.SetValue(null, ExePath);
            // ‏Path يجعل ويندوز يجد ملفّات التطبيق المجاورة عند التشغيل من مجلد آخر.
            key.SetValue("Path", Path.GetDirectoryName(ExePath) ?? "");
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return ex.Message;
        }
    }

    /// <summary>يزيل التسجيل. لا يرمي إن لم يكن مسجَّلاً.</summary>
    public static void Unregister(string keyword)
    {
        if (!IsValidKeyword(keyword)) return;

        try { Registry.CurrentUser.DeleteSubKeyTree($@"{AppPathsRoot}\{keyword}.exe", throwOnMissingSubKey: false); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
    }

    /// <summary>
    /// المجلد الذي أُطلِق منه التطبيق، أو null إن لم يكن إطلاقاً «من مكان».
    ///
    /// <para>أوّل وسيط يكون مجلداً موجوداً له الأولويّة (‏<c>hrt D:\code</c>)، وإلّا مجلد العمل
    /// الحاليّ — وهو ما يضعه المستكشف. ويُستثنى مجلد التطبيق نفسه: التشغيل بالنقر على الأيقونة
    /// يجعله مجلد العمل، وفتحُ تيرمنال في مجلد التثبيت ليس ما يريده أحد.</para>
    /// </summary>
    public static string? LaunchFolder()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                string candidate = args[i].Trim().Trim('"');
                if (candidate.Length > 0 && Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            }

            string cwd = Environment.CurrentDirectory;
            if (!Directory.Exists(cwd)) return null;

            string install = Path.GetDirectoryName(ExePath) ?? "";
            if (install.Length > 0 && PathsEqual(cwd, install)) return null;

            // مجلد النظام هو مجلد العمل حين يُطلَق التطبيق من بعض المسارات — وليس مقصوداً كذلك.
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (system.Length > 0 && PathsEqual(cwd, system)) return null;

            return Path.GetFullPath(cwd);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
