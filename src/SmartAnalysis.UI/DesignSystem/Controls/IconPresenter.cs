using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartAnalysis.UI.DesignSystem.Controls;

/// <summary>
/// Renders a first-party icon geometry (<c>SA.Icon.*</c>, doc 25) as a stroked outline in the current
/// <see cref="Control.Foreground"/> brush — "currentColor" semantics, so icons theme-swap with the palette.
/// Its default style (Icons/IconStyles.xaml) scales the 24-grid geometry uniformly via a Viewbox, keeping
/// the Lucide 2px stroke proportional at any size token. Usage:
/// <c>&lt;ds:IconPresenter Data="{StaticResource SA.Icon.Save}" Foreground="{DynamicResource SA.Brush.Accent.OnSurface}"/&gt;</c>.
/// </summary>
public class IconPresenter : Control
{
    static IconPresenter()
        => DefaultStyleKeyProperty.OverrideMetadata(
            typeof(IconPresenter), new FrameworkPropertyMetadata(typeof(IconPresenter)));

    /// <summary>The icon outline geometry (an <c>SA.Icon.*</c> resource).</summary>
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(IconPresenter), new PropertyMetadata(null));

    /// <summary>Gets or sets <see cref="DataProperty"/>.</summary>
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
