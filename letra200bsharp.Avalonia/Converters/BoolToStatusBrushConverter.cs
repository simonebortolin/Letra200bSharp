using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace letra200bsharp.Avalonia.Converters;

/// <summary>Maps <c>IsStatusError</c> to a red/green brush for the status message.</summary>
public class BoolToStatusBrushConverter : IValueConverter
{
    public static readonly BoolToStatusBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.Crimson : Brushes.SeaGreen;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
