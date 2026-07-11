using System;
using System.ComponentModel;
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

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
