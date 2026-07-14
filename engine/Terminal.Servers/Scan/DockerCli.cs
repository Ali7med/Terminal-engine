namespace Terminal.Servers.Scan;

/// <summary>مساعدات مشتركة لبناء أوامر Docker عبر SSH (بادئة sudo).</summary>
public static class DockerCli
{
    /// <summary>
    /// بادئة الأمر: <c>"sudo -n "</c> عند <paramref name="sudo"/> (غير تفاعليّ — يفشل بلا NOPASSWD
    /// بدل التعليق إذ لا tty في جلسة exec)، وإلّا سلسلة فارغة. للأوامر التفاعليّة (شِل عبر ssh) استعمل
    /// <see cref="InteractivePrefix"/> كي يطلب sudo كلمة المرور داخل التبويب.
    /// </summary>
    public static string Prefix(bool sudo) => sudo ? "sudo -n " : "";

    /// <summary>بادئة sudo تفاعليّة (<c>"sudo "</c>) — تُستعمل حين تتوفّر tty فيطلب sudo كلمة المرور.</summary>
    public static string InteractivePrefix(bool sudo) => sudo ? "sudo " : "";
}
