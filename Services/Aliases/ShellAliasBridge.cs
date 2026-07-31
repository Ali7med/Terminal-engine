using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TerminalLauncher.Services.Aliases;

/// <summary>
/// يسجّل الأسماء المستعارة في <b>الصدفة الحقيقيّة</b>، فتعمل حتّى حين يُكتب الاسم داخل شبكة
/// التيرمنال مباشرةً لا في صندوق الإدخال (وداخل أيّ أداة تُشغِّل الصدفة نفسها).
///
/// <para><b>كيف:</b> تُولَّد ملفّات تهيئة صغيرة عند الإقلاع وعند كلّ حفظ/حذف، ثمّ يُضاف إلى سطر
/// تشغيل الصدفة ما يُحمِّلها: <c>doskey /macrofile</c> لـcmd، ودَوطُ سكربت لـPowerShell،
/// و<c>--rcfile</c> لـbash. لا يُلمَس ملفُّ تهيئة المستخدم إطلاقاً: التسجيل يخصّ الجلسات التي
/// يفتحها هذا التطبيق وحدها، فإزالة الأداة لا تترك أثراً في نظام المستخدم.</para>
///
/// <para><b>حدود معروفة:</b> طبقة التطبيق (<c>TryRunAlias</c>) تبقى المسار الأغنى — فيها التحقّق
/// من المتغيّرات الإلزاميّة وحوارُ التأكيد. الصدفة لا تعرف الاثنين، لذا:
/// <list type="bullet">
/// <item>الاسم الذي يطلب تأكيداً قبل التنفيذ <b>لا يُسجَّل</b> في الصدفة — تسجيله يتجاوز حمايةً
/// طلبها المستخدم بنفسه.</item>
/// <item>القيم الافتراضيّة تُترجَم لـbash وPowerShell فقط؛ <c>doskey</c> لا يدعمها.</item>
/// <item>WSL والبروفايلات المخصّصة خارج التغطية: سطر تشغيلها ليس تحت سيطرتنا.</item>
/// </list></para>
/// </summary>
public static class ShellAliasBridge
{
    /// <summary>مجلّد ملفّات التهيئة المولَّدة (بجوار <c>aliases.json</c>).</summary>
    public static string Folder { get; } = Path.Combine(
        Path.GetDirectoryName(AliasStore.DefaultPath) ?? Path.GetTempPath(), "shell");

    private static string CmdFile => Path.Combine(Folder, "aliases.doskey");
    private static string PwshFile => Path.Combine(Folder, "aliases.ps1");
    private static string BashFile => Path.Combine(Folder, "aliases.sh");

    /// <summary>التسجيل في الصدفة مفعَّل (يضبطه المُضيف من الإعدادات).</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>هل وُلِّدت الملفّات في هذه الجلسة فعلاً؟ (بلا توليد ناجح لا نلمس سطر التشغيل.)</summary>
    private static bool _generated;

    /// <summary>
    /// يعيد توليد ملفّات التهيئة من المخزن. يُستدعى عند الإقلاع وبعد كلّ حفظ/حذف اسم مستعار.
    /// التيرمنالات المفتوحة تبقى على القائمة القديمة — الملفّ يُقرأ عند بدء الصدفة لا بعده.
    /// </summary>
    public static void Refresh()
    {
        _generated = false;
        if (!Enabled) return;

        try
        {
            Directory.CreateDirectory(Folder);
            IReadOnlyList<CommandAlias> all = AliasStore.Shared.All();

            // ‏Windows PowerShell 5.1 يقرأ .ps1 بترميز النظام ما لم يجد BOM، فأيّ حرف غير لاتينيّ
            // في أمرٍ (رسالة كومِت عربيّة مثلاً) يصل مشوّهاً. bash بالمقابل يختنق بالـBOM.
            WriteAtomic(CmdFile, BuildDoskey(all), bom: false);
            WriteAtomic(PwshFile, BuildPowerShell(all), bom: true);
            WriteAtomic(BashFile, BuildBash(all), bom: false);
            _generated = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // تعذّر التوليد ⇒ نبقى على طبقة التطبيق وحدها؛ لا يجوز أن يمنع هذا فتح تيرمنال.
        }
    }

