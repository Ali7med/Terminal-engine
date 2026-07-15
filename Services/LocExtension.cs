using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

namespace TerminalLauncher.Services;

/// <summary>
/// مصدر ربط للتعريب: يوفّر مُفهرِساً <c>this[key]</c> يعيد النصّ المترجَم، ويُطلق تغييراً عند تبدّل اللغة
/// (<see cref="Loc.Changed"/>) كي تتحدّث النصوص المربوطة حيّاً — بما فيها ما داخل <c>DataTemplate</c>.
/// </summary>
public sealed class LocProxy : INotifyPropertyChanged
{
    public static LocProxy Instance { get; } = new();

    private LocProxy() => Loc.Changed += () =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// امتداد XAML للتعريب: <c>{loc:T some.key}</c> — يربط نصّاً مترجَماً يتحدّث مع تغيّر اللغة. يصلح داخل
/// القوالب (<c>DataTemplate</c>) حيث يتعذّر الضبط من الكود. مثال: <c>Header="{loc:T srv.containers.rename}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TExtension() { }
    public TExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]") { Source = LocProxy.Instance, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}
