using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>
/// سجلّ المشاريع الحيّ. المشروع بلا لون — لونه المعروض = لون تاكه الأساس (عبر <see cref="TagService"/>).
/// الحفظ على القرص مسؤوليّة المستهلِك عبر حدث <see cref="Changed"/>.
/// </summary>
public static class ProjectService
{
    private static readonly List<Project> _projects = new();

    /// <summary>يُطلَق عند أيّ تغيير في المشاريع (إضافة/تعديل/حذف) — للحفظ وإعادة رسم الواجهة.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<Project> All => _projects;

    /// <summary>يستبدل المحتوى بمشاريع محمَّلة من القرص (بلا إطلاق حدث).</summary>
    public static void Initialize(IEnumerable<Project> projects)
    {
        _projects.Clear();
        _projects.AddRange(projects);
    }

    /// <summary>يعيد مشروعاً بالاسم أو null (مطابقة غير حسّاسة للحالة).</summary>
    public static Project? Find(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : _projects.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>ينشئ مشروعاً جديداً (يُطلِق <see cref="Changed"/>)؛ يعيد الموجود إن كان الاسم مستعمَلاً.</summary>
    public static Project Create(string name)
    {
        var p = GetOrCreateSilent(name);
        Changed?.Invoke();
        return p;
    }

    /// <summary>يضيف مشروعاً بلا إطلاق حدث (للترحيل الدفعيّ)؛ يعيد الموجود إن كان الاسم مستعمَلاً.</summary>
    public static Project GetOrCreateSilent(string name)
    {
        name = name.Trim();
        var existing = Find(name);
        if (existing != null) return existing;
        var created = new Project { Name = name };
        _projects.Add(created);
        return created;
    }

    /// <summary>يعيد تسمية مشروع (يُطلِق <see cref="Changed"/>). لا يفعل شيئاً إن تعارض الاسم الجديد أو كان فارغاً.</summary>
    public static void Rename(string oldName, string newName)
    {
        newName = (newName ?? "").Trim();
        var p = Find(oldName);
        if (p == null || newName.Length == 0) return;
        if (!string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase) && Find(newName) != null) return;
        p.Name = newName;
        Changed?.Invoke();
    }

    /// <summary>يضبط باث المشروع الافتراضيّ (يُطلِق <see cref="Changed"/>).</summary>
    public static void SetFolder(string name, string folder)
    {
        var p = Find(name);
        if (p == null) return;
        p.Folder = folder ?? "";
        Changed?.Invoke();
    }

    /// <summary>يضبط صدفة المشروع الافتراضيّة (يُطلِق <see cref="Changed"/>).</summary>
    public static void SetShell(string name, string shell)
    {
        var p = Find(name);
        if (p == null) return;
        p.Shell = shell ?? "";
        Changed?.Invoke();
    }

    /// <summary>يستبدل تاكات المشروع بالكامل (يُطلِق <see cref="Changed"/>).</summary>
    public static void SetTags(string name, IEnumerable<string> tags)
    {
        var p = Find(name);
        if (p == null) return;
        p.Tags = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Changed?.Invoke();
    }

    /// <summary>يفكّ ربط تاك من كلّ المشاريع (عند حذف التاك). يُطلِق <see cref="Changed"/> إن تغيّر شيء.</summary>
    public static void RemoveTagFromAll(string tag)
    {
        bool any = false;
        foreach (var p in _projects)
        {
            int n = p.Tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
            if (n > 0) any = true;
        }
        if (any) Changed?.Invoke();
    }

    /// <summary>يعيد تسمية تاك في كلّ المشاريع (عند إعادة تسمية التاك).</summary>
    public static void RenameTagInAll(string oldTag, string newTag)
    {
        bool any = false;
        foreach (var p in _projects)
            for (int i = 0; i < p.Tags.Count; i++)
                if (string.Equals(p.Tags[i], oldTag, StringComparison.OrdinalIgnoreCase))
                {
                    p.Tags[i] = newTag; any = true;
                }
        if (any) Changed?.Invoke();
    }

