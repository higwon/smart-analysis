using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartAnalysis.UI.Converters;

/// <summary>
/// Resolves an <c>SA.Icon.*</c> resource key (a string on the view-model) to its <see cref="Geometry"/>
/// from the merged design system — so view-models carry a stable key, not a WPF resource reference.
/// </summary>
public sealed class ResourceKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key ? System.Windows.Application.Current?.TryFindResource(key) as Geometry : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
