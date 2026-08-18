using System.Windows;
using System.Windows.Controls;
using SmartAnalysis.UI.Views;
using Xunit;

namespace SmartAnalysis.UiTests.Views;

/// <summary>
/// Export target routing (V04): compare mode exports the Before/After grid; otherwise the surface is exported
/// only when it is <b>actually shown</b> (<c>ShowSingle3D</c>), not merely preferred — so an overlay editor that
/// forces the 2D stage exports the visible 2D image, not the hidden surface.
/// </summary>
public sealed class ExportTargetTests
{
    [Fact]
    public void Routes_to_the_visible_view()
    {
        WpfTestHost.Invoke(() =>
        {
            var compare = new Border();
            var surface = new Border();
            var image = new Border();

            // Before/After wins regardless of 3D state.
            Assert.Same(compare, MainWindow.ChooseExportTarget(isBeforeAfter: true, showSingle3D: true, compare, surface, image));

            // Single image, surface actually shown → the surface.
            Assert.Same(surface, MainWindow.ChooseExportTarget(isBeforeAfter: false, showSingle3D: true, compare, surface, image));

            // 3D preferred BUT an overlay editor forces the 2D stage (ShowSingle3D == false) → the 2D image.
            Assert.Same(image, MainWindow.ChooseExportTarget(isBeforeAfter: false, showSingle3D: false, compare, surface, image));

            return true;
        });
    }
}