    /// <summary>
    /// سطر تشغيل الصدفة بعد إضافة ما يُحمّل الأسماء المستعارة — أو السطر كما هو إن كانت الصدفة
    /// خارج التغطية (WSL، بروفايل مخصّص، أو سطرٌ يحمل أصلاً أمر تشغيل خاصّاً به).
    /// </summary>
    public static string Decorate(string commandLine)
    {
        if (!Enabled || !_generated || string.IsNullOrWhiteSpace(commandLine)) return commandLine;

        try
        {
            return Kind(commandLine) switch
            {
                // ‏/K و-Command يبتلعان بقيّة السطر، فمكانهما الآخِر.
                ShellKind.Cmd  => $"{commandLine} /K doskey /macrofile=\"{CmdFile}\"",
                ShellKind.Pwsh => $"{commandLine} -NoExit -Command \". '{PwshFile.Replace("'", "''")}'\"",
                // ‏bash بالعكس: خياراته الطويلة تسبق القصيرة («bash [GNU long option] [option] …»)،
                // فإلحاق ‎--rcfile‎ بعد ‎-i‎ يجعله يقرؤه ‎--‎ ويرفض السطر كلّه بـ«‎--: invalid option».
                ShellKind.Bash => InsertAfterExe(commandLine, $"--rcfile \"{ToPosix(BashFile)}\""),
                _              => commandLine,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return commandLine;
        }
    }

    private enum ShellKind { Unsupported, Cmd, Pwsh, Bash }

    /// <summary>
    /// نوع الصدفة المستنتَج من سطر التشغيل — و<b>يُرفَض</b> السطر الذي يحمل أمر تشغيل خاصّاً به:
    /// إضافةُ <c>/K</c> فوق <c>/C</c> قائم، أو <c>-Command</c> فوق <c>-File</c>، تكسر أمراً كتبه
    /// المستخدم بنفسه في بروفايل مخصّص.
    /// </summary>
    private static ShellKind Kind(string commandLine)
    {
        string line = commandLine.ToLowerInvariant();

        // WSL يُشغّل صدفة داخل التوزيعة بمسارات مختلفة ولا يقبل وسائط bash — خارج التغطية.
        if (line.Contains("wsl.exe") || line.Contains("wsl ")) return ShellKind.Unsupported;

        if (line.Contains("cmd.exe") || line.EndsWith("cmd"))
            return HasAny(line, " /c", " /k") ? ShellKind.Unsupported : ShellKind.Cmd;

        if (line.Contains("powershell") || line.Contains("pwsh"))
            return HasAny(line, " -command", " -c ", " -file", " -f ", " -encodedcommand", " -noexit")
                ? ShellKind.Unsupported : ShellKind.Pwsh;

        if (line.Contains("bash") || line.Contains("zsh") || line.Contains("sh.exe"))
            return HasAny(line, "--rcfile", "--init-file", " -c ", " -l", " --login")
                ? ShellKind.Unsupported : ShellKind.Bash;

        return ShellKind.Unsupported;
    }

    private static bool HasAny(string line, params string[] needles)
    {
        foreach (string n in needles)
            if (line.Contains(n, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// يُدخل وسيطاً مباشرةً بعد الملفّ التنفيذيّ وقبل وسائط البروفايل — لِما يشترط موضعاً مبكّراً
    /// (خيارات bash الطويلة). يحترم اقتباس المسار: <c>"C:\Program Files\…\bash.exe" -i</c> اسمُه
    /// التنفيذيّ ينتهي عند علامة الاقتباس المغلِقة لا عند أوّل مسافة.
    /// </summary>
    private static string InsertAfterExe(string commandLine, string argument)
    {
        string line = commandLine.TrimStart();
        int end = line.StartsWith('"') ? line.IndexOf('"', 1) + 1 : line.IndexOf(' ');
        if (end <= 0) return $"{line} {argument}";   // الملفّ التنفيذيّ وحده بلا وسائط

        return $"{line[..end]} {argument}{line[end..]}";
    }

    /// <summary>‏<c>C:\Users\x\a.sh</c> ⇒ <c>/c/Users/x/a.sh</c> — الصيغة التي يفهمها bash التابع لـGit.</summary>
    private static string ToPosix(string windowsPath)
    {
        string p = windowsPath.Replace('\\', '/');
        if (p.Length > 2 && p[1] == ':' && p[2] == '/')
            p = "/" + char.ToLowerInvariant(p[0]) + p[2..];
        return p;
    }

    // ===== التوليد =====

    /// <summary>هل يُسجَّل هذا الاسم في الصدفة؟ (المعطَّل لا، وطالبُ التأكيد لا — انظر ملاحظة الصنف.)</summary>
    private static bool Registrable(CommandAlias alias, string shellTag) =>
        alias.Enabled
        && !alias.ConfirmBeforeRun
        && alias.Name.Length > 0
        && alias.Commands.Count > 0
        && IsSafeName(alias.Name)
        && (alias.Shell.Length == 0
            || shellTag.Contains(alias.Shell, StringComparison.OrdinalIgnoreCase));

    /// <summary>اسمٌ صالحٌ لدالّة/ماكرو صدفة: حروف وأرقام وشرطة وشرطة سفليّة، ويبدأ بحرف.</summary>
    private static bool IsSafeName(string name)
    {
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        foreach (char c in name)
            if (!char.IsLetterOrDigit(c) && c is not ('_' or '-')) return false;
        return true;
    }

    /// <summary>ترويسة الملفّات المولَّدة — بالإنجليزيّة عمداً: تُقرأ بترميز الصدفة لا بترميز الواجهة.</summary>
    private const string Header =
        "Generated by TerminalLauncher - do not edit, rebuilt on every save.";

    /// <summary>
    /// ماكروهات <c>doskey</c>: سطرٌ لكلّ اسم، والخطوات مفصولة بـ<c>$T</c>.
    ///
    /// <para><b>بلا ترويسة:</b> صيغة ملفّ الماكرو <c>name=text</c> حصراً ولا تعرف أسطر التعليق،
    /// فسطرُ ترويسةٍ يصير ماكرو مشوّهاً. ولا قيم افتراضيّة — doskey لا يعرفها، فالوسيط الغائب
    /// يصل فارغاً.</para>
    /// </summary>
    private static string BuildDoskey(IReadOnlyList<CommandAlias> aliases)
    {
        var sb = new StringBuilder();

        foreach (CommandAlias alias in aliases)
        {
            if (!Registrable(alias, "cmd")) continue;

            var steps = new List<string>();
            foreach (string command in alias.Commands)
            {
                string t = Translate(command, alias,
                    positional: (i, _) => i < 9 ? "$" + (i + 1) : "",
                    all: "$*",
                    dollar: "$$");
                if (t.Trim().Length > 0) steps.Add(t.Trim());
            }

            if (steps.Count > 0) sb.Append(alias.Name).Append('=').Append(string.Join(" $T ", steps)).Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// دوالّ PowerShell. يُزال أيّ اسمٍ مستعار مدمج يحمل الكلمة نفسها أوّلاً: ترتيب البحث في
    /// PowerShell يضع الأسماء المستعارة قبل الدوالّ، فبدون الإزالة يفوز <c>ls</c> المدمج على دالّتنا.
    /// </summary>
    private static string BuildPowerShell(IReadOnlyList<CommandAlias> aliases)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(Header).Append("\r\n");
        sb.Append("function __tl_arg($a, $i, $d) { if ($i -lt $a.Count -and \"$($a[$i])\") { $a[$i] } else { $d } }\r\n\r\n");

        foreach (CommandAlias alias in aliases)
        {
            if (!Registrable(alias, "powershell pwsh")) continue;

            sb.Append("Remove-Item \"alias:").Append(alias.Name)
              .Append("\" -Force -ErrorAction SilentlyContinue\r\n");
            sb.Append("function ").Append(alias.Name).Append(" {\r\n");
            sb.Append("    $__a = $args\r\n");

            foreach (string command in alias.Commands)
            {
                string t = Translate(command, alias,
                    positional: (i, def) => def.Length > 0
                        ? $"$(__tl_arg $__a {i} '{def.Replace("'", "''")}')"
                        : $"$($__a[{i}])",
                    all: "$($__a -join ' ')",
                    dollar: "$");
                if (t.Trim().Length > 0) sb.Append("    ").Append(t.Trim()).Append("\r\n");
            }

            sb.Append("}\r\n\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// دوالّ bash داخل ملفّ يُمرَّر بـ<c>--rcfile</c>. يبدأ بدَوط <c>~/.bashrc</c> لأنّ
    /// <c>--rcfile</c> يحلّ محلّه لا يُضاف إليه — بدونه يفقد المستخدم كلّ تهيئته.
    /// </summary>
    private static string BuildBash(IReadOnlyList<CommandAlias> aliases)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(Header).Append('\n');
        sb.Append("[ -f ~/.bashrc ] && . ~/.bashrc\n\n");

        foreach (CommandAlias alias in aliases)
        {
            if (!Registrable(alias, "bash zsh sh")) continue;

            sb.Append(alias.Name).Append("() {\n");

            foreach (string command in alias.Commands)
            {
                string t = Translate(command, alias,
                    positional: (i, def) => def.Length > 0
                        ? "${" + (i + 1) + ":-" + def.Replace("\"", "\\\"") + "}"
                        : "${" + (i + 1) + "}",
                    all: "\"$@\"",
                    dollar: "$");
                if (t.Trim().Length > 0) sb.Append("  ").Append(t.Trim()).Append('\n');
            }

            sb.Append("}\n\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// يترجم أمراً واحداً إلى صياغة الصدفة الهدف: <c>$name</c>/<c>${name}</c> لمتغيّر <b>معرَّف</b>
    /// ⇒ وسيطه بموضعه، <c>$@</c> ⇒ كلّ الوسائط، <c>$1..$9</c> ⇒ وسيط بموضعه. أيّ <c>$</c> أخرى
    /// تبقى كما هي (بعد تهريبها إن لزم): <c>$env:PATH</c> و<c>$PWD</c> أوامرُ صدفة صحيحة.
    ///
    /// <para>نسخةٌ موازية لـ<see cref="AliasExpander"/>: تلك تستبدل بالقيم الفعليّة وقت التنفيذ،
    /// وهذه تستبدل بمواضع الصدفة وقت التوليد.</para>
    /// </summary>
    private static string Translate(
        string command, CommandAlias alias,
        Func<int, string, string> positional, string all, string dollar)
    {
        // اسم المتغيّر ← موضعه وقيمته الافتراضيّة.
        var slots = new Dictionary<string, (int Index, string Default)>(StringComparer.Ordinal);
        for (int i = 0; i < alias.Variables.Count; i++)
        {
            AliasVariable v = alias.Variables[i];
            if (v.Name.Length > 0 && !slots.ContainsKey(v.Name)) slots[v.Name] = (i, v.Default);
        }

        var sb = new StringBuilder(command.Length);

        for (int i = 0; i < command.Length; i++)
        {
            if (command[i] != '$') { sb.Append(command[i]); continue; }

            // ‎${name}‎ — الشكل الصريح.
            if (i + 1 < command.Length && command[i + 1] == '{')
            {
                int close = command.IndexOf('}', i + 2);
                if (close > 0 && slots.TryGetValue(command[(i + 2)..close], out var braced))
                {
                    sb.Append(positional(braced.Index, braced.Default));
                    i = close;
                    continue;
                }
            }

            // ‎$@‎ — كلّ الوسائط.
            if (i + 1 < command.Length && command[i + 1] == '@')
            {
                sb.Append(all);
                i++;
                continue;
            }

            // ‎$1..$9‎ — وسيط بموضعه (بلا افتراضيّ: لا اسم له يحمله).
            if (i + 1 < command.Length && command[i + 1] is >= '1' and <= '9')
            {
                sb.Append(positional(command[i + 1] - '1', ""));
                i++;
                continue;
            }

            // ‎$name‎ لمتغيّر معرَّف فقط.
            int end = i + 1;
            while (end < command.Length && (char.IsLetterOrDigit(command[end]) || command[end] == '_')) end++;

            if (end > i + 1 && slots.TryGetValue(command[(i + 1)..end], out var named))
            {
                sb.Append(positional(named.Index, named.Default));
                i = end - 1;
                continue;
            }

            sb.Append(dollar);   // ‏$ لا تخصّنا — تبقى للصدفة (مهرَّبة حيث تحتاج doskey)
        }

        return sb.ToString();
    }

    /// <summary>كتابة ذرّيّة: انقطاعٌ أثناء الحفظ لا يجوز أن يترك ملفّ تهيئة نصفه أوامر مبتورة.</summary>
    private static void WriteAtomic(string path, string content, bool bom)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom));
        File.Move(temp, path, overwrite: true);
    }
}
