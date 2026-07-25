using System;

namespace TerminalLauncher.Services.Ai;

/// <summary>الأصداف المدعومة في حقن خطافات تكامل الصدفة.</summary>
public enum IntegrationShell
{
    /// <summary>PowerShell (Core و Windows PowerShell).</summary>
    PowerShell,

    /// <summary>bash.</summary>
    Bash,

    /// <summary>zsh.</summary>
    Zsh,
}

/// <summary>خطاف تكامل جاهز للحقن: نصّه، مسار ملفّ البروفايل، وعلامة الحدّ لتفادي التكرار.</summary>
/// <param name="Shell">الصدفة المستهدفة.</param>
/// <param name="ProfilePath">المسار المتوقَّع لملفّ البروفايل (قد لا يكون موجوداً بعد).</param>
/// <param name="Hook">نصّ الخطاف كاملاً.</param>
/// <param name="Marker">سطر يميّز الخطاف — إن وُجد في البروفايل فالتكامل مثبَّت أصلاً.</param>
public sealed record ShellIntegrationHook(IntegrationShell Shell, string ProfilePath, string Hook, string Marker);

/// <summary>
/// يولّد خطافات <b>OSC 133</b> لكلّ صدفة. المحرّك يحلّل A/B/C/D فعلاً؛ ما تحتاجه الأصداف هو أن
/// <b>تطبع</b> هذه العلامات حول كلّ prompt وأمر وخرج ورمز خروج — وهذا ما تفعله هذه الخطافات.
///
/// <para><b>لماذا خطاف لا إعداد يدويّ:</b> بدون هذه العلامات لا يعرف المحرّك أين يبدأ أمرٌ وأين
/// ينتهي ولا رمز خروجه، فتُعطَّل «أصلح آخر فاشل» وعمود النجاح/الفشل في قاعدة التعلّم. الحقن
/// يُفعّلها لكلّ المستخدمين تلقائيّاً بدل تهيئة يدويّة لكلّ جهاز.</para>
///
/// <para>النصوص ثابتة ومعروفة، فالمستخدم يرى diff حرفيّاً قبل أيّ كتابة، ونحفظ نسخة احتياطية
/// من البروفايل، ولا نكتب إلّا بتأكيد صريح.</para>
/// </summary>
public static class ShellIntegrationScripts
{
    /// <summary>علامة مشتركة تُحيط بالكتلة المحقونة في كلّ الأصداف.</summary>
    public const string BeginMarker = "# >>> TerminalLauncher shell integration >>>";
    private const string EndMarker = "# <<< TerminalLauncher shell integration <<<";

    /// <summary>
    /// PowerShell: نعيد تعريف <c>prompt</c> لطباعة OSC 133 A/B، ونستعمل
    /// <c>Set-PSReadLineKeyHandler</c> على Enter لطباعة C وبدء الأمر، وحدثاً بعد كلّ أمر لطباعة
    /// D مع <c>$LASTEXITCODE</c>.
    /// </summary>
    private const string PowerShellHook =
        BeginMarker + "\n" +
        "function global:__tl_osc133_prompt {\n" +
        "  $code = if ($?) { 0 } else { if ($LASTEXITCODE) { $LASTEXITCODE } else { 1 } }\n" +
        "  \"$([char]27)]133;D;$code$([char]7)$([char]27)]133;A$([char]7)\"\n" +
        "}\n" +
        "$global:__tl_orig_prompt = $function:prompt\n" +
        "function global:prompt {\n" +
        "  $osc = __tl_osc133_prompt\n" +
        "  $base = & $global:__tl_orig_prompt\n" +
        "  \"$osc$base$([char]27)]133;B$([char]7)\"\n" +
        "}\n" +
        "if (Get-Module -ListAvailable PSReadLine) {\n" +
        "  Set-PSReadLineKeyHandler -Key Enter -ScriptBlock {\n" +
        "    [Microsoft.PowerShell.PSConsoleReadLine]::AddLine()\n" +
        "    [Console]::Write(\"$([char]27)]133;C$([char]7)\")\n" +
        "  }\n" +
        "}\n" +
        EndMarker;

