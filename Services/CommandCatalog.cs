using System;
using System.Collections.Generic;

namespace TerminalLauncher.Services;

/// <summary>نوع الوسيط الذي يقبله الأمر — يحدّد ما تعرضه قائمة الاقتراحات بعد كتابة الأمر.</summary>
public enum ArgKind
{
    /// <summary>لا وسائط ذات معنى (اقتراحات الخيارات/التاريخ فقط).</summary>
    None,
    /// <summary>مجلدات فقط (cd · mkdir · pushd …).</summary>
    Directory,
    /// <summary>ملفات فقط (cat · vim · node …) — مع المجلدات كوسيط عبور.</summary>
    File,
    /// <summary>ملفات ومجلدات (cp · mv · git add …).</summary>
    Any,
    /// <summary>فروع/وسوم git (checkout · merge · rebase …).</summary>
    GitRef,
    /// <summary>سكربتات package.json (npm run · pnpm run …).</summary>
    NpmScript,
    /// <summary>اسم أمر آخر (sudo · which · man · time …).</summary>
    Command,
    /// <summary>عمليّات قيد التشغيل (kill · taskkill · Stop-Process).</summary>
    Process,
}

/// <summary>وصف ثنائي اللغة لعنصر في الكتالوج.</summary>
public readonly record struct Bi(string Ar, string En)
{
    public string Text => Loc.Current == AppLang.En ? En : Ar;
    public override string ToString() => Text;
}

/// <summary>
/// مواصفة أمر: اسمه، وصفه، نوع وسيطه الافتراضيّ، أوامره الفرعيّة (إن كان أمراً مركَّباً مثل git)،
/// وخياراته الشائعة. <see cref="SubArg"/> يتجاوز <see cref="Arg"/> لأمر فرعيّ بعينه
/// (مثلاً <c>git checkout</c> ⇒ فروع، بينما <c>git add</c> ⇒ ملفات).
/// </summary>
public sealed record CommandSpec(
    string Name,
    Bi Desc,
    ArgKind Arg = ArgKind.Any,
    string[]? Subs = null,
    string[]? Flags = null,
    Dictionary<string, ArgKind>? SubArg = null)
{
    /// <summary>نوع الوسيط الفعليّ بعد أمر فرعيّ (أو الافتراضيّ إن لم يُعرَّف له).</summary>
    public ArgKind ArgFor(string? sub)
        => sub != null && SubArg != null && SubArg.TryGetValue(sub, out var k) ? k : Arg;
}

/// <summary>
/// كتالوج الأوامر الافتراضيّة لكلّ عائلة صدفة (unix / powershell / cmd) + أدوات التطوير المشتركة
/// (git · npm · docker · dotnet …). مصدر الحقيقة الوحيد لِما يقترحه الصندوق: اسم الأمر عند كتابة
/// الكلمة الأولى، ثمّ الوسائط <b>المناسبة لعمل الأمر</b> (مجلدات لـ cd، فروع لـ git checkout …).
/// </summary>
public static class CommandCatalog
{
    private static Bi B(string ar, string en) => new(ar, en);

