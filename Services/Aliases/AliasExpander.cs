using System;
using System.Collections.Generic;
using System.Text;

namespace TerminalLauncher.Services.Aliases;

/// <summary>
/// نتيجة توسيع اسم مستعار: إمّا أوامر جاهزة، وإمّا سبب المنع.
/// </summary>
/// <param name="Commands">الأوامر بعد استبدال المتغيّرات (فارغة عند الخطأ).</param>
/// <param name="MissingVariable">اسم المتغيّر الإلزاميّ الناقص، أو null.</param>
public sealed record AliasExpansion(IReadOnlyList<string> Commands, string? MissingVariable)
{
    /// <summary>هل التوسيع صالح للتنفيذ؟</summary>
    public bool Ok => MissingVariable is null && Commands.Count > 0;
}

/// <summary>
/// يوسّع سطراً مكتوباً في صندوق الأوامر إلى أوامر الاسم المستعار.
///
/// <para><b>الاستبدال محافظ عمداً:</b> لا يُستبدل إلّا <c>$name</c> لمتغيّر <b>معرَّف فعلاً</b>،
/// و<c>$1..$9</c>، و<c>$@</c>. أيّ <c>$</c> أخرى تبقى كما هي — فـ<c>$env:PATH</c> و<c>$PWD</c>
/// و<c>$(git rev-parse)</c> أوامرُ صدفة صحيحة، واستبدالها بالفراغ يفسد الأمر بصمت.</para>
/// </summary>
public static class AliasExpander
{
    /// <summary>
    /// يقسّم سطر الاستدعاء إلى وسائط، محترماً الاقتباس المزدوج والمفرد. الاقتباس يُزال من الناتج:
    /// <c>gpc "first commit"</c> ⇐ وسيط واحد نصّه <c>first commit</c>.
    /// </summary>
    public static List<string> SplitArguments(string? input)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return args;

        var current = new StringBuilder();
        char quote = '\0';
        bool started = false;

        foreach (char c in input)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
                continue;
            }

            if (c is '"' or '\'') { quote = c; started = true; continue; }

            if (char.IsWhiteSpace(c))
            {
                if (started || current.Length > 0) { args.Add(current.ToString()); current.Clear(); started = false; }
                continue;
            }

            current.Append(c);
        }

        if (started || current.Length > 0) args.Add(current.ToString());
        return args;
    }

    /// <summary>
    /// اسم الاسم المستعار المكتوب في أوّل السطر، أو فارغ. لا يقتطع شيئاً من السطر — المستدعي
    /// هو من يقرّر إن كان الاسم معروفاً.
    /// </summary>
    public static string HeadWord(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "";

        string trimmed = line.TrimStart();
        int space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }

    /// <summary>
    /// يوسّع الاسم المستعار بوسائط سطر الاستدعاء (بلا اسم الاسم المستعار نفسه).
    /// </summary>
    public static AliasExpansion Expand(CommandAlias alias, IReadOnlyList<string> args)
    {
        if (alias is null) throw new ArgumentNullException(nameof(alias));

        // ربط المتغيّرات بالوسائط بترتيب التعريف؛ ما زاد عن عددها يبقى متاحاً عبر $@ و$N.
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < alias.Variables.Count; i++)
        {
            AliasVariable variable = alias.Variables[i];
            if (variable.Name.Length == 0) continue;

            string value = i < args.Count ? args[i] : variable.Default;
            if (value.Length == 0 && variable.Required)
                return new AliasExpansion(Array.Empty<string>(), variable.Name);

            values[variable.Name] = value;
        }

        var commands = new List<string>();
        foreach (string raw in alias.Commands)
        {
            string command = Substitute(raw, values, args).Trim();
            if (command.Length > 0) commands.Add(command);
        }

        return new AliasExpansion(commands, null);
    }

    /// <summary>يستبدل المتغيّرات المعرَّفة والمواضع في نصّ أمر واحد.</summary>
    private static string Substitute(string command, Dictionary<string, string> values, IReadOnlyList<string> args)
    {
        var sb = new StringBuilder(command.Length);

        for (int i = 0; i < command.Length; i++)
        {
            if (command[i] != '$') { sb.Append(command[i]); continue; }

            // ‎${name}‎ — الشكل الصريح، يفصل الاسم عمّا يليه من حروف.
            if (i + 1 < command.Length && command[i + 1] == '{')
            {
                int close = command.IndexOf('}', i + 2);
                string braced = close > 0 ? command[(i + 2)..close] : "";
                if (close > 0 && values.TryGetValue(braced, out string? bracedValue))
                {
                    sb.Append(bracedValue);
                    i = close;
                    continue;
                }
            }

            // ‎$@‎ — كلّ الوسائط مفصولة بمسافة.
            if (i + 1 < command.Length && command[i + 1] == '@')
            {
                sb.Append(string.Join(' ', args));
                i++;
                continue;
            }

            // ‎$1..$9‎ — وسيط بموضعه.
            if (i + 1 < command.Length && command[i + 1] is >= '1' and <= '9')
            {
                int index = command[i + 1] - '1';
                sb.Append(index < args.Count ? args[index] : "");
                i++;
                continue;
            }

            // ‎$name‎ — لمتغيّر معرَّف فقط؛ وإلّا يبقى النصّ كما هو.
            int end = i + 1;
            while (end < command.Length && (char.IsLetterOrDigit(command[end]) || command[end] == '_')) end++;

            string name = command[(i + 1)..end];
            if (name.Length > 0 && values.TryGetValue(name, out string? value))
            {
                sb.Append(value);
                i = end - 1;
                continue;
            }

            sb.Append('$');
        }

        return sb.ToString();
    }
}
