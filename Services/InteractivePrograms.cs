using System;
using System.Collections.Generic;
using System.IO;

namespace TerminalLauncher.Services;

/// <summary>
/// البرامج التفاعليّة التي تملك الشاشة وتقرأ الإدخال بنفسها: وكلاء الذكاء (claude · codex …)،
/// المحرّرات (vim · nano)، الصَدَفات البعيدة (ssh)، المفسّرات التفاعليّة (python · node)، وأدوات
/// ملء الشاشة (top · less).
///
/// <para><b>لماذا قائمة لا استدلال:</b> صندوق التأليف كان يختفي عند كلّ أمر يعمل — يستنتج من شكل
/// الشاشة أنّ «الموجّه ليس جاهزاً». فكان يختفي أثناء البناء والاختبار أيضاً، فلا يبقى مكانٌ لكتابة
/// شيء ولا سبيلٌ لإيقاف التنفيذ بـ<c>Ctrl+C</c> من الصندوق. القاعدة الآن معكوسة: الصندوق يبقى
/// ظاهراً افتراضاً، ولا يختفي إلّا للشاشة البديلة أو لبرنامج <b>معروف</b> أنّه يقرأ كلّ ضغطة مفتاح.</para>
/// </summary>
public static class InteractivePrograms
{
    /// <summary>القائمة الافتراضيّة — تُستعمَل حين لا يكتب المستخدم قائمته الخاصّة.</summary>
    public const string Defaults =
        "claude, codex, gemini, aider, cursor-agent, opencode, ollama, " +
        "vim, nvim, vi, nano, emacs, micro, " +
        "ssh, mosh, telnet, tmux, screen, " +
        "python, python3, ipython, node, irb, psql, mysql, sqlite3, redis-cli, " +
        "top, htop, btop, less, more, man, lazygit, gitui, lazydocker, k9s, ranger, mc";

    /// <summary>
    /// قائمة المستخدم من الإعدادات (فارغة = الافتراضيّة). يضبطها المُضيف عند تحميل الإعدادات وعند
    /// كلّ حفظ. ساكنة لأنّ الإعداد يخصّ التطبيق كلّه لا تبويباً بعينه.
    /// </summary>
    public static string UserList
    {
        get => _userList;
        set { _userList = value ?? ""; _cache = null; }
    }

    private static string _userList = "";
    private static (string Source, HashSet<string> Names)? _cache;

    /// <summary>
    /// هل هذا السطر يشغّل برنامجاً تفاعليّاً؟ يتخطّى الأغلفة (<c>sudo</c> · <c>npx</c> …) ويجرّد
    /// المسار واللاحقة، فـ<c>sudo /usr/bin/vim.exe file</c> يُعرَف كـ<c>vim</c>.
    /// </summary>
    public static bool IsInteractive(string? commandLine)
    {
        string? name = ProgramName(commandLine);
        return name is not null && Names().Contains(name);
    }

    /// <summary>هل أيّ سطر في المجموعة يشغّل برنامجاً تفاعليّاً؟ (اسم مستعار يوسَّع إلى عدّة أوامر).</summary>
    public static bool AnyInteractive(IEnumerable<string> commandLines)
    {
        foreach (string line in commandLines)
            if (IsInteractive(line)) return true;
        return false;
    }

    /// <summary>اسم البرنامج المشغَّل في السطر (بلا مسار ولا لاحقة، حروف صغيرة)، أو null.</summary>
    private static string? ProgramName(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;

        foreach (string token in commandLine.Split(' ', '\t'))
        {
            string word = token.Trim().Trim('"', '\'');
            if (word.Length == 0) continue;

            // ‏VAR=value في أوّل السطر ضبطُ بيئة لا برنامج، والخيارات ليست اسم البرنامج.
            if (word.StartsWith('-') || word.StartsWith('/') && word.Length <= 3) continue;
            if (word.Contains('=') && !word.Contains('\\') && !word.Contains('/')) continue;

            string name;
            try { name = Path.GetFileNameWithoutExtension(word); }
            catch (ArgumentException) { name = word; }
            if (name.Length == 0) continue;

            name = name.ToLowerInvariant();
            if (IsWrapper(name)) continue;   // sudo/npx/… يشغّلان أمراً آخر — الاسم في التوكِن التالي
            return name;
        }

        return null;
    }

    /// <summary>أغلفة تُنفِّذ أمراً آخر — يُتخطّى اسمها لبلوغ البرنامج الحقيقيّ.</summary>
    private static bool IsWrapper(string name) =>
        name is "sudo" or "doas" or "env" or "time" or "nohup" or "xargs" or "npx" or "bunx" or "pnpx"
             or "winpty" or "cmd" or "start" or "uv" or "uvx" or "poetry" or "pipx";

    /// <summary>أسماء القائمة الفعّالة، مبنيّة مرّة لكلّ نصّ مصدر (تُقرأ عند كلّ أمر يُرسَل).</summary>
    private static HashSet<string> Names()
    {
        string source = _userList.Trim().Length > 0 ? _userList : Defaults;
        if (_cache is { } c && c.Source == source) return c.Names;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in source.Split(new[] { ',', ';', '\n', '\r', ' ', '\t' },
                                            StringSplitOptions.RemoveEmptyEntries))
        {
            string name = raw.Trim().Trim('"', '\'');
            if (name.Length > 0) names.Add(name);
        }

        _cache = (source, names);
        return names;
    }
}
