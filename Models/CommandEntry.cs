using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TerminalLauncher.Models;

/// <summary>
/// إدخال محفوظ: باث (مجلد العمل) + أمر يُنفَّذ داخله عبر cmd.
/// </summary>
public sealed class CommandEntry : INotifyPropertyChanged
{
    private string _name = "";
    private string _path = "";
    private string _command = "";
    private string _shell = "cmd";
    private List<string> _tags = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// تبويب لحظيّ لا يُحفَظ في لقطة الجلسة ولا يُسترجَع (مثل شِل حاوية Docker عبر ssh — سطر أمره يحمل
    /// موارد مؤقّتة زائلة). في الذاكرة فقط؛ غير مُسلسَل.
    /// </summary>
    public bool IsTransient { get; set; }

    /// <summary>مفتاح الصدفة الافتراضية لهذا الأمر: cmd | powershell | bash.</summary>
    public string Shell
    {
        get => _shell;
        set { _shell = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Initial)); }
    }

    /// <summary>الحرف الأوّل من الاسم (لمختصر الأمر في الشريط المطويّ).</summary>
    public string Initial => string.IsNullOrWhiteSpace(_name) ? "?" : _name.Trim().Substring(0, 1).ToUpperInvariant();

    public string Path
    {
        get => _path;
        set { _path = value; OnPropertyChanged(); }
    }

    public string Command
    {
        get => _command;
        set { _command = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// أسماء المشاريع/التصنيفات التي يتبعها هذا الأمر (وسوم متعدّدة). الأوّل يُعتبَر المشروع الأساس
    /// (لون ترويسة الشارة). قد تكون فارغة (أمر بلا تصنيف).
    /// </summary>
    public List<string> Tags
    {
        get => _tags;
        set
        {
            _tags = value ?? new();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PrimaryTag));
            OnPropertyChanged(nameof(HasTags));
        }
    }

    /// <summary>المشروع الأساس (أوّل وسم) أو null إن بلا تصنيف — لون ترويسة الشارة يُشتقّ منه.</summary>
    public string? PrimaryTag => _tags.Count > 0 ? _tags[0] : null;

    /// <summary>هل للأمر تصنيف واحد على الأقلّ؟</summary>
    public bool HasTags => _tags.Count > 0;

    /// <summary>هل يتبع الأمر المشروع المسمّى (مطابقة غير حسّاسة للحالة)؟</summary>
    public bool HasTag(string project)
        => _tags.Any(t => string.Equals(t, project, StringComparison.OrdinalIgnoreCase));

    /// <summary>يُعلِم الواجهة بتغيّر الوسوم بعد تعديلها في مكانها (Add/Remove على القائمة نفسها).</summary>
    public void NotifyTagsChanged()
    {
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(PrimaryTag));
        OnPropertyChanged(nameof(HasTags));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
