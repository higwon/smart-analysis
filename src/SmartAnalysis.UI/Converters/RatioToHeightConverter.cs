using System.Globalization;
using System.Windows.Data;

namespace SmartAnalysis.UI.Converters;

/// <summary>Scales a 0..1 ratio to a pixel height: <c>value * parameter</c> (parameter = the max height).</summary>
public sealed class RatioToHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ratio = value is double d ? d : 0.0;
        var max = parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var m) ? m : 48.0;
        return Math.Max(1.0, ratio * max);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
