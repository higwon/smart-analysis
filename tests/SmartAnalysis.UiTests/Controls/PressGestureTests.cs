using SmartAnalysis.UI.Controls;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// TASK-UX09: the click-vs-pan decision, which was where the defect lived and where no test could reach it.
/// Synthetic mouse events carry no position, so the control could not be driven; the decision now lives in a
/// type that can be.
/// </summary>
public sealed class PressGestureTests
{
    private const double Min = 4.0;

    private static PressGesture Pressed(double x = 100, double y = 100, bool canPan = true)
    {
        var g = new PressGesture();
        g.Press(x, y, canPan);
        return g;
    }

    [Fact]
    public void A_press_on_a_pannable_image_is_not_yet_a_pan()
    {
        // The defect. Arming a pan the instant the button went down made every click on a zoomed image a
        // zero-length drag, so the picture whose pixels you most want to click was the one you could not.
        Assert.False(Pressed().IsPanning);
    }

    [Fact]
    public void A_press_and_release_without_moving_is_a_click_even_when_the_image_can_pan()
    {
        var g = Pressed();

        Assert.Equal((100.0, 100.0), g.Release(100, 100, Min, Min));
    }

    [Fact]
    public void A_press_that_travels_becomes_a_pan_and_is_no_longer_a_click()
    {
        var g = Pressed();

        Assert.True(g.BeginsPan(100 + Min + 1, 100, Min, Min));
        Assert.True(g.IsPanning);
        Assert.Null(g.Release(100 + Min + 1, 100, Min, Min));
    }

    [Fact]
    public void A_pan_begins_once_and_then_stops_announcing_itself()
    {
        // The caller takes up the drag on the one call that returns true; a second true would reset its anchor
        // mid-drag and make the image jump.
        var g = Pressed();

        Assert.True(g.BeginsPan(200, 100, Min, Min));
        Assert.False(g.BeginsPan(300, 100, Min, Min));
        Assert.True(g.IsPanning);
    }

    [Theory]
    [InlineData(Min, 0)]        // exactly at the threshold is still a click
    [InlineData(0, Min)]
    [InlineData(-Min, 0)]
    public void Movement_up_to_the_threshold_leaves_it_a_click(double dx, double dy)
    {
        var g = Pressed();

        Assert.False(g.BeginsPan(100 + dx, 100 + dy, Min, Min));
        Assert.NotNull(g.Release(100 + dx, 100 + dy, Min, Min));
    }

    [Fact]
    public void A_press_on_an_image_that_cannot_pan_is_still_a_click()
    {
        // A fitted image is not pannable, which is where clicking worked before — it must go on working.
        var g = Pressed(canPan: false);

        Assert.False(g.BeginsPan(500, 500, Min, Min));   // never becomes a pan
        Assert.Equal((100.0, 100.0), g.Release(100, 100, Min, Min));
    }

    [Fact]
    public void A_drift_past_the_threshold_is_not_a_click_even_when_no_pan_was_possible()
    {
        // Nothing during the move would have caught it: an unpannable image never arms, so the release is the
        // only place a shaky press on one can be rejected.
        var g = Pressed(canPan: false);

        Assert.Null(g.Release(100 + Min + 1, 100, Min, Min));
    }

    [Fact]
    public void A_release_with_no_press_is_nothing()
    {
        // A drag that began outside the control, or a stale gesture, must not become a selection.
        Assert.Null(new PressGesture().Release(100, 100, Min, Min));
    }

    [Fact]
    public void A_second_release_reports_nothing()
    {
        var g = Pressed();
        Assert.NotNull(g.Release(100, 100, Min, Min));

        Assert.Null(g.Release(100, 100, Min, Min));
    }

    [Fact]
    public void A_cancelled_press_is_neither_a_click_nor_a_pan()
    {
        // A double-click fits the image; the press that carried it must not also select a pixel.
        var g = Pressed();
        g.Cancel();

        Assert.False(g.BeginsPan(500, 500, Min, Min));
        Assert.Null(g.Release(100, 100, Min, Min));
    }

    [Fact]
    public void Movement_before_any_press_begins_nothing()
        => Assert.False(new PressGesture().BeginsPan(500, 500, Min, Min));
}
