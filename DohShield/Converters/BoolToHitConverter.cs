using System.Globalization;
using System.Windows.Data;

namespace DohShield.Converters;

/// <summary>快取命中：true → "✓"，false → ""</summary>
[ValueConversion(typeof(bool), typeof(string))]
public sealed class BoolToHitConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "✓" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