    private static Dictionary<string, CommandSpec> Index(IEnumerable<CommandSpec> specs)
    {
        var d = new Dictionary<string, CommandSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in specs) d[s.Name] = s;
        return d;
    }

    // ===== أوامر يونكس (bash / zsh / git-bash / wsl) =====
    private static readonly CommandSpec[] UnixSpecs =
    {
        new("cd",     B("انتقال إلى مجلد", "Change directory"),        ArgKind.Directory, Flags: new[]{ "-" , ".." }),
        new("ls",     B("سرد المحتويات", "List directory"),            ArgKind.Directory, Flags: new[]{ "-l", "-la", "-lh", "-a", "-R", "--color" }),
        new("pwd",    B("مسار المجلد الحاليّ", "Print working directory"), ArgKind.None),
        new("pushd",  B("دفع مجلد للمكدّس", "Push directory"),          ArgKind.Directory),
        new("popd",   B("سحب مجلد من المكدّس", "Pop directory"),        ArgKind.None),
        new("mkdir",  B("إنشاء مجلد", "Make directory"),               ArgKind.Directory, Flags: new[]{ "-p" }),
        new("rmdir",  B("حذف مجلد فارغ", "Remove empty directory"),    ArgKind.Directory),
        new("rm",     B("حذف", "Remove"),                              ArgKind.Any,  Flags: new[]{ "-r", "-f", "-rf", "-i", "-v" }),
        new("cp",     B("نسخ", "Copy"),                                ArgKind.Any,  Flags: new[]{ "-r", "-a", "-v", "-u" }),
        new("mv",     B("نقل/إعادة تسمية", "Move / rename"),           ArgKind.Any,  Flags: new[]{ "-v", "-i", "-n" }),
        new("touch",  B("إنشاء ملفّ فارغ", "Create empty file"),        ArgKind.File),
        new("cat",    B("عرض محتوى ملفّ", "Print file"),                ArgKind.File, Flags: new[]{ "-n" }),
        new("less",   B("تصفّح ملفّ", "Page through file"),             ArgKind.File),
        new("more",   B("تصفّح ملفّ", "Page through file"),             ArgKind.File),
        new("head",   B("أوّل الأسطر", "First lines"),                  ArgKind.File, Flags: new[]{ "-n" }),
        new("tail",   B("آخر الأسطر", "Last lines"),                    ArgKind.File, Flags: new[]{ "-n", "-f" }),
        new("nano",   B("محرّر نصّ", "Text editor"),                     ArgKind.File),
        new("vim",    B("محرّر نصّ", "Text editor"),                     ArgKind.File),
        new("vi",     B("محرّر نصّ", "Text editor"),                     ArgKind.File),
        new("nvim",   B("محرّر نصّ", "Text editor"),                     ArgKind.File),
        new("grep",   B("بحث في النصّ", "Search text"),                  ArgKind.Any,  Flags: new[]{ "-r", "-i", "-n", "-v", "-E", "-l", "-w" }),
        new("rg",     B("بحث سريع (ripgrep)", "Fast search"),           ArgKind.Any,  Flags: new[]{ "-i", "-n", "-l", "-g", "--type" }),
        new("find",   B("عثور على ملفات", "Find files"),                ArgKind.Directory, Flags: new[]{ "-name", "-type f", "-type d", "-maxdepth" }),
        new("sed",    B("تحرير تدفّقيّ", "Stream editor"),               ArgKind.File),
        new("awk",    B("معالجة نصّ", "Text processing"),                ArgKind.File),
        new("sort",   B("فرز الأسطر", "Sort lines"),                    ArgKind.File),
        new("uniq",   B("إزالة التكرار", "Unique lines"),               ArgKind.File),
        new("wc",     B("عدّ الأسطر/الكلمات", "Word count"),             ArgKind.File, Flags: new[]{ "-l", "-w", "-c" }),
        new("diff",   B("مقارنة ملفّين", "Diff files"),                  ArgKind.File, Flags: new[]{ "-u", "-r" }),
        new("chmod",  B("تغيير الصلاحيّات", "Change permissions"),       ArgKind.Any,  Flags: new[]{ "+x", "-R", "755", "644" }),
        new("chown",  B("تغيير المالك", "Change owner"),                ArgKind.Any,  Flags: new[]{ "-R" }),
        new("ln",     B("رابط رمزيّ", "Link"),                          ArgKind.Any,  Flags: new[]{ "-s" }),
        new("du",     B("حجم المجلد", "Disk usage"),                    ArgKind.Directory, Flags: new[]{ "-sh", "-h" }),
        new("df",     B("مساحة الأقراص", "Disk free"),                  ArgKind.None, Flags: new[]{ "-h" }),
        new("tar",    B("أرشفة", "Archive"),                            ArgKind.Any,  Flags: new[]{ "-xzf", "-czf", "-tf" }),
        new("zip",    B("ضغط", "Zip"),                                  ArgKind.Any,  Flags: new[]{ "-r" }),
        new("unzip",  B("فكّ الضغط", "Unzip"),                          ArgKind.File),
        new("which",  B("مسار أمر", "Locate command"),                  ArgKind.Command),
        new("man",    B("دليل أمر", "Manual page"),                     ArgKind.Command),
        new("time",   B("قياس زمن أمر", "Time a command"),              ArgKind.Command),
        new("sudo",   B("تنفيذ بصلاحيّات الجذر", "Run as root"),         ArgKind.Command),
        new("watch",  B("تكرار أمر دوريّاً", "Repeat command"),          ArgKind.Command, Flags: new[]{ "-n" }),
        new("xargs",  B("تمرير المدخلات كوسائط", "Build arg lists"),     ArgKind.Command),
        new("export", B("متغيّر بيئة", "Export env var"),                ArgKind.None),
        new("env",    B("متغيّرات البيئة", "Environment"),               ArgKind.None),
        new("source", B("تنفيذ سكربت في الصدفة", "Source script"),       ArgKind.File),
        new("echo",   B("طباعة نصّ", "Print text"),                      ArgKind.None),
        new("ps",     B("العمليّات", "Processes"),                       ArgKind.None, Flags: new[]{ "aux", "-ef" }),
        new("kill",   B("إنهاء عمليّة", "Kill process"),                 ArgKind.Process, Flags: new[]{ "-9" }),
        new("top",    B("مراقب العمليّات", "Process monitor"),           ArgKind.None),
        new("htop",   B("مراقب العمليّات", "Process monitor"),           ArgKind.None),
        new("curl",   B("طلب شبكة", "HTTP request"),                    ArgKind.None, Flags: new[]{ "-s", "-L", "-O", "-X POST", "-H", "-d" }),
        new("wget",   B("تنزيل ملفّ", "Download file"),                  ArgKind.None, Flags: new[]{ "-O", "-c" }),
        new("ssh",    B("اتّصال بعيد", "Remote shell"),                  ArgKind.None, Flags: new[]{ "-p", "-i" }),
        new("scp",    B("نسخ عبر ssh", "Copy over ssh"),                ArgKind.Any,  Flags: new[]{ "-r", "-P" }),
        new("rsync",  B("مزامنة ملفّات", "Sync files"),                  ArgKind.Any,  Flags: new[]{ "-avz", "--delete" }),
        new("clear",  B("تنظيف الشاشة", "Clear screen"),                ArgKind.None),
        new("history",B("سجلّ الأوامر", "Command history"),              ArgKind.None),
        new("exit",   B("خروج", "Exit"),                                ArgKind.None),
        new("open",   B("فتح بالتطبيق الافتراضيّ", "Open with default"), ArgKind.Any),
        new("code",   B("فتح في VS Code", "Open in VS Code"),           ArgKind.Any,  Flags: new[]{ ".", "-r", "-n" }),
    };

    // ===== أوامر PowerShell =====
    private static readonly CommandSpec[] PwshSpecs =
    {
        new("cd",              B("انتقال إلى مجلد", "Change directory"),   ArgKind.Directory),
        new("Set-Location",    B("انتقال إلى مجلد", "Change directory"),   ArgKind.Directory),
        new("ls",              B("سرد المحتويات", "List directory"),       ArgKind.Directory),
        new("dir",             B("سرد المحتويات", "List directory"),       ArgKind.Directory),
        new("Get-ChildItem",   B("سرد المحتويات", "List directory"),       ArgKind.Directory, Flags: new[]{ "-Recurse", "-Force", "-Filter", "-File", "-Directory" }),
        new("pwd",             B("المجلد الحاليّ", "Working directory"),    ArgKind.None),
        new("Get-Content",     B("عرض محتوى ملفّ", "Read file"),            ArgKind.File, Flags: new[]{ "-Tail", "-TotalCount", "-Wait", "-Raw" }),
        new("Set-Content",     B("كتابة ملفّ", "Write file"),               ArgKind.File, Flags: new[]{ "-Encoding utf8" }),
        new("Add-Content",     B("إلحاق بملفّ", "Append to file"),          ArgKind.File),
        new("New-Item",        B("إنشاء ملفّ/مجلد", "Create item"),         ArgKind.Any,  Flags: new[]{ "-ItemType Directory", "-ItemType File", "-Force" }),
        new("Remove-Item",     B("حذف", "Remove"),                         ArgKind.Any,  Flags: new[]{ "-Recurse", "-Force", "-Confirm:$false" }),
        new("Copy-Item",       B("نسخ", "Copy"),                           ArgKind.Any,  Flags: new[]{ "-Recurse", "-Force" }),
        new("Move-Item",       B("نقل", "Move"),                           ArgKind.Any,  Flags: new[]{ "-Force" }),
        new("Rename-Item",     B("إعادة تسمية", "Rename"),                 ArgKind.Any),
        new("Test-Path",       B("فحص وجود مسار", "Test path"),            ArgKind.Any),
        new("Select-String",   B("بحث في النصّ", "Search text"),            ArgKind.Any,  Flags: new[]{ "-Pattern", "-Path", "-CaseSensitive" }),
        new("Get-Process",     B("العمليّات", "Processes"),                 ArgKind.Process),
        new("Stop-Process",    B("إنهاء عمليّة", "Kill process"),           ArgKind.Process, Flags: new[]{ "-Name", "-Id", "-Force" }),
        new("Get-Service",     B("الخدمات", "Services"),                   ArgKind.None),
        new("Start-Process",   B("تشغيل برنامج", "Start process"),         ArgKind.Any),
        new("Invoke-WebRequest",B("طلب شبكة", "HTTP request"),             ArgKind.None, Flags: new[]{ "-Uri", "-Method", "-OutFile" }),
        new("Expand-Archive",  B("فكّ أرشيف", "Expand archive"),           ArgKind.File, Flags: new[]{ "-DestinationPath", "-Force" }),
        new("Compress-Archive",B("إنشاء أرشيف", "Compress archive"),       ArgKind.Any,  Flags: new[]{ "-DestinationPath", "-Force" }),
        new("Get-Command",     B("العثور على أمر", "Find command"),        ArgKind.Command),
        new("Get-Help",        B("مساعدة أمر", "Command help"),            ArgKind.Command, Flags: new[]{ "-Examples", "-Full" }),
        new("Import-Module",   B("تحميل وحدة", "Import module"),           ArgKind.None),
        new("Measure-Object",  B("إحصاء", "Measure"),                      ArgKind.None, Flags: new[]{ "-Line", "-Word", "-Sum" }),
        new("Select-Object",   B("انتقاء خصائص", "Select properties"),     ArgKind.None, Flags: new[]{ "-First", "-Last", "-Property" }),
        new("Where-Object",    B("تصفية", "Filter"),                       ArgKind.None),
        new("ForEach-Object",  B("تكرار", "Iterate"),                      ArgKind.None),
        new("ConvertFrom-Json",B("تحليل JSON", "Parse JSON"),              ArgKind.None),
        new("Clear-Host",      B("تنظيف الشاشة", "Clear screen"),          ArgKind.None),
        new("cls",             B("تنظيف الشاشة", "Clear screen"),          ArgKind.None),
        new("whoami",          B("المستخدم الحاليّ", "Current user"),       ArgKind.None),
        new("exit",            B("خروج", "Exit"),                          ArgKind.None),
        new("code",            B("فتح في VS Code", "Open in VS Code"),     ArgKind.Any,  Flags: new[]{ ".", "-r", "-n" }),
    };

    // ===== أوامر cmd.exe =====
    private static readonly CommandSpec[] CmdSpecs =
    {
        new("cd",       B("انتقال إلى مجلد", "Change directory"), ArgKind.Directory, Flags: new[]{ "/d", ".." }),
        new("dir",      B("سرد المحتويات", "List directory"),     ArgKind.Directory, Flags: new[]{ "/b", "/s", "/a" }),
        new("type",     B("عرض ملفّ", "Print file"),               ArgKind.File),
        new("copy",     B("نسخ", "Copy"),                         ArgKind.Any),
        new("xcopy",    B("نسخ شجريّ", "Copy tree"),               ArgKind.Any,  Flags: new[]{ "/E", "/I", "/Y" }),
        new("move",     B("نقل", "Move"),                         ArgKind.Any),
        new("del",      B("حذف ملفّ", "Delete file"),              ArgKind.File, Flags: new[]{ "/f", "/q", "/s" }),
        new("md",       B("إنشاء مجلد", "Make directory"),        ArgKind.Directory),
        new("rd",       B("حذف مجلد", "Remove directory"),        ArgKind.Directory, Flags: new[]{ "/s", "/q" }),
        new("cls",      B("تنظيف الشاشة", "Clear screen"),        ArgKind.None),
        new("echo",     B("طباعة نصّ", "Print text"),              ArgKind.None),
        new("set",      B("متغيّر بيئة", "Environment variable"),  ArgKind.None),
        new("where",    B("مسار أمر", "Locate command"),          ArgKind.Command),
        new("tasklist", B("العمليّات", "Processes"),               ArgKind.None),
        new("taskkill", B("إنهاء عمليّة", "Kill process"),         ArgKind.Process, Flags: new[]{ "/F", "/IM", "/PID" }),
        new("ipconfig", B("إعدادات الشبكة", "Network config"),    ArgKind.None, Flags: new[]{ "/all", "/flushdns" }),
        new("ping",     B("اختبار اتّصال", "Ping host"),           ArgKind.None, Flags: new[]{ "-t", "-n" }),
        new("start",    B("فتح/تشغيل", "Start"),                  ArgKind.Any),
        new("exit",     B("خروج", "Exit"),                        ArgKind.None),
    };

    // ===== أدوات التطوير (تعمل في كلّ الصدفات) =====
    private static readonly CommandSpec[] DevSpecs =
    {
        new("git", B("نظام إصدارات", "Version control"), ArgKind.Any,
            Subs: new[]{ "status", "add", "commit", "push", "pull", "fetch", "clone", "checkout", "switch",
                         "branch", "merge", "rebase", "log", "diff", "stash", "reset", "restore", "remote",
                         "tag", "init", "show", "cherry-pick", "worktree", "blame", "revert", "clean" },
            Flags: new[]{ "--version", "--help" },
            SubArg: new(StringComparer.OrdinalIgnoreCase)
            {
                ["checkout"] = ArgKind.GitRef, ["switch"] = ArgKind.GitRef, ["merge"] = ArgKind.GitRef,
                ["rebase"]   = ArgKind.GitRef, ["branch"] = ArgKind.GitRef, ["cherry-pick"] = ArgKind.GitRef,
                ["revert"]   = ArgKind.GitRef, ["show"]   = ArgKind.GitRef, ["tag"] = ArgKind.GitRef,
                ["add"]      = ArgKind.Any,    ["diff"]   = ArgKind.Any,    ["restore"] = ArgKind.Any,
                ["status"]   = ArgKind.None,   ["push"]   = ArgKind.None,   ["pull"] = ArgKind.None,
                ["fetch"]    = ArgKind.None,   ["stash"]  = ArgKind.None,   ["log"]  = ArgKind.None,
                ["commit"]   = ArgKind.None,   ["clone"]  = ArgKind.None,   ["init"] = ArgKind.Directory,
            }),

        new("npm", B("مدير حزم Node", "Node package manager"), ArgKind.None,
            Subs: new[]{ "install", "i", "run", "start", "test", "build", "ci", "uninstall", "update",
                         "init", "publish", "audit", "outdated", "exec", "link", "list" },
            SubArg: new(StringComparer.OrdinalIgnoreCase) { ["run"] = ArgKind.NpmScript }),

        new("pnpm", B("مدير حزم سريع", "Fast package manager"), ArgKind.None,
            Subs: new[]{ "install", "add", "run", "dev", "build", "test", "remove", "update", "dlx", "exec", "list" },
            SubArg: new(StringComparer.OrdinalIgnoreCase) { ["run"] = ArgKind.NpmScript }),

        new("yarn", B("مدير حزم Yarn", "Yarn package manager"), ArgKind.NpmScript,
            Subs: new[]{ "install", "add", "run", "dev", "build", "test", "remove", "upgrade", "dlx" },
            SubArg: new(StringComparer.OrdinalIgnoreCase) { ["run"] = ArgKind.NpmScript }),

        new("bun", B("منفّذ/مدير حزم Bun", "Bun runtime"), ArgKind.File,
            Subs: new[]{ "install", "add", "run", "dev", "build", "test", "remove", "x" },
            SubArg: new(StringComparer.OrdinalIgnoreCase) { ["run"] = ArgKind.NpmScript }),

        new("npx",  B("تشغيل حزمة مؤقّتاً", "Run package"), ArgKind.None),
        new("node", B("منفّذ JavaScript", "JavaScript runtime"), ArgKind.File, Flags: new[]{ "-v", "--watch" }),

        new("dotnet", B("منصّة .NET", ".NET CLI"), ArgKind.Any,
            Subs: new[]{ "build", "run", "test", "restore", "publish", "add", "new", "clean", "watch",
                         "format", "sln", "nuget", "tool", "list", "pack" },
            Flags: new[]{ "--version", "-c Release", "--no-build" },
            SubArg: new(StringComparer.OrdinalIgnoreCase)
            {
                ["build"] = ArgKind.Any, ["run"] = ArgKind.None, ["test"] = ArgKind.Any,
                ["new"]   = ArgKind.None, ["restore"] = ArgKind.Any, ["clean"] = ArgKind.Any,
            }),

        new("python",  B("منفّذ Python", "Python runtime"), ArgKind.File, Flags: new[]{ "-m", "-V" }),
        new("python3", B("منفّذ Python", "Python runtime"), ArgKind.File, Flags: new[]{ "-m", "-V" }),
        new("py",      B("مشغّل Python", "Python launcher"), ArgKind.File, Flags: new[]{ "-m", "-3" }),
        new("pip",     B("مدير حزم Python", "Python packages"), ArgKind.None,
            Subs: new[]{ "install", "uninstall", "list", "freeze", "show", "download" }),

        new("docker", B("حاويات", "Containers"), ArgKind.None,
            Subs: new[]{ "ps", "images", "run", "exec", "build", "pull", "push", "logs", "stop", "start",
                         "restart", "rm", "rmi", "compose", "volume", "network", "inspect", "system" },
            SubArg: new(StringComparer.OrdinalIgnoreCase) { ["build"] = ArgKind.Directory }),
        new("docker-compose", B("تنسيق حاويات", "Compose"), ArgKind.None,
            Subs: new[]{ "up", "down", "build", "logs", "ps", "restart", "exec", "pull" }),

        new("cargo", B("أدوات Rust", "Rust toolchain"), ArgKind.None,
            Subs: new[]{ "build", "run", "test", "check", "new", "add", "fmt", "clippy", "update", "install" }),
        new("go", B("أدوات Go", "Go toolchain"), ArgKind.None,
            Subs: new[]{ "run", "build", "test", "mod", "get", "fmt", "vet", "install" },
            SubArg: new(StringComparer.OrdinalIgnoreCase) { ["run"] = ArgKind.File, ["build"] = ArgKind.Any }),

        new("make", B("بناء عبر Makefile", "Build via Makefile"), ArgKind.None),
        new("gh",   B("واجهة GitHub", "GitHub CLI"), ArgKind.None,
            Subs: new[]{ "pr", "issue", "repo", "run", "release", "auth", "browse", "workflow" }),
    };

    private static readonly Dictionary<string, CommandSpec> UnixIndex = Index(Merge(UnixSpecs, DevSpecs));
    private static readonly Dictionary<string, CommandSpec> PwshIndex = Index(Merge(PwshSpecs, DevSpecs));
    private static readonly Dictionary<string, CommandSpec> CmdIndex  = Index(Merge(CmdSpecs,  DevSpecs));

    private static IEnumerable<CommandSpec> Merge(params CommandSpec[][] groups)
    {
        foreach (var g in groups) foreach (var s in g) yield return s;
    }

    /// <summary>عائلة الصدفة المستنتَجة من سطر التشغيل.</summary>
    public enum Family { Unix, Pwsh, Cmd }

    /// <summary>يستنتج عائلة الصدفة من مسار/سطر تشغيلها.</summary>
    public static Family FamilyOf(string? shell)
    {
        shell ??= "";
        if (shell.Contains("powershell", StringComparison.OrdinalIgnoreCase)
         || shell.Contains("pwsh", StringComparison.OrdinalIgnoreCase)) return Family.Pwsh;
        if (shell.Contains("cmd", StringComparison.OrdinalIgnoreCase)) return Family.Cmd;
        return Family.Unix;
    }

    /// <summary>كلّ أوامر العائلة (لاقتراح الكلمة الأولى).</summary>
    public static IReadOnlyCollection<CommandSpec> All(Family f) => f switch
    {
        Family.Pwsh => PwshIndex.Values,
        Family.Cmd  => CmdIndex.Values,
        _           => UnixIndex.Values,
    };

    /// <summary>مواصفة أمر بالاسم (أو null إن كان خارج الكتالوج).</summary>
    public static CommandSpec? Find(Family f, string name)
    {
        var idx = f switch { Family.Pwsh => PwshIndex, Family.Cmd => CmdIndex, _ => UnixIndex };
        return idx.TryGetValue(name, out var s) ? s : null;
    }
}
