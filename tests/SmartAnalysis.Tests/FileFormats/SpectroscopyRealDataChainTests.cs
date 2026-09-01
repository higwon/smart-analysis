using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// TASK-T01: the spectroscopy analysis chain over <b>real</b> force curves, env-gated the way the reader's own
/// real-sample tests are (ADR-015).
/// <para>
/// Every spectroscopy fixture in this repository is synthetic, and this session's defects were found by a person
/// looking at a screenshot of real data — an adhesion map that came out uniformly zero (A40), a threshold box that
/// changed nothing (U10). Synthetic curves are shaped the way the code expects, which is exactly why they could
/// not tell two behaviours apart. The real corpus is shaped differently: on <c>FD on PR pattern</c> the pull-off
/// adhesion is larger than the peak push, which no fixture here produces.
/// </para>
/// <para>
/// These are the <b>single-curve</b> properties: the measures, the threshold window and the baseline heuristic,
/// which are about the shape of a curve rather than about a map. The map this file builds is a grid of real
/// curves — real shapes on an arrangement made up here — which is enough for what a volume pixel is (one measure
/// of one curve) and deliberately NOT enough for anything about layout. A real force-volume map, with the reader,
/// the geometry and the acquisition order that come with it, is covered in <see cref="RealForceVolumeMapTests"/>.
/// </para>
/// <para>
/// What can be asserted on data whose right answers are unknown is limited to properties that must hold for any
/// curve. The baseline heuristic is <b>reported</b> rather than asserted, so a human can see whether it fits real
/// acquisitions instead of having a passing threshold invented here and quietly enshrined.
/// </para>
/// </summary>
public sealed class SpectroscopyRealDataChainTests(ITestOutputHelper output)
{
    private const double Threshold = 50.0;
    private const double Baseline = ForceDistanceMeasures.DefaultBaselinePercent;

    /// <summary>A curve read from a real file, copied out so the dataset's buffers can be released at once.</summary>
    private sealed record RealCurve(
        string Name,
        float[] Separation,
        float[] Force,
        ChannelDescriptor SeparationChannel,
        ChannelDescriptor ForceChannel);

    private static string? SamplesRoot()
    {
        var env = Environment.GetEnvironmentVariable("SMARTANALYSIS_TIFF_SAMPLES_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            return env;
        }

        const string def = @"C:\Park Systems\SmartAnalysis 2.0\Samples";
        return Directory.Exists(def) ? def : null;
    }

    // Read ONCE for the whole class. Every TIFF under the samples root is opened to find the force curves among
    // them — on this machine 142 files for 5 curves — and doing that per test spends most of the run re-reading
    // the same disk. The curves are copied out of their datasets anyway, so there is nothing to share but arrays.
    private static readonly Lazy<Task<List<RealCurve>>> Corpus = new(ReadCurvesAsync);

    private static Task<List<RealCurve>> CurvesAsync() => Corpus.Value;

    private static async Task<List<RealCurve>> ReadCurvesAsync()
    {
        var curves = new List<RealCurve>();
        if (SamplesRoot() is not { } root)
        {
            return curves;
        }

        var reader = new PsiaTiffReader(StandardUnits.CreateRegistry());
        foreach (var path in Directory.EnumerateFiles(root, "*.tif*", SearchOption.AllDirectories).OrderBy(p => p))
        {
            FileReadResult result;
            try
            {
                result = await reader.ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);
            }
            catch
            {
                // A sample tree is whatever happens to be on the machine. One file this reader cannot open is
                // not a reason to report no corpus at all.
                continue;
            }

            if (result.Dataset is ForceCurveDataset curve)
            {
                curves.Add(new RealCurve(
                    Path.GetFileNameWithoutExtension(path),
                    curve.Separation.Memory.Span.ToArray(),
                    curve.Force.Memory.Span.ToArray(),
                    curve.SeparationChannel,
                    curve.ForceChannel));
            }

            (result.Dataset as IDisposable)?.Dispose();
        }

