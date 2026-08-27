using System.Globalization;
using System.Windows.Data;

namespace SmartAnalysis.UI.Converters;

/// <summary>
/// Maps <c>true → 1.0</c>, <c>false → 0.45</c>. For content that is present but inert: a plain
/// <c>TextBlock</c> does not grey itself out when an ancestor is disabled, so a disabled group of controls
/// keeps a full-strength label unless something dims it.
/// </summary>
public sealed class BooleanToOpacityConverter : IValueConverter
{
    private const double Dimmed = 0.45;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : Dimmed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
