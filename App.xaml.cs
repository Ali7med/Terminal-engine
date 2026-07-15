using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace TerminalLauncher;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    private static bool ValidHandle(IntPtr h) => h != IntPtr.Zero && h != new IntPtr(-1);

    /// <summary>
    /// أُطلق من طرفية إن كان له نافذة كونسول أو أيّ مقبض stdio موروث (كونسول أو أنبوب Git Bash).
    /// </summary>
    private static bool LaunchedFromShell()
        => GetConsoleWindow() != IntPtr.Zero
           || ValidHandle(GetStdHandle(-11))   // STD_OUTPUT_HANDLE
           || ValidHandle(GetStdHandle(-10))   // STD_INPUT_HANDLE
           || ValidHandle(GetStdHandle(-12));  // STD_ERROR_HANDLE

    protected override void OnStartup(StartupEventArgs e)
    {
        // إن أُطلق من طرفية (dotnet run / Git Bash / cmd / PowerShell) يرث مقابضها، فتلتصق
        // عمليات cmd الوليدة بها وتتجاوز الكونسول الوهمي (ConPTY) → لا يظهر إخراج.
        // الحلّ الجذري: أعد إطلاق نسخة منفصلة بلا مقابض موروثة ثم اخرج، فيعمل التيرمنال
        // المضمّن مهما كانت طريقة التشغيل (نقر مزدوج أو من أي طرفية).
        if (!e.Args.Contains("--detached") && LaunchedFromShell())
        {
            try
            {
                string? exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe) &&
                    exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo(exe, "--detached") { UseShellExecute = true });
                    Shutdown();
                    return;
                }
            }
            catch { /* إن فشل، نتابع بالتشغيل العادي */ }
        }

        // ConPTY يجعل dotnet/MSBuild يستخدمان المسجّل التفاعلي المتحرّك (يحرّك المؤشّر) فيظهر
        // «مقصوصاً». نُطفئه ليُخرج أسطراً عادية تُعرَض كاملةً. يُورَّث لكل جلسة.
        Environment.SetEnvironmentVariable("MSBUILDTERMINALLOGGER", "off");
        Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");

        // خطوط المستخدم: اقرأ fonts.json وطبّقه قبل بناء النوافذ (يستبدل موارد Font.*/Size.*)
        Services.FontManager.Load();
        Services.FontManager.Apply();

        base.OnStartup(e);
    }
}