        return curves;
    }

    /// <summary>The largest set of real curves sharing a sample count, so they can be laid out on one grid.</summary>
    private static async Task<IReadOnlyList<RealCurve>> SameLengthCurvesAsync()
        => (await CurvesAsync())
            .GroupBy(c => c.Force.Length)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()
            ?.ToArray() ?? [];

    private static ForceVolumeDataset MapOf(IReadOnlyList<RealCurve> curves, float forceOffset = 0f)
    {
        int samples = curves[0].Force.Length;
        var separation = new float[curves.Count * samples];
        var force = new float[curves.Count * samples];
        for (int p = 0; p < curves.Count; p++)
        {
            for (int i = 0; i < samples; i++)
            {
                separation[(p * samples) + i] = curves[p].Separation[i];
                force[(p * samples) + i] = curves[p].Force[i] + forceOffset;
            }
        }

        return new ForceVolumeDataset(
            DatasetId.New(),
            new DataSource("real-samples", null),
            ScanBuffer<float>.TakeOwnership(separation, samples, curves.Count),
            ScanBuffer<float>.TakeOwnership(force, samples, curves.Count),
            curves[0].SeparationChannel,
            curves[0].ForceChannel,
            new ForceVolumeGeometry(curves.Count, 1, 3.0, 1.0, -1.5, -0.5, StandardUnits.Micrometre),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }

    private static Task<OperationResult> RunAsync(ForceVolumeDataset map, VolumeMeasure measure, CurvePhase phase)
        => new VolumeImageOperation(new FixedEnvironment()).RunAsync(
            new OperationInput(map),
            new ParameterSet(new Dictionary<string, object?>
            {
                [VolumeImageOperation.MeasureParameter] = measure,
                [VolumeImageOperation.PhaseParameter] = phase,
                [VolumeImageOperation.ThresholdParameter] = Threshold,
                [VolumeImageOperation.BaselineParameter] = Baseline,
            }),
            null,
            CancellationToken.None);

    /// <summary>
    /// Equal to within float32 noise. On this corpus a detector offset moves a measure by at most 6e-5 of its own
    /// size, while a real dependence on the offset would move it by order 1 — four orders of margin either way.
    /// </summary>
    private static void Same(double a, double b, string what)
    {
        double tolerance = Math.Max(1e-9, Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-3);
        Assert.True(Math.Abs(a - b) <= tolerance, $"{what}: {a:G6} vs {b:G6}");
    }

    private bool Skip(int found, [System.Runtime.CompilerServices.CallerMemberName] string test = "")
    {
        if (found > 0)
        {
            return false;
        }

        output.WriteLine($"{test}: no real force curves available — skipping.");
        return true;
    }

    [Fact]
    public async Task Every_real_curve_yields_a_measure_that_is_a_number()
    {
        // The floor. A measure that comes out NaN on a real acquisition is a hole in the picture, and no synthetic
        // fixture would say so — they are all shaped to have the thing being looked for.
        var curves = await CurvesAsync();
        if (Skip(curves.Count))
        {
            return;
        }

        foreach (var c in curves)
        {
            var m = ForceDistanceMeasures.Of(c.Force, c.Separation, Threshold, Baseline);
            output.WriteLine(
                $"{c.Name} ({c.Force.Length} samples): max={m.MaxForce:G6} adhesion={m.Adhesion:G6} "
                + $"stiffness={m.Stiffness:G6} deformation={m.Deformation:G6}");

            Assert.False(m.HasNonFiniteSamples, $"{c.Name}: the file itself carries non-finite samples.");
            Assert.True(double.IsFinite(m.MaxForce), $"{c.Name}: max force.");
            Assert.True(double.IsFinite(m.Adhesion), $"{c.Name}: adhesion.");
            Assert.True(double.IsFinite(m.Stiffness), $"{c.Name}: stiffness.");
            Assert.True(double.IsFinite(m.Deformation), $"{c.Name}: deformation.");
        }
    }

    [Fact]
    public async Task A_detector_offset_moves_no_measure_of_a_real_curve()
    {
        // The property A40 rests on, against curves nobody shaped to suit it. The defect it closed was found on a
        // real file precisely because every synthetic curve here happened to sit on zero already.
        var curves = await CurvesAsync();
        if (Skip(curves.Count))
        {
            return;
        }

        foreach (var c in curves)
        {
            var shifted = c.Force.Select(f => f + 5000f).ToArray();
            var a = ForceDistanceMeasures.Of(c.Force, c.Separation, Threshold, Baseline);
            var b = ForceDistanceMeasures.Of(shifted, c.Separation, Threshold, Baseline);

            Same(a.MaxForce, b.MaxForce, $"{c.Name} max force");
            Same(a.Adhesion, b.Adhesion, $"{c.Name} adhesion");
            Same(a.Stiffness, b.Stiffness, $"{c.Name} stiffness");
            Same(a.Deformation, b.Deformation, $"{c.Name} deformation");
        }
    }

    [Fact]
    public async Task Asking_for_a_deeper_threshold_narrows_the_window_on_a_real_curve()
    {
        // U10 was a threshold box that changed nothing. Pushing the threshold deeper must actually narrow the
        // window — the peak edge is fixed and the other only moves toward it. Asserted strictly, so a threshold
        // that is read and then ignored fails here rather than passing on equal widths.
        var curves = await CurvesAsync();
        if (Skip(curves.Count))
        {
            return;
        }

        double[] thresholds = [10.0, 25.0, 50.0, 75.0, 90.0];
        foreach (var c in curves)
        {
            var widths = thresholds
                .Select(t => ForceDistanceMeasures.Of(c.Force, c.Separation, t, Baseline).Deformation)
                .ToArray();

            output.WriteLine($"{c.Name}: {string.Join(" > ", widths.Select(w => w.ToString("G4")))}");
            for (int i = 1; i < widths.Length; i++)
            {
                Assert.True(
                    widths[i] < widths[i - 1],
                    $"{c.Name}: threshold {thresholds[i]}% opened no narrower a window than {thresholds[i - 1]}%.");
            }
        }
    }

    [Fact]
    public async Task The_peak_of_a_real_curve_is_where_it_is_whatever_the_threshold()
    {
        // The threshold picks the far edge of the window, not the near one. A peak that drifted with the threshold
        // would mean both edges are found by the same rule — the shape LD-16 describes.
        var curves = await CurvesAsync();
        if (Skip(curves.Count))
        {
            return;
        }

        foreach (var c in curves)
        {
            double at10 = ForceDistanceMeasures.Of(c.Force, c.Separation, 10.0, Baseline).PeakSeparation;
            double at90 = ForceDistanceMeasures.Of(c.Force, c.Separation, 90.0, Baseline).PeakSeparation;

            Same(at10, at90, $"{c.Name} peak separation");
        }
    }

    [Fact]
    public async Task A_picture_of_real_curves_holds_the_number_the_marks_explain()
    {
        // The seam UX08 added, against real curves: the window the Inspector draws must be the window the pixel was
        // measured over, or the line on the screen explains a number the picture does not hold.
        var curves = await SameLengthCurvesAsync();
        if (Skip(curves.Count))
        {
            return;
        }

        using var map = MapOf(curves);
        using var image = (ScanImageDataset)
            (await RunAsync(map, VolumeMeasure.Deformation, CurvePhase.Approach)).DerivedDataset!;
        var pixels = image.Data.Memory.Span;

        var preview = new SpectroscopyParameterPreviewUseCase();
        int cross = 0;
        for (int p = 0; p < curves.Count; p++)
        {
            if (!float.IsFinite(pixels[p])
                || preview.Locate(map, p, phaseIsApproach: true, Threshold, Baseline) is not { } w)
            {
                continue;
            }

            Same(pixels[p], Math.Abs(w.PeakSeparation - w.WindowSeparation), $"{curves[p].Name} pixel vs marks");
            cross++;
        }

        output.WriteLine($"{curves.Count} real curves, {cross} cross-checked against the picture");
        Assert.True(cross > 0, "no pixel of a picture of real curves could be cross-checked against its marks.");
    }

    [Fact]
    public async Task A_detector_offset_moves_no_pixel_of_a_picture_of_real_curves()
    {
        // The same invariance one layer up, through segmentation and the grid — the path the screenshot went down.
        var curves = await SameLengthCurvesAsync();
        if (Skip(curves.Count))
        {
            return;
        }

        foreach (var measure in Enum.GetValues<VolumeMeasure>())
        {
            using var map = MapOf(curves);
            using var shifted = MapOf(curves, forceOffset: 5000f);
            using var a = (ScanImageDataset)(await RunAsync(map, measure, CurvePhase.Approach)).DerivedDataset!;
            using var b = (ScanImageDataset)(await RunAsync(shifted, measure, CurvePhase.Approach)).DerivedDataset!;

            var x = a.Data.Memory.Span;
            var y = b.Data.Memory.Span;
            for (int i = 0; i < x.Length; i++)
            {
                Assert.Equal(float.IsFinite(x[i]), float.IsFinite(y[i]));
                if (float.IsFinite(x[i]))
                {
                    Same(x[i], y[i], $"{measure} at {curves[i].Name}");
                }
            }
        }
    }

    [Fact]
    public async Task How_the_baseline_heuristic_fits_real_curves()
    {
        // Reported, not asserted. Reading the non-contact level off the far fifth of the travel is a judgement
        // about how these instruments record, and the honest thing is to show how often it holds rather than to
        // invent a passing threshold for it here.
        var curves = await CurvesAsync();
        if (Skip(curves.Count))
        {
            return;
        }

        foreach (var c in curves)
        {
            var m = ForceDistanceMeasures.Of(c.Force, c.Separation, Threshold, Baseline);
            output.WriteLine(
                $"{c.Name}: baseline={m.Baseline:G6} "
                + (m.BaselineIsFlat ? "flat" : "SLOPING — no non-contact level at the far end") + ", "
                + (m.LooksLikeRoundTrip ? "round trip" : "ONE WAY — no retract to find adhesion on"));
        }
    }

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }
}
