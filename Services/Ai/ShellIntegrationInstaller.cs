using System;
using System.IO;

namespace TerminalLauncher.Services.Ai;

/// <summary>حالة تكامل الصدفة في ملفّ بروفايلها.</summary>
public enum IntegrationStatus
{
    /// <summary>البروفايل غير موجود بعد — سيُنشأ عند الحقن.</summary>
    ProfileMissing,

    /// <summary>البروفايل موجود ولا يحتوي الخطاف.</summary>
    NotInstalled,

    /// <summary>الخطاف مثبَّت أصلاً (العلامة موجودة).</summary>
    Installed,
}

/// <summary>نتيجة محاولة حقن.</summary>
/// <param name="Ok">هل نجح الحقن؟</param>
/// <param name="BackupPath">مسار النسخة الاحتياطية إن أُنشئت.</param>
/// <param name="Message">رسالة للعرض (نجاح أو سبب فشل).</param>
public sealed record IntegrationResult(bool Ok, string? BackupPath, string Message);

/// <summary>
/// يحقن خطافات تكامل الصدفة (OSC 133) في ملفّ البروفايل — بأمان.
///
/// <para><b>ثلاثة ضمانات:</b> (1) لا نكتب إن كان الخطاف مثبَّتاً أصلاً (كشف بالعلامة)، (2) نأخذ
/// نسخة احتياطية من البروفايل قبل أيّ تعديل، (3) نُلحق في النهاية بلا مساس بمحتوى المستخدم.
/// الكتابة لا تجري إلّا باستدعاء صريح من المعالج بعد أن يرى المستخدم الـdiff.</para>
/// </summary>
public static class ShellIntegrationInstaller
{
    /// <summary>يفحص حالة التكامل في بروفايل الصدفة.</summary>
    public static IntegrationStatus StatusOf(ShellIntegrationHook hook)
    {
        try
        {
            if (!File.Exists(hook.ProfilePath)) return IntegrationStatus.ProfileMissing;
            string content = File.ReadAllText(hook.ProfilePath);
            return content.Contains(hook.Marker, StringComparison.Ordinal)
                ? IntegrationStatus.Installed
                : IntegrationStatus.NotInstalled;
        }
        catch (IOException)
        {
            return IntegrationStatus.NotInstalled;
        }
    }

    /// <summary>
    /// يحقن الخطاف. يُنشئ مجلد البروفايل إن لزم، ينسخ احتياطياً، ثمّ يُلحق الخطاف. لا يرمي —
    /// يعيد نتيجة مُصنَّفة كي يعرضها المعالج.
    /// </summary>
    public static IntegrationResult Install(ShellIntegrationHook hook)
    {
        try
        {
            if (StatusOf(hook) == IntegrationStatus.Installed)
                return new IntegrationResult(true, null, "التكامل مثبَّت أصلاً.");

            string? dir = Path.GetDirectoryName(hook.ProfilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string? backup = null;
            string existing = "";
            if (File.Exists(hook.ProfilePath))
            {
                existing = File.ReadAllText(hook.ProfilePath);
                backup = BackupPathFor(hook.ProfilePath);
                File.Copy(hook.ProfilePath, backup, overwrite: true);
            }

            // إلحاق بسطر فاصل — لا نلمس ما كتبه المستخدم قبله.
            string separator = existing.Length == 0 || existing.EndsWith('\n') ? "\n" : "\n\n";
            File.AppendAllText(hook.ProfilePath, separator + hook.Hook + "\n");

            return new IntegrationResult(true, backup, "تمّ الحقن بنجاح.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new IntegrationResult(false, null, "تعذّرت الكتابة إلى ملفّ البروفايل: " + ex.Message);
        }
    }

    /// <summary>مسار النسخة الاحتياطية: البروفايل نفسه بلاحقة <c>.tl-backup</c>.</summary>
    private static string BackupPathFor(string profilePath) => profilePath + ".tl-backup";
}
