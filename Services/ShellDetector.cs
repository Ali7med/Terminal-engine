using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>
/// اكتشاف تلقائيّ للصدفات المثبّتة على الجهاز (T-101.2):
/// CMD، PowerShell 5، PowerShell 7 (pwsh)، Git Bash، وتوزيعات WSL.
/// كل صدفة (وكل توزيعة WSL) تُعاد كـ <see cref="ShellProfile"/> مبنيّ داخليّاً (IsBuiltIn).
/// </summary>
public static class ShellDetector
{
    /// <summary>يكتشف كل الصدفات المتاحة ويعيدها كبروفايلات مبنيّة داخليّاً (المتاحة فقط).</summary>
    public static List<ShellProfile> DetectAll()
    {
        var list = new List<ShellProfile>();

        // CMD — متاح دائماً على ويندوز.
        list.Add(new ShellProfile
        {
            Id = "cmd",
            Name = "Command Prompt",
            Icon = ">_",
            ExePath = "cmd.exe",
            Newline = "\r",
            Available = true,
            IsBuiltIn = true,
        });

        // Windows PowerShell 5 — متاح دائماً على ويندوز 10/11.
        list.Add(new ShellProfile
        {
            Id = "powershell",
            Name = "Windows PowerShell",
            Icon = "PS",
            ExePath = "powershell.exe",
            Arguments = "-NoLogo",
            Newline = "\r",
            Available = true,
            IsBuiltIn = true,
        });

        // PowerShell 7 (pwsh) — من المسار المعروف أو من PATH.
        string? pwsh = FindPwsh();
        if (pwsh != null)
        {
            list.Add(new ShellProfile
            {
                Id = "pwsh",
                Name = "PowerShell 7",
                Icon = "PS",
                ExePath = pwsh,
                Arguments = "-NoLogo",
                Newline = "\r",
                Available = true,
                IsBuiltIn = true,
            });
        }

        // Git Bash — من المسارات المعروفة أو المتغيّرات البيئيّة.
        string? bash = FindGitBash();
        if (bash != null)
        {
            list.Add(new ShellProfile
            {
                Id = "bash",
                Name = "Git Bash",
                Icon = "$",
                ExePath = bash,
                Arguments = "-i",
                Newline = "\n",
                Available = true,
                IsBuiltIn = true,
            });
        }

        // توزيعات WSL الحقيقيّة فقط — نستبعد توزيعات Docker المساعدة (docker-desktop*) لأنّها ليست
        // صدفات استخدام وتسبّب بروفايلات معطوبة/زائدة. المستخدم يضيف غيرها كبروفايل مخصّص عند الحاجة.
        foreach (var distro in DetectWslDistros())
        {
            if (distro.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new ShellProfile
            {
                Id = "wsl:" + distro,
                Name = "WSL · " + distro,
                Icon = "🐧",
                ExePath = "wsl.exe",
                Arguments = $"-d {distro}",
                Newline = "\n",
                Available = true,
                IsBuiltIn = true,
            });
        }

        return list;
    }

    /// <summary>يبحث عن pwsh.exe في المسار المعروف ثم في متغيّر PATH.</summary>
    private static string? FindPwsh()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string wellKnown = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        if (File.Exists(wellKnown)) return wellKnown;
        return FindOnPath("pwsh.exe");
    }

    /// <summary>يبحث عن bash.exe التابع لـ Git في مسارات التثبيت المعتادة ومتغيّرات البيئة.</summary>
    private static string? FindGitBash()
    {
        var candidates = new List<string>
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
        };

        // بعض التثبيتات تضع Git تحت %LocalAppData%\Programs\Git.
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(localApp, "Programs", "Git", "bin", "bash.exe"));

        // اشتقاق من مسار git.exe في PATH (…\Git\cmd\git.exe → …\Git\bin\bash.exe).
        string? gitExe = FindOnPath("git.exe");
        if (gitExe != null)
        {
            string? cmdDir = Path.GetDirectoryName(gitExe);
            string? gitRoot = cmdDir != null ? Path.GetDirectoryName(cmdDir) : null;
            if (gitRoot != null) candidates.Add(Path.Combine(gitRoot, "bin", "bash.exe"));
        }

        foreach (var p in candidates)
            if (File.Exists(p)) return p;
        return null;
    }

    /// <summary>يبحث عن ملفّ تنفيذيّ ضمن مجلّدات متغيّر PATH.</summary>
    private static string? FindOnPath(string exe)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* مسار غير صالح في PATH — يُتجاهَل. */ }
        }
        return null;
    }

    /// <summary>
    /// يشغّل <c>wsl.exe -l -q</c> ويقرأ أسماء التوزيعات. تُخرِج wsl.exe عادةً UTF-16LE
    /// (وبعض الإصدارات UTF-8)، لذا نقرأ البايتات الخام ونكتشف الترميز باستدلال أحرف NUL
    /// المتخلّلة (سِمة UTF-16LE لنصّ ASCII)، ثمّ ننظّف \r وأحرف NUL.
    /// </summary>
    public static List<string> DetectWslDistros()
    {
        var distros = new List<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-l -q",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return distros;

            // نقرأ البايتات الخام لنكتشف الترميز بأنفسنا (لا نثق بترميز افتراضيّ واحد).
            using var ms = new MemoryStream();
            proc.StandardOutput.BaseStream.CopyTo(ms);
            proc.WaitForExit(4000);
            byte[] bytes = ms.ToArray();

            string output = DecodeWslOutput(bytes);
            foreach (var raw in output.Split('\n'))
            {
                // إزالة \r وأحرف NUL المتبقّية.
                string name = raw.Replace("\0", "").Trim();
                if (name.Length > 0) distros.Add(name);
            }
        }
        catch
        {
            // wsl.exe غير مثبّت أو تعذّر تشغيله — لا توزيعات.
        }
        return distros;
    }

    /// <summary>يفكّ مخرجات wsl: UTF-16LE إن كثُرت أحرف NUL المتخلّلة، وإلا UTF-8.</summary>
    private static string DecodeWslOutput(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        int nulls = 0;
        int scan = System.Math.Min(bytes.Length, 64);
        for (int i = 0; i < scan; i++)
            if (bytes[i] == 0) nulls++;
        // أكثر من ربع البايتات أصفار ⇒ UTF-16LE (كل حرف ASCII = بايت + NUL).
        return nulls > scan / 4 ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
    }
}
