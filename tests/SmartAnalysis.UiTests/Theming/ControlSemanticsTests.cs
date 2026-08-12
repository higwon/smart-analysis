using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;

namespace SmartAnalysis.UiTests.Theming;

/// <summary>
/// Guards that the design system's implicit control templates preserve WPF's built-in semantics — not just
/// theme chrome. Owning a control's template means a <c>Slider Orientation="Vertical"</c>,
/// <c>TabControl TabStripPlacement="Bottom"</c>, <c>Expander ExpandDirection="Up"</c>, or a checked
/// <c>MenuItem</c> must still behave, otherwise the implicit default silently narrows the control.
/// </summary>
public sealed class ControlSemanticsTests
{
    // Parents the control in a measured Border (applies the implicit style + instantiates the template +
    // builds the visual tree) on the shared WPF thread, then runs the query.
    private static TResult Hosted<TControl, TResult>(Func<TControl> make, Func<TControl, TResult> query)
        where TControl : FrameworkElement
        => WpfTestHost.Invoke(() =>
        {
            var control = make();
            var host = new Border { Child = control, Width = 300, Height = 300 };
            host.Measure(new Size(300, 300));
            host.Arrange(new Rect(0, 0, 300, 300));
            host.UpdateLayout();
            control.ApplyTemplate();
            return query(control);
        });

    [Fact]
    public void Vertical_slider_uses_a_vertical_track()
    {
        var orientation = Hosted(
            () => new Slider { Orientation = Orientation.Vertical },
            s => ((Track)s.Template.FindName("PART_Track", s)).Orientation);

        Assert.Equal(Orientation.Vertical, orientation);
    }

    [Fact]
    public void Bottom_tab_strip_moves_the_header_below_the_content()
    {
        var (headerRow, contentRow) = Hosted(
            () =>
            {
                var tabs = new TabControl { TabStripPlacement = Dock.Bottom };
                tabs.Items.Add(new TabItem { Header = "A", Content = "a" });
                return tabs;
            },
            tabs => (
                Grid.GetRow((Border)tabs.Template.FindName("HeaderBorder", tabs)),
                Grid.GetRow((Border)tabs.Template.FindName("ContentBorder", tabs))));

        Assert.Equal(1, headerRow);  // header moved to the second row…
        Assert.Equal(0, contentRow); // …content to the first
    }

    [Fact]
    public void Up_expander_docks_the_header_to_the_bottom()
    {
        var dock = Hosted(
            () => new Expander { ExpandDirection = ExpandDirection.Up, Header = "H", Content = "c" },
            e => DockPanel.GetDock((ToggleButton)e.Template.FindName("Header", e)));

        Assert.Equal(Dock.Bottom, dock);
    }

    [Fact]
    public void Checked_menu_item_shows_its_check_indicator()
    {
        // The drop-down (SubmenuItem) template must reveal the non-colour check glyph when IsChecked, so a
        // checkable menu item's state survives our owning its template. Apply that template directly (a real
        // submenu's containers only generate once its popup opens, which a headless test can't drive).
        var (uncheckedVis, checkedVis) = WpfTestHost.Invoke(() =>
        {
            var template = (ControlTemplate)System.Windows.Application.Current!.Resources["SA.MenuItem.SubmenuItem"];

            Visibility Probe(bool isChecked)
            {
                var item = new MenuItem { Header = "Toggle", IsCheckable = true, IsChecked = isChecked, Template = template };
                var host = new Border { Child = item, Width = 200, Height = 40 };
                host.Measure(new Size(200, 40));
                host.Arrange(new Rect(0, 0, 200, 40));
                host.UpdateLayout();
                item.ApplyTemplate();
                return ((UIElement)item.Template.FindName("Check", item)).Visibility;
            }

            return (Probe(false), Probe(true));
        });

        Assert.Equal(Visibility.Collapsed, uncheckedVis); // hidden when not checked
        Assert.Equal(Visibility.Visible, checkedVis);     // shown when checked
    }
}
