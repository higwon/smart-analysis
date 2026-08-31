using System.Globalization;

namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// One mark on a ruler: where it sits along the edge as a fraction of the visible span, and what it is called.
/// <para>
/// A fraction rather than a pixel, because how long the edge is on screen is the control's business and how far
/// along it a value sits is not. It also survives a reversed axis without a special case: a scan recorded top-down
/// gives <c>from &gt; to</c>, and the same subtraction places the ticks the right way round.
/// </para>
/// </summary>
public readonly record struct AxisTick(double Fraction, string Label);

/// <summary>What to draw along one edge of an image: the marks, and the unit they are counted in.</summary>
public sealed record RulerTicks(IReadOnlyList<AxisTick> Ticks, string Unit)
{
    public static RulerTicks None { get; } = new([], string.Empty);
}

/// <summary>
/// Where to put the marks on an image's rulers.
/// <para>
/// A picture of a surface with no stated size is a picture of a texture: 2 µm across and 2 mm across look
/// identical on screen, and nothing on the MVP stage said which. Legacy answers this with rulers along the left
/// and bottom edges — not a scale bar over the image — and this follows that, including its rule for choosing the
/// step (<c>RulerHelper.CalcTickRange2</c>).
/// </para>
/// <para>
/// The span asked for is the <b>visible</b> one, not the whole image: a ruler that keeps describing the full
/// extent while the viewer is zoomed into a corner is a caption for a picture that is no longer there.
/// </para>
/// </summary>
public static class AxisRuler
{
    /// <summary>Legacy aims for four intervals before it starts coarsening (<c>tickcount = 4</c>).</summary>
    private const int TargetIntervals = 4;

    /// <summary>
    /// A loop guard, not a policy. What actually limits the marks is the coarsening below — legacy also caps at
    /// nine, but under this rule the count never reaches it, and a cap no input can hit is a rule that only
    /// looks like one.
    /// </summary>
    private const int Guard = 64;

    /// <summary>
    /// The marks for an edge running from <paramref name="from"/> to <paramref name="to"/> in
    /// <paramref name="unit"/>, over <paramref name="lengthPx"/> pixels of screen.
    /// </summary>
    public static RulerTicks For(
        double from,
        double to,
        string unit,
        double lengthPx,
        double minLabelSpacingPx = 30.0)
    {
        if (!double.IsFinite(from) || !double.IsFinite(to) || !(lengthPx > 0) || !(minLabelSpacingPx > 0))
        {
            return RulerTicks.None;
        }

        double low = Math.Min(from, to);
        double high = Math.Max(from, to);
        double span = high - low;
        if (!(span > 0))
        {
            // An axis that covers no distance has one place, not a scale. Marking it would suggest otherwise.
            return RulerTicks.None;
        }

        double step = Step(span, lengthPx, minLabelSpacingPx);
        if (!(step > 0))
        {
            return RulerTicks.None;
        }

        string format = Format(span);
        var ticks = new List<AxisTick>();

        // Start on the step grid at or below the low end, so the marks land on round numbers rather than on
        // wherever the image happens to begin.
        double first = Math.Floor(low / step) * step;
        double epsilon = span / 1e6;
        for (int i = 0; i < Guard; i++)
        {
            double value = Significant(first + (i * step), 6);
            if (value < low - epsilon)
            {
                continue;
            }

            if (value > high + epsilon)
            {
                break;
            }

            ticks.Add(new AxisTick(
                (value - from) / (to - from),
                value.ToString(format, CultureInfo.InvariantCulture)));
        }

        return new RulerTicks(ticks, unit ?? string.Empty);
    }

    /// <summary>
    /// The step, snapped to 1, 2, 5 or 10 times a power of ten — the same rule legacy uses. Snapping matters
    /// more than hitting the target count: a ruler marked every 0.163 µm is arithmetic, not a scale.
    /// </summary>
    private static double Step(double span, double lengthPx, double minLabelSpacingPx)
    {
        double raw = span / TargetIntervals;
        double decade = Math.Pow(10, Math.Floor(Math.Log10(raw)));

        double step = decade;
        double best = double.PositiveInfinity;
        foreach (double mantissa in new[] { 1.0, 2.0, 5.0, 10.0 })
        {
            double distance = Math.Abs(raw - (mantissa * decade));
            if (distance < best)
            {
                best = distance;
                step = mantissa * decade;
            }
        }

        // Then coarser and coarser through the 1-2-5 sequence until the labels are not packed tighter than they
        // can be read. Legacy tries four candidates and gives up, drawing nothing; walking on through the decades
        // instead keeps a short edge marked rather than blank, which is the whole point of having a ruler on it.
        double affordable = lengthPx / minLabelSpacingPx;
        for (int i = 0; i < 32 && step > 0; i++)
        {
            if (span / step <= affordable)
            {
                return step;
            }

            step = Coarser(step);
        }

        return 0;
    }

    /// <summary>The next value up the 1-2-5 sequence: 1 → 2 → 5 → 10 → 20 → …</summary>
    private static double Coarser(double step)
    {
        double decade = Math.Pow(10, Math.Floor(Math.Log10(step) + 1e-9));
        double mantissa = step / decade;
        return mantissa < 1.5 ? 2 * decade
            : mantissa < 3.5 ? 5 * decade
            : 10 * decade;
    }

    /// <summary>
    /// How many decimals, decided by the <b>span</b> rather than by each value — so the marks on one ruler are
    /// written the same way and can be compared at a glance. Legacy's table.
    /// </summary>
    private static string Format(double span) => span switch
    {
        < 0.001 => "0.#####E0",
        < 0.01 => "F4",
        < 0.1 => "F3",
        < 1 => "F2",
        < 10 => "F1",
        > 10000 => "0.#####E0",
        _ => "F0",
    };

    // Accumulating a step is not exact, and 0.30000000000000004 would be written out in full by "F#####E0".
    private static double Significant(double value, int digits)
    {
        if (value == 0.0 || !double.IsFinite(value))
        {
            return value;
        }

        double magnitude = Math.Pow(10, digits - (int)Math.Ceiling(Math.Log10(Math.Abs(value))));
        return Math.Round(value * magnitude) / magnitude;
    }
}
