using SmartAnalysis.Visualization.Rendering;
using System;
using System.Linq;
using Xunit;

namespace SmartAnalysis.Tests.Visualization;

/// <summary>
/// TASK-V12: a picture of a surface with no stated size is a picture of a texture — 2 um across and 2 mm across
/// look identical on screen, and nothing on the stage said which.
/// <para>
/// Legacy answers this with rulers along the left and bottom edges rather than a scale bar over the image, and
/// this follows its rule for choosing the step (<c>RulerHelper.CalcTickRange2</c>): aim for four intervals, snap
/// to 1/2/5/10 times a power of ten, then coarsen until the labels are not packed tighter than they can be read.
/// </para>
/// </summary>
public sealed class AxisRulerTests
{
    private static double[] Values(RulerTicks ruler)
        => [.. ruler.Ticks.Select(t => double.Parse(t.Label, System.Globalization.CultureInfo.InvariantCulture))];

    [Fact]
    public void The_marks_land_on_round_numbers()
    {
        // 0.5 um steps over 2 um, not 2/4 = 0.5 by luck but by the snap: a ruler marked every 0.163 um is
        // arithmetic, not a scale.
        var ruler = AxisRuler.For(0.0, 2.0, "um", lengthPx: 400);

        Assert.Equal([0.0, 0.5, 1.0, 1.5, 2.0], Values(ruler));
        Assert.Equal("um", ruler.Unit);
    }

    [Fact]
    public void An_awkward_span_is_still_marked_on_round_numbers()
    {
        // 3.7 um / 4 = 0.925, which snaps to 1.
        var ruler = AxisRuler.For(0.0, 3.7, "um", lengthPx: 400);

        Assert.Equal([0.0, 1.0, 2.0, 3.0], Values(ruler));
    }

    [Fact]
    public void A_short_edge_is_marked_more_coarsely_than_a_long_one()
    {
        // The same span on 400 px and on 60 px. Keeping the step would pack the labels on top of each other.
        var wide = AxisRuler.For(0.0, 2.0, "um", lengthPx: 400);
        var narrow = AxisRuler.For(0.0, 2.0, "um", lengthPx: 60);

        // Coarser, and still a ruler. Giving up and drawing nothing is also "fewer marks", which is why this
        // says how few is too few — an empty ruler passed the count comparison on its own.
        Assert.True(narrow.Ticks.Count >= 2, "a short edge was left unmarked rather than marked coarsely.");
        Assert.True(narrow.Ticks.Count < wide.Ticks.Count);
        Assert.All(Values(narrow), v => Assert.Contains(v, new[] { 0.0, 1.0, 2.0 }));
    }

    [Fact]
    public void The_first_mark_sits_on_the_step_grid_not_where_the_image_happens_to_start()
    {
        // A zoomed view starts at 0.3; the marks are still at 0.5, 1.0, 1.5 — the numbers a person reads off a
        // ruler, not 0.3, 0.8, 1.3.
        var ruler = AxisRuler.For(0.3, 1.9, "um", lengthPx: 400);

        Assert.Equal([0.5, 1.0, 1.5], Values(ruler));
    }

    [Fact]
    public void No_mark_falls_outside_the_edge_it_is_drawn_on()
    {
        var ruler = AxisRuler.For(0.3, 1.9, "um", lengthPx: 400);

        Assert.All(ruler.Ticks, t => Assert.InRange(t.Fraction, 0.0, 1.0));
    }

    [Fact]
    public void A_reversed_axis_puts_its_marks_the_right_way_round()
    {
        // A scan recorded top-down gives from > to. The mark for the LOW value belongs at the far end.
        var ruler = AxisRuler.For(2.0, 0.0, "um", lengthPx: 400);

        Assert.All(ruler.Ticks, t => Assert.InRange(t.Fraction, 0.0, 1.0));
        var zero = ruler.Ticks.Single(t => t.Label == "0.0");
        var two = ruler.Ticks.Single(t => t.Label == "2.0");
        Assert.True(zero.Fraction > two.Fraction);
    }

