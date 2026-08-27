using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Application;

/// <summary>
/// TASK-UX08 (doc 26 §22.6): a volume image's settings are statements about a curve, so they have to be
/// visible on the curve they are read from. This is the seam that says where they fall — and it has to be the
/// same computation the picture is built from, or the line drawn on the curve becomes a plausible guess.
/// </summary>
public sealed class SpectroscopyParameterPreviewTests
{
    private const int Samples = 20;
    private const float Contact = 8f;

    private static ISpectroscopyParameterPreview Preview() => new SpectroscopyParameterPreviewUseCase();

    /// <summary>
    /// One point, shaped like a real round trip: flat and out of contact beyond <see cref="Contact"/>, a
    /// quadratic push inside it, and a pull-off on the retract only. Everything sits on <paramref name="offset"/>.
    /// </summary>
    private static ForceVolumeDataset Map(float offset = 0f, bool oneWay = false)
    {
        const int points = 2;
        const int half = Samples / 2;
        var separation = new float[points * Samples];
        var force = new float[points * Samples];

        for (int p = 0; p < points; p++)
        {
            for (int i = 0; i < Samples; i++)
            {
                float z = i < half ? half - i : (oneWay ? half - i : i - half + 1);
                separation[(p * Samples) + i] = z;
                float push = z >= Contact ? 0f : (Contact - z) * (Contact - z);
                bool pullOff = i >= half && !oneWay && z == Contact;
                force[(p * Samples) + i] = offset + (pullOff ? -5f : push);
            }
        }

        return new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, Samples, points),
            ScanBuffer<float>.TakeOwnership(force, Samples, points),
            new ChannelDescriptor("separation", ChannelKind.Topography, StandardUnits.Nanometre, "Z"),
            new ChannelDescriptor("force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            new ForceVolumeGeometry(2, 1, 2.0, 1.0, 0.0, 0.0, StandardUnits.Micrometre),
            ScanMetadata.Unknown, ProvenanceRecord.Root);
    }

    [Fact]
    public void The_marks_sit_on_the_curve_as_drawn_not_on_a_baseline_corrected_copy()
    {
        // The plot shows raw force. A threshold is a percentage of the peak ABOVE the baseline, so the line has
        // to be converted back into absolute force or it lands nowhere near the curve the user is looking at.
        using var map = Map(offset: 267f);

        var w = Preview().Locate(map, 0, phaseIsApproach: true, thresholdPercent: 50.0, baselinePercent: 20.0)!.Value;

        Assert.Equal(267.0, w.Baseline, 3);
        // The approach runs z = 10 down to 1, so the deepest push is (8-1)^2 = 49 above the baseline.
        Assert.Equal(267.0 + (49.0 * 0.5), w.ThresholdForce, 3);
        Assert.Equal(1.0, w.PeakSeparation, 3);
    }

    [Fact]
    public void An_offset_moves_the_marks_with_the_curve_and_nothing_else()
    {
        using var atZero = Map();
        using var shifted = Map(offset: 267f);

        var a = Preview().Locate(atZero, 0, true, 50.0, 20.0)!.Value;
        var b = Preview().Locate(shifted, 0, true, 50.0, 20.0)!.Value;

        Assert.Equal(a.ThresholdForce + 267.0, b.ThresholdForce, 3);
        Assert.Equal(a.PeakSeparation, b.PeakSeparation, 3);
        Assert.Equal(a.WindowSeparation, b.WindowSeparation, 3);
    }

    [Fact]
    public void A_point_with_no_run_of_the_asked_for_half_has_nothing_to_mark()
    {
        // The same point whose pixel is a hole. Drawing marks anyway would explain a picture that is not there.
        using var map = Map(oneWay: true);

        Assert.Null(Preview().Locate(map, 0, phaseIsApproach: false, 50.0, 20.0));
    }

    [Fact]
    public void A_setting_outside_its_range_marks_nothing_rather_than_throwing()
    {
        // The panel already refuses it and says so; the curve simply has nothing to draw.
        using var map = Map();

        Assert.Null(Preview().Locate(map, 0, true, 50.0, baselinePercent: 500.0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void A_point_the_map_does_not_have_marks_nothing(int point)
    {
        using var map = Map();

        Assert.Null(Preview().Locate(map, point, true, 50.0, 20.0));
    }

    [Fact]
    public async Task The_window_is_the_one_the_picture_was_measured_over()
    {
        // The whole point of drawing it. If this seam picked a different half, or a different threshold edge,
        // the line would explain a number the pixel does not hold.
        using var map = Map(offset: 267f);
        var w = Preview().Locate(map, 0, phaseIsApproach: true, thresholdPercent: 50.0, baselinePercent: 20.0)!.Value;

        var result = await RunVolumeAsync(map, VolumeMeasure.Deformation, CurvePhase.Approach);
        using var image = (ScanImageDataset)result.DerivedDataset!;

        Assert.Equal(image.Data.Memory.Span[0], Math.Abs(w.PeakSeparation - w.WindowSeparation), 4);
    }

    private static Task<OperationResult> RunVolumeAsync(
        ForceVolumeDataset map, VolumeMeasure measure, CurvePhase phase)
        => new VolumeImageOperation(new FixedEnvironment()).RunAsync(
            new OperationInput(map),
            new ParameterSet(new Dictionary<string, object?>
            {
                [VolumeImageOperation.MeasureParameter] = measure,
                [VolumeImageOperation.PhaseParameter] = phase,
                [VolumeImageOperation.ThresholdParameter] = 50.0,
                [VolumeImageOperation.BaselineParameter] = 20.0,
            }),
            null,
            CancellationToken.None);

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => new("test", "1.0", "test", DateTimeOffset.UnixEpoch);
    }
}
