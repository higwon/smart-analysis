using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartAnalysis.UI.Converters;

/// <summary>
/// A row that takes a share of its grid while its content is shown, and no space at all otherwise:
/// <c>true</c> → <c>parameter</c> stars (default 1), <c>false</c> → <c>Auto</c>.
/// </summary>
public sealed class BooleanToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
        {
            return GridLength.Auto;
        }

        double stars = parameter is string s
            && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            && v > 0.0
                ? v
                : 1.0;

        return new GridLength(stars, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
