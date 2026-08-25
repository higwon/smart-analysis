using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// TASK-FF06: a PSIA spectroscopy image (ImageType 2) reads as a <see cref="ForceCurveDataset"/> — the type the
/// force-curve analysis (A12/A13/A23) has had no way to obtain from a real file. What is a force curve and what is
/// merely a spectrum is decided by the file's own axis flags and units, not by channel names or ordering.
/// </summary>
public sealed class PsiaTiffSpectroscopyTests : IDisposable
{
    private const int Points = 8;
    private const int Float = 2;
    private const int Short = 0;

    private readonly List<string> _files = [];

    private static PsiaTiffReader Reader() => new(StandardUnits.CreateRegistry());

    public void Dispose()
    {
        foreach (var file in _files)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A leftover temp file is not a test failure.
            }
        }
    }

    private string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sa-ff06-{Guid.NewGuid():N}.tiff");
        _files.Add(path);
        return path;
    }

    /// <summary>The 2D header still carries the image type and the payload's element width for a spectroscopy file.</summary>
    private static byte[] ImageHeader(int dataType = Float) => PsiaTiffTestWriter.BuildHeader(
        imageType: 2, sourceName: "Spectroscopy", imageMode: "FD", width: 1, height: 1,
        xScanSize: 1, yScanSize: 1, xOffset: 0, yOffset: 0,
        dataGain: 1, zOffset: 0, unit: "nm", dataType: dataType);

    /// <summary>Packs channel planes back to back — the layout the instrument writes.</summary>
    private static byte[] Planar(int dataType, params double[][] planes)
        => planes.SelectMany(plane => PsiaTiffTestWriter.PackPixels(plane, dataType)).ToArray();

    private static double[] Ramp(double start, double step)
        => Enumerable.Range(0, Points).Select(i => start + (i * step)).ToArray();

    [Fact]
    public async Task A_spectroscopy_file_reads_as_a_force_curve()
    {
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Z Scan", "um", DataGain: 0.5, IsXAxis: true, IsYAxis: false),
                    new("Force", "nN", DataGain: 2.0, IsXAxis: false, IsYAxis: true),
                ],
                dataPoints: Points,
                forceConstant: 26),
            Planar(Float, Ramp(0, 1), Ramp(100, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        using var curve = Assert.IsType<ForceCurveDataset>(result.Dataset);
        Assert.Equal(Points, curve.Length);
        Assert.Equal("um", curve.SeparationChannel.Unit.Symbol);
        Assert.Equal("nN", curve.ForceChannel.Unit.Symbol);
        Assert.Equal(ChannelKind.Force, curve.ForceChannel.Kind);
        Assert.Equal("26", curve.Metadata.Extended["psia.spect.forceConstant_N_per_m"]);
    }

    [Fact]
    public async Task Channel_planes_are_read_whole_rather_than_interleaved()
    {
        // The payload holds every sample of source 0, THEN every sample of source 1. Reading it as if the sources
        // alternated per point still lands inside the buffer and still produces a smooth-looking curve — it just
        // silently interleaves the two channels. These two ramps are far apart precisely so that mistake cannot pass.
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Z Scan", "um", DataGain: 1.0, IsXAxis: true, IsYAxis: false),
                    new("Force", "nN", DataGain: 1.0, IsXAxis: false, IsYAxis: true),
                ],
                dataPoints: Points),
            Planar(Float, Ramp(0, 1), Ramp(1000, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        using var curve = Assert.IsType<ForceCurveDataset>(result.Dataset);
        Assert.Equal(Ramp(0, 1).Select(v => (float)v).ToArray(), curve.Separation.Span.ToArray());
        Assert.Equal(Ramp(1000, 1).Select(v => (float)v).ToArray(), curve.Force.Span.ToArray());
    }

    [Fact]
    public async Task Raw_samples_are_scaled_by_each_channels_own_gain_and_offset()
    {
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(Short),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Z Scan", "um", DataGain: 0.25, IsXAxis: true, IsYAxis: false, Offset: -1),
                    new("Force", "nN", DataGain: 4.0, IsXAxis: false, IsYAxis: true, Offset: 10),
                ],
                dataPoints: Points),
            Planar(Short, Ramp(0, 4), Ramp(0, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        using var curve = Assert.IsType<ForceCurveDataset>(result.Dataset);
        var separation = curve.Separation.Span;
        var force = curve.Force.Span;
        for (int i = 0; i < Points; i++)
        {
            Assert.Equal((float)((i * 4 * 0.25) - 1), separation[i], 5);
            Assert.Equal((float)((i * 4.0) + 10), force[i], 5);
        }
    }

    [Fact]
    public async Task The_axis_flags_decide_which_channel_is_which_not_the_channel_order()
    {
        // Force written first, Z second. Choosing by position (or by guessing from the source name) would swap the
        // two axes and turn the curve inside out.
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Force", "nN", DataGain: 1.0, IsXAxis: false, IsYAxis: true),
                    new("Z Scan", "um", DataGain: 1.0, IsXAxis: true, IsYAxis: false),
                ],
                dataPoints: Points),
            Planar(Float, Ramp(1000, 1), Ramp(0, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        using var curve = Assert.IsType<ForceCurveDataset>(result.Dataset);
        Assert.Equal("Z Scan", curve.SeparationChannel.DisplayName);
        Assert.Equal("Force", curve.ForceChannel.DisplayName);
        Assert.Equal(Ramp(0, 1).Select(v => (float)v).ToArray(), curve.Separation.Span.ToArray());
        Assert.Equal(Ramp(1000, 1).Select(v => (float)v).ToArray(), curve.Force.Span.ToArray());
    }

    [Fact]
    public async Task An_unused_channel_slot_is_never_mistaken_for_an_axis()
    {
        // All eight slots are always written, so the ones past SpectSources hold stale bytes. Here NEITHER declared
        // channel is flagged as the ordinate, so a search that ran past the declared count would settle on slot 5 —
        // a channel with no plane in the payload at all. That has to be a corrupt header, not a read past the data.
        var path = NewPath();
        var lines = new PsiaTiffTestWriter.SpectroscopyLine[8];
        lines[0] = new("Z Scan", "um", DataGain: 1.0, IsXAxis: true, IsYAxis: false);
        lines[1] = new("Force", "nN", DataGain: 1.0, IsXAxis: false, IsYAxis: false);
        lines[5] = new("Stale", "nN", DataGain: 99.0, IsXAxis: false, IsYAxis: true);

        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(lines, dataPoints: Points, sourceCountOverride: 2),
            Planar(Float, Ramp(0, 1), Ramp(1000, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.Corrupt, result.Error!.Kind);
    }

    [Fact]
    public async Task The_micro_sign_a_real_file_writes_resolves_to_the_registrys_micrometre()
    {
        // Real PSIA files write "µm"; the unit registry deliberately holds only the ASCII "um" and leaves such
        // input-file variants to the parser. Without that normalisation a genuine force curve looks dimensionless
        // and is refused as "not a force-distance curve".
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Z Scan", "µm", DataGain: 1.0, IsXAxis: true, IsYAxis: false),
                    new("Force", "nN", DataGain: 1.0, IsXAxis: false, IsYAxis: true),
                ],
                dataPoints: Points),
            Planar(Float, Ramp(0, 1), Ramp(1000, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        using var curve = Assert.IsType<ForceCurveDataset>(result.Dataset);
        Assert.Equal(StandardUnits.Micrometre.Symbol, curve.SeparationChannel.Unit.Symbol);
        Assert.Equal("µm", curve.Metadata.Extended["psia.spect.xUnitRaw"]); // what the file actually said
    }

    [Fact]
    public async Task A_spectrum_that_is_not_force_versus_distance_is_a_typed_refusal()
    {
        // A PiFM/IR sweep is a spectroscopy image too, but wavenumber against amplitude is not a force curve.
        // Presenting it as one would hand nonsense to the contact-mechanics fits.
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("WaveNumber", "cm-1", DataGain: 1.0, IsXAxis: true, IsYAxis: false),
                    new("Lockin3 Amplitude", "V", DataGain: 1.0, IsXAxis: false, IsYAxis: true),
                ],
                dataPoints: Points),
            Planar(Float, Ramp(0, 1), Ramp(1000, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.UnsupportedImageType, result.Error!.Kind);
    }

    [Fact]
    public async Task More_than_one_spectrum_in_a_file_is_refused_rather_than_read_as_the_first()
    {
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Z Scan", "um", DataGain: 1.0, IsXAxis: true, IsYAxis: false),
                    new("Force", "nN", DataGain: 1.0, IsXAxis: false, IsYAxis: true),
                ],
                dataPoints: Points,
                spectroscopyPoints: 3),
            Planar(Float, Ramp(0, 1), Ramp(1000, 1), Ramp(0, 1), Ramp(1000, 1), Ramp(0, 1), Ramp(1000, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.UnsupportedImageType, result.Error!.Kind);
    }

    [Fact]
    public async Task A_payload_smaller_than_the_header_declares_is_truncated_not_a_short_curve()
    {
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Z Scan", "um", DataGain: 1.0, IsXAxis: true, IsYAxis: false),
                    new("Force", "nN", DataGain: 1.0, IsXAxis: false, IsYAxis: true),
                ],
                dataPoints: Points),
            Planar(Float, Ramp(0, 1))); // the force plane is missing entirely

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.Truncated, result.Error!.Kind);
    }

    [Fact]
    public async Task A_spectroscopy_file_without_its_spectroscopy_header_is_corrupt()
    {
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFileWithoutHeader(path, ImageHeader(), Planar(Float, Ramp(0, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.Corrupt, result.Error!.Kind);
    }

    [Fact]
    public async Task A_header_with_no_axis_flag_at_all_is_corrupt()
    {
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Z Scan", "um", DataGain: 1.0, IsXAxis: false, IsYAxis: false),
                    new("Force", "nN", DataGain: 1.0, IsXAxis: false, IsYAxis: true),
                ],
                dataPoints: Points),
            Planar(Float, Ramp(0, 1), Ramp(1000, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.Corrupt, result.Error!.Kind);
    }

    [Fact]
    public async Task A_declared_source_count_past_the_channel_slots_is_corrupt()
    {
        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Z Scan", "um", DataGain: 1.0, IsXAxis: true, IsYAxis: false),
                    new("Force", "nN", DataGain: 1.0, IsXAxis: false, IsYAxis: true),
                ],
                dataPoints: Points,
                sourceCountOverride: 9), // only eight slots exist
            Planar(Float, Ramp(0, 1), Ramp(1000, 1)));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.Corrupt, result.Error!.Kind);
    }
    [Fact]
    public async Task A_curve_read_from_a_file_is_what_the_approach_retract_split_consumes()
    {
        // The point of the whole task: A12/A13/A23 all take a ForceCurveDataset, and until now no reader produced
        // one — the analysis was unreachable from a real file. A V-shaped Z ramp (down, then back up) is what a
        // force–distance measurement is, and the segmentation has to find both phases in what the reader hands it.
        const int half = 32;
        var down = Enumerable.Range(0, half).Select(i => 100.0 - i).ToArray();
        var up = Enumerable.Range(0, half).Select(i => 100.0 - half + 1.0 + i).ToArray();
        var separationRaw = down.Concat(up).ToArray();
        var forceRaw = Enumerable.Range(0, half * 2).Select(i => (double)Math.Abs(half - i)).ToArray();

        var path = NewPath();
        PsiaTiffTestWriter.WriteSpectroscopyFile(
            path,
            ImageHeader(),
            PsiaTiffTestWriter.BuildSpectroscopyHeader(
                [
                    new("Z Scan", "um", DataGain: 0.01, IsXAxis: true, IsYAxis: false),
                    new("Force", "nN", DataGain: 1.0, IsXAxis: false, IsYAxis: true),
                ],
                dataPoints: half * 2),
            Planar(Float, separationRaw, forceRaw));

        var result = await Reader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        using var curve = Assert.IsType<ForceCurveDataset>(result.Dataset);
        var segmentation = ApproachRetractSegmentation.BySeparationTrend(curve.Separation.Span);

        Assert.Equal(SegmentKind.Approach, segmentation.KindAt(0));
        Assert.Equal(SegmentKind.Retract, segmentation.KindAt(curve.Length - 1));
        Assert.True(segmentation.CountOf(SegmentKind.Approach) > 1);
        Assert.True(segmentation.CountOf(SegmentKind.Retract) > 1);
    }

}