    /// <summary>
    /// bash: نستعمل <c>PROMPT_COMMAND</c> لطباعة D مع <c>$?</c> ثمّ A، و<c>PS0</c> لطباعة C عند
    /// بدء تنفيذ الأمر، ونُلحق B بنهاية <c>PS1</c>.
    /// </summary>
    private const string BashHook =
        BeginMarker + "\n" +
        "__tl_osc133_precmd() {\n" +
        "  local code=$?\n" +
        "  printf '\\033]133;D;%s\\007\\033]133;A\\007' \"$code\"\n" +
        "}\n" +
        "case \"$PROMPT_COMMAND\" in\n" +
        "  *__tl_osc133_precmd*) ;;\n" +
        "  *) PROMPT_COMMAND=\"__tl_osc133_precmd;${PROMPT_COMMAND}\" ;;\n" +
        "esac\n" +
        "PS0=$'\\033]133;C\\007'\"${PS0}\"\n" +
        "PS1=\"${PS1}\"$'\\033]133;B\\007'\n" +
        EndMarker;

    /// <summary>
    /// zsh: نستعمل خطّافي <c>precmd</c> و<c>preexec</c> لطباعة D/A و C على الترتيب — الآليّة
    /// الأنظف في zsh وأقربها لدلالة OSC 133.
    /// </summary>
    private const string ZshHook =
        BeginMarker + "\n" +
        "__tl_osc133_precmd() {\n" +
        "  printf '\\033]133;D;%s\\007\\033]133;A\\007' \"$?\"\n" +
        "}\n" +
        "__tl_osc133_preexec() {\n" +
        "  printf '\\033]133;C\\007'\n" +
        "}\n" +
        "autoload -Uz add-zsh-hook 2>/dev/null && {\n" +
        "  add-zsh-hook precmd __tl_osc133_precmd\n" +
        "  add-zsh-hook preexec __tl_osc133_preexec\n" +
        "}\n" +
        EndMarker;

    /// <summary>يبني خطاف صدفة مع مسار بروفايلها الافتراضيّ.</summary>
    public static ShellIntegrationHook For(IntegrationShell shell) => shell switch
    {
        IntegrationShell.PowerShell => new(shell, PowerShellProfilePath(), PowerShellHook, BeginMarker),
        IntegrationShell.Bash => new(shell, HomeFile(".bashrc"), BashHook, BeginMarker),
        IntegrationShell.Zsh => new(shell, HomeFile(".zshrc"), ZshHook, BeginMarker),
        _ => throw new ArgumentOutOfRangeException(nameof(shell)),
    };

    /// <summary>
    /// يستنتج الصدفة من اسم بروفايل الصدفة المختار في التبويب. يعيد null لما لا نعرف كيف نحقن فيه
    /// (cmd، حاويات، اتّصالات SSH) — لا نخمّن ملفّ بروفايل قد لا يوجد.
    /// </summary>
    public static IntegrationShell? Detect(string? shellName)
    {
        if (string.IsNullOrWhiteSpace(shellName)) return null;
        string s = shellName.ToLowerInvariant();

        if (s.Contains("powershell") || s.Contains("pwsh")) return IntegrationShell.PowerShell;
        if (s.Contains("zsh")) return IntegrationShell.Zsh;
        if (s.Contains("bash") || s.Contains("git bash") || s.Contains("wsl")) return IntegrationShell.Bash;
        return null;
    }

    private static string PowerShellProfilePath()
    {
        // مسار بروفايل PowerShell Core لكلّ المستضيفات (Documents\PowerShell\...).
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return System.IO.Path.Combine(documents, "PowerShell", "Microsoft.PowerShell_profile.ps1");
    }

    private static string HomeFile(string name)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(home, name);
    }
}
