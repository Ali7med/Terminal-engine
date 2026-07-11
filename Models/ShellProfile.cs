using System;
using System.Collections.Generic;

namespace TerminalLauncher.Models;

/// <summary>
/// بروفايل صدفة: تعريف كامل لكيفيّة تشغيل تيرمنال (صدفة مكتشَفة تلقائياً أو أمر مخصّص).
/// يُمرَّر منه سطر التشغيل ومجلد العمل ومتغيّرات البيئة إلى جلسة ConPTY.
/// </summary>
public sealed class ShellProfile
{
    /// <summary>معرّف فريد ثابت (مفتاح الحفظ/الاختيار). للصدفات المكتشَفة يكون ثابتاً معروفاً (cmd/powershell/pwsh/bash/wsl:distro).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>الاسم المعروض في القوائم.</summary>
    public string Name { get; set; } = "";

    /// <summary>أيقونة اختياريّة (رمز/إيموجي) تُعرَض بجانب الاسم.</summary>
    public string? Icon { get; set; }

    /// <summary>مسار الملف التنفيذيّ للصدفة (اختياريّ إن استُعمِل <see cref="CommandLine"/> مباشرةً).</summary>
    public string? ExePath { get; set; }

    /// <summary>وسائط سطر الأوامر التي تُمرَّر للملف التنفيذيّ.</summary>
    public string? Arguments { get; set; }

    /// <summary>مجلد العمل الابتدائيّ (اختياريّ؛ يتجاوز مسار الأمر المحفوظ إن حُدِّد).</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>متغيّرات بيئة إضافيّة تُضاف عند تشغيل الجلسة (قد تكون فارغة).</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>لون التاب الاختياريّ (#RRGGBB) — للاستعمال المستقبليّ في تلوين رأس التاب.</summary>
    public string? TabColor { get; set; }

    /// <summary>نهاية السطر المُرسَلة عند Enter: "\r" لصدفات ويندوز، "\n" لصدفات يونكس (bash/WSL).</summary>
    public string Newline { get; set; } = "\r";

    /// <summary>هل الصدفة متاحة فعليّاً على هذا الجهاز (للصدفات المكتشَفة). المخصّصة دائماً true.</summary>
    public bool Available { get; set; } = true;

    /// <summary>true إن كان بروفايلاً مكتشَفاً تلقائياً (لا يُحفَظ/يُحرَّر)، false للبروفايلات المخصّصة.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// يبني سطر تشغيل ConPTY الكامل (الملف التنفيذيّ + الوسائط) مع اقتباس المسار إن احتوى فراغات.
    /// إن كان <see cref="ExePath"/> فارغاً يُعاد <see cref="Arguments"/> كما هو (سطر أمر جاهز).
    /// </summary>
    public string BuildCommandLine()
    {
        string exe = ExePath ?? "";
        string args = Arguments ?? "";
        if (string.IsNullOrWhiteSpace(exe))
            return args.Trim();

        string quotedExe = exe.Contains(' ') && !exe.StartsWith('"') ? $"\"{exe}\"" : exe;
        return string.IsNullOrWhiteSpace(args) ? quotedExe : $"{quotedExe} {args}";
    }

    /// <summary>الاسم مع الأيقونة إن وُجِدت (للعرض في الكومبو).</summary>
    public string DisplayLabel => string.IsNullOrEmpty(Icon) ? Name : $"{Icon}  {Name}";

    /// <summary>سطر وصفيّ يظهر تحت الاسم في قائمة الإدارة (النوع + سطر التشغيل).</summary>
    public string TypeHint => (IsBuiltIn ? "مكتشَف · " : "مخصّص · ") + BuildCommandLine();
}
