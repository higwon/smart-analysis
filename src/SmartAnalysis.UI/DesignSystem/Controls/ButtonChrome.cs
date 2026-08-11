using System.Windows;
using System.Windows.Media;

namespace SmartAnalysis.UI.DesignSystem.Controls;

/// <summary>
/// Attached properties that let a single shared button <c>ControlTemplate</c> be reused across variants
/// (Secondary/Primary/Danger/Icon/Toolbar) without duplicating the template. Each variant style injects
/// its own hover/pressed brushes here; the template's state layers bind to them. This is why Danger no
/// longer inherits Primary's accent hover/pressed (the bug this fixes): the interaction color is a
/// per-variant token, not baked into the template's triggers.
/// </summary>
public static class ButtonChrome
{
    /// <summary>The fill shown on <c>IsMouseOver</c> (a per-variant semantic brush).</summary>
    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "HoverBackground", typeof(Brush), typeof(ButtonChrome), new PropertyMetadata(null));

    /// <summary>The fill shown on <c>IsPressed</c> (a per-variant semantic brush).</summary>
    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "PressedBackground", typeof(Brush), typeof(ButtonChrome), new PropertyMetadata(null));

    /// <summary>Gets <see cref="HoverBackgroundProperty"/>.</summary>
    public static Brush? GetHoverBackground(DependencyObject obj) => (Brush?)obj.GetValue(HoverBackgroundProperty);

    /// <summary>Sets <see cref="HoverBackgroundProperty"/>.</summary>
    public static void SetHoverBackground(DependencyObject obj, Brush? value) => obj.SetValue(HoverBackgroundProperty, value);

    /// <summary>Gets <see cref="PressedBackgroundProperty"/>.</summary>
    public static Brush? GetPressedBackground(DependencyObject obj) => (Brush?)obj.GetValue(PressedBackgroundProperty);

    /// <summary>Sets <see cref="PressedBackgroundProperty"/>.</summary>
    public static void SetPressedBackground(DependencyObject obj, Brush? value) => obj.SetValue(PressedBackgroundProperty, value);
}