    [Fact]
    public void An_axis_that_covers_no_distance_is_not_marked()
    {
        // One place is not a scale, and marking it would suggest otherwise.
        Assert.Empty(AxisRuler.For(1.0, 1.0, "um", lengthPx: 400).Ticks);
    }

    [Fact]
    public void An_edge_with_no_length_on_screen_is_not_marked()
        => Assert.Empty(AxisRuler.For(0.0, 2.0, "um", lengthPx: 0).Ticks);

    [Fact]
    public void A_span_that_is_not_a_number_is_not_marked()
    {
        Assert.Empty(AxisRuler.For(double.NaN, 2.0, "um", lengthPx: 400).Ticks);
        Assert.Empty(AxisRuler.For(0.0, double.PositiveInfinity, "um", lengthPx: 400).Ticks);
    }

    [Theory]
    [InlineData(0.0005, "5E-4")]      // sub-milli: exponent rather than a row of zeros
    [InlineData(0.005, "0.0050")]
    [InlineData(0.05, "0.050")]
    [InlineData(0.5, "0.50")]
    [InlineData(5.0, "5.0")]
    [InlineData(50.0, "50")]
    public void How_many_decimals_is_decided_by_the_span_not_by_each_value(double span, string expectedLast)
    {
        // One ruler, one way of writing a number — so the marks can be compared at a glance rather than read
        // one at a time.
        var ruler = AxisRuler.For(0.0, span, "um", lengthPx: 400);

        Assert.Equal(expectedLast, ruler.Ticks[^1].Label);
    }

    [Fact]
    public void A_very_wide_span_is_written_in_exponent_form()
    {
        var ruler = AxisRuler.For(0.0, 50000.0, "nm", lengthPx: 400);

        Assert.Contains("E", ruler.Ticks[^1].Label, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(120)]
    [InlineData(400)]
    [InlineData(10000)]
    public void No_two_marks_are_closer_than_a_label_is_wide(double lengthPx)
    {
        // The real limit, asserted as one. Legacy also caps the count at nine, but under this rule the count
        // never reaches nine — a cap no input can hit is a rule that only looks like one, so it is not here.
        const double MinSpacing = 30.0;
        var ruler = AxisRuler.For(0.0, 2.0, "um", lengthPx, MinSpacing);

        var gaps = ruler.Ticks
            .Zip(ruler.Ticks.Skip(1), (a, b) => Math.Abs(b.Fraction - a.Fraction) * lengthPx)
            .ToArray();

        Assert.All(gaps, gap => Assert.True(gap >= MinSpacing, $"marks {gap:0.#} px apart on a {lengthPx} px edge."));
    }

    [Theory]
    [InlineData(8.0, 100)]     // nearest snap is 2, too dense; the next is 5, NOT 4
    [InlineData(2.0, 40)]
    [InlineData(3.7, 400)]
    [InlineData(0.05, 120)]
    [InlineData(50000.0, 200)]
    public void The_gap_between_marks_is_always_one_two_or_five_times_a_power_of_ten(double span, double lengthPx)
    {
        // What makes it a ruler rather than a division. Coarsening by DOUBLING would also fit the edge and would
        // also be "coarser" — and would mark the sample every 4 um, which is not a number anyone reads off one.
        var ruler = AxisRuler.For(0.0, span, "um", lengthPx);

        Assert.True(ruler.Ticks.Count >= 2, "a ruler with fewer than two marks states no scale.");

        var values = Values(ruler);
        for (int i = 1; i < values.Length; i++)
        {
            double gap = values[i] - values[i - 1];
            double decade = Math.Pow(10, Math.Floor(Math.Log10(gap) + 1e-9));
            double mantissa = gap / decade;

            Assert.True(
                Math.Abs(mantissa - 1) < 1e-6 || Math.Abs(mantissa - 2) < 1e-6 || Math.Abs(mantissa - 5) < 1e-6,
                $"marks {gap:G6} apart: {mantissa:0.###} x 10^n is not a number anyone reads off a ruler.");
        }
    }

    [Fact]
    public void The_unit_travels_with_the_marks()
    {
        // The numbers alone are the thing that made 2 um and 2 mm look alike.
        Assert.Equal("nm", AxisRuler.For(0.0, 200.0, "nm", lengthPx: 400).Unit);
    }
}