    /// <summary>يحذف مشروعاً بالاسم (يُطلِق <see cref="Changed"/> إن وُجِد).</summary>
    public static void Remove(string name)
    {
        int i = _projects.FindIndex(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        _projects.RemoveAt(i);
        Changed?.Invoke();
    }

    // ===== الأوامر =====

    /// <summary>يقسّم نصّاً متعدّد الأسطر إلى خطوات مُشذَّبة غير فارغة.</summary>
    public static List<string> SplitSteps(string text)
        => (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>
    /// يضيف أمراً مثبَّتاً لمشروع بعد فحص التكرار (مطابقة الخطوات المُشذَّبة). <paramref name="stepsText"/>
    /// نصّ متعدّد الأسطر (سطر=خطوة). يعيد true إن أُضيف فعلاً. يُطلِق <see cref="Changed"/>.
    /// </summary>
    public static bool AddCommand(string? projectName, string stepsText, string? label = null, string? folder = null)
    {
        var p = Find(projectName);
        if (p == null) return false;
        var steps = SplitSteps(stepsText);
        if (steps.Count == 0) return false;
        string key = string.Join("\n", steps);
        if (p.Commands.Any(c => c.DedupKey == key)) return false;
        p.Commands.Add(new ProjectCommand
        {
            Label = string.IsNullOrWhiteSpace(label) ? "" : label!.Trim(),
            Steps = steps,
            Folder = string.IsNullOrWhiteSpace(folder) ? null : folder!.Trim(),
        });
        Changed?.Invoke();
        return true;
    }

    /// <summary>يحدّث أمراً قائماً في مكانه (اسم/خطوات/فولدر). يُطلِق <see cref="Changed"/>.</summary>
    public static void UpdateCommand(ProjectCommand cmd, string label, string stepsText, string? folder)
    {
        var steps = SplitSteps(stepsText);
        if (steps.Count == 0) return;
        cmd.Label = string.IsNullOrWhiteSpace(label) ? "" : label.Trim();
        cmd.Steps = steps;
        cmd.Folder = string.IsNullOrWhiteSpace(folder) ? null : folder!.Trim();
        Changed?.Invoke();
    }

    /// <summary>يحذف أمراً مثبَّتاً من مشروع (يُطلِق <see cref="Changed"/> إن حُذِف).</summary>
    public static void RemoveCommand(string? projectName, ProjectCommand cmd)
    {
        var p = Find(projectName);
        if (p == null) return;
        if (p.Commands.Remove(cmd)) Changed?.Invoke();
    }

    // ===== اللون (مشتقّ من التاك الأساس) =====

    /// <summary>لون المشروع = لون تاكه الأساس، أو null إن بلا تاكات.</summary>
    /// <summary>
    /// لون المشروع: اللون الصريح المختار من قائمة المشروع إن وُجد، وإلّا لون تاكه الأساس. اللون الصريح
    /// يسبق التاك لأنّه اختيار مباشر من المستخدم لهذا المشروع بعينه.
    /// </summary>
    public static Color? ColorOf(Project? p)
    {
        if (p is null) return null;
        if (!string.IsNullOrWhiteSpace(p.Color))
        {
            try { return (Color)ColorConverter.ConvertFromString(p.Color); }
            catch { /* لون محفوظ تالف — نرتدّ للتاك */ }
        }
        return p.PrimaryTag is { } t ? TagService.ColorOf(t) : null;
    }

    /// <summary>
    /// يُطلِق <see cref="Changed"/> يدويّاً (حفظ + إعادة رسم) بعد تعديل كائن مشروع مباشرةً — مثل نسخ
    /// مشروع بحقوله وأوامره دفعةً واحدة بدل استدعاء مُضبِّط لكلّ حقل.
    /// </summary>
    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>يضبط لون المشروع الصريح (فارغ = ارجع للون التاك). يُطلِق <see cref="Changed"/>.</summary>
    public static void SetColor(string name, string hex)
    {
        var p = Find(name);
        if (p == null) return;
        p.Color = hex ?? "";
        Changed?.Invoke();
    }

    /// <summary>لون مشروع بالاسم (لون تاكه الأساس)، أو null.</summary>
    public static Color? ColorOf(string? projectName) => ColorOf(Find(projectName));
}
