using System.Globalization;
using System.Windows.Data;
using FileConverter.App.Models;

namespace FileConverter.App.Converters;

public sealed class OutputFormatDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is OutputFormat format ? format.ToDisplayName() : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
