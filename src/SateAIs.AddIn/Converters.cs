using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SateAIs.AddIn;

/// <summary>The add-in's resource dictionaries, for converters that run outside the visual tree.</summary>
internal static class ThemeResources
{
    private static readonly Lazy<ResourceDictionary> Cosmic = new(() => Load("Theme/Cosmic.xaml"));
    private static readonly Lazy<ResourceDictionary> Icons = new(() => Load("Theme/Icons.xaml"));

    public static Brush Brush(string key) => Cosmic.Value[key] as Brush ?? Brushes.Transparent;

    public static Geometry? Icon(string key) => Icons.Value.Contains(key) ? Icons.Value[key] as Geometry : null;

    private static ResourceDictionary Load(string path) =>
        new() { Source = new Uri($"pack://application:,,,/SateAIs.AddIn;component/{path}", UriKind.Absolute) };
}

/// <summary>True → Collapsed, false → Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A colour string such as "#00d4ff" to a brush, for the per-type icons.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string hex && ColorConverter.ConvertFromString(hex) is Color color
            ? new SolidColorBrush(color)
            : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>An icon key such as "ship" to the geometry "Icon.ship" (plus an optional suffix, e.g. ".dashed").</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? ThemeResources.Icon($"Icon.{key}{parameter as string ?? ""}") : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>"ok" / "error" / anything else → the status colours.</summary>
public sealed class StatusKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ThemeResources.Brush(value switch
    {
        "ok" => "Cosmic.Success",
        "error" => "Cosmic.Warning",
        _ => "Cosmic.TextMuted",
    });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
