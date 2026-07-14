using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TerminalLauncher.Models;
using TerminalLauncher.Services;

namespace TerminalLauncher.Converters;

/// <summary>
/// يحوّل <see cref="CommandEntry"/> إلى فرشاة لون مشروعه الأساس (أوّل وسم) — لترويسة شارة الأمر في
/// الشريط الجانبيّ. يعيد فرشاة شفّافة إن كان الأمر بلا تصنيف (فلا تظهر ترويسة).
/// </summary>
public sealed class EntryPrimaryTagBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Transparent = MakeFrozen(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        Color? c = value switch
        {
            Project p => ProjectService.ColorOf(p),      // لون المشروع = لون تاكه الأساس
            string s  => ProjectService.ColorOf(s),       // اسم مشروع → لون تاكه الأساس
            _         => null,
        };
        return c is { } col ? MakeFrozen(col) : Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush MakeFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>يحوّل <see cref="CommandEntry.HasTags"/> (أو أيّ bool) إلى Visibility (true=مرئيّ).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
