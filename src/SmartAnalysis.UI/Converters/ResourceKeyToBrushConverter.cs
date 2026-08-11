using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartAnalysis.UI.Converters;

/// <summary>
/// Resolves an <c>SA.Brush.*</c> resource key (a string on the view-model) to its live <see cref="Brush"/>
/// from the merged design system, so view-models name a semantic color by key and it still theme-swaps.
/// </summary>
public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key ? System.Windows.Application.Current?.TryFindResource(key) as Brush : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
