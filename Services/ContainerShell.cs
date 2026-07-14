using System;
using System.Text.RegularExpressions;

namespace TerminalLauncher.Services;

/// <summary>
/// يبني الأمر البعيد لِـ«شِل تفاعليّ داخل حاوية Docker»: <c>[sudo] docker exec -it &lt;id&gt; sh -c
/// 'exec bash || exec sh'</c> (bash إن توفّر وإلّا sh — كثير من صور alpine بلا bash). يُنفَّذ داخل
/// جلسة <see cref="Terminal.Servers.Ssh.SshShellSession"/> فوق اتّصال SSH مُصادَق — بلا ssh.exe محلّيّ
/// وبلا طلب كلمة مرور ثانية.
/// </summary>
public static class ContainerShell
{
    /// <summary>اسم/معرّف حاوية Docker صالح (يبدأ بحرف/رقم ثمّ حروف/أرقام/<c>_ . -</c>).</summary>
    private static readonly Regex ContainerNameRx = new("^[a-zA-Z0-9][a-zA-Z0-9_.-]*$", RegexOptions.Compiled);

    /// <summary>الصدفة الافتراضيّة: bash إن توفّرت وإلّا sh.</summary>
    public const string DefaultShell = "sh -c 'exec bash || exec sh'";

    /// <summary>
    /// يبني الأمر البعيد لتشغيل شِل تفاعليّ داخل الحاوية <paramref name="containerId"/>. يرمي إن كان
    /// المعرّف غير صالح. <paramref name="sudo"/> يسبق بـ <c>sudo</c> (يطلب كلمة المرور داخل الجلسة إن لزم).
    /// <paramref name="innerCommand"/> هو ما يُشغَّل داخل الحاوية (مثل <c>bash</c>/<c>sh</c>/أمر مخصّص)؛
    /// null = الافتراضيّة.
    /// </summary>
    public static string BuildRemoteCommand(string containerId, bool sudo = false, string? innerCommand = null)
    {
        if (string.IsNullOrWhiteSpace(containerId) || !ContainerNameRx.IsMatch(containerId))
            throw new ArgumentException($"معرّف حاوية غير صالح: {containerId}", nameof(containerId));

        string inner = string.IsNullOrWhiteSpace(innerCommand) ? DefaultShell : innerCommand.Trim();
        string sudoPrefix = sudo ? "sudo " : "";
        return $"{sudoPrefix}docker exec -it {containerId} {inner}";
    }
}
