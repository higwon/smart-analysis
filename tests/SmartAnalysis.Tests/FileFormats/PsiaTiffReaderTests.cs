using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// TASK-FF01: the PSIA-TIFF reader (ADR-015). Deterministic synthetic PSIA-TIFFs (hand-written by
/// <see cref="PsiaTiffTestWriter"/>) are parsed by the real TiffLibrary via <see cref="PsiaTiffReader"/>,
/// validating tag IO, header parse, endianness, pixel decode, and the domain mapping end-to-end — no
/// committed binary fixture. Real-file legacy parity stays env-gated (MV00/T01), out of this PR.
/// </summary>
public sealed class PsiaTiffReaderTests : IDisposable
{
    private readonly IUnitRegistry _units = StandardUnits.CreateRegistry();
    private readonly List<string> _temp = [];

    private PsiaTiffReader NewReader() => new(_units);

    private string TempPath()
    {
        // No Path.GetTempFileName randomness needed beyond a unique name; use a GUID-free counter.
        string path = Path.Combine(Path.GetTempPath(), $"psia_test_{Guid.NewGuid():N}.tiff");
        _temp.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var p in _temp)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort cleanup */ }
        }
    }

    private string WriteScan(
        int width, int height, int dataType, double[] raw,
        double xScanSize = 10.0, double yScanSize = 8.0, double xOffset = 1.0, double yOffset = 2.0,
        double dataGain = 0.5, double zOffset = 100.0, string unit = "nm",
        string sourceName = "Height", string imageMode = "AFM", int imageType = 0)
    {
        var header = PsiaTiffTestWriter.BuildHeader(
            imageType, width, height, xScanSize, yScanSize, xOffset, yOffset, dataGain, zOffset, unit, sourceName, imageMode, dataType);
        var data = PsiaTiffTestWriter.PackPixels(raw, dataType);
        var path = TempPath();
        PsiaTiffTestWriter.WriteFile(path, header, data);
        return path;
    }

    // --- Happy path: 2D scan image → ScanImageDataset with correct axes/units/values/provenance ---

    [Fact]
    public async Task Reads_a_2d_short_scan_image_with_correct_axes_units_and_values()
    {
        // 3x2 short image; physical = raw*0.5 + 100.
        double[] raw = [0, 10, 20, 30, 40, 50];
        var path = WriteScan(width: 3, height: 2, dataType: 0, raw: raw);

        var result = await NewReader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        using var dataset = Assert.IsType<ScanImageDataset>(result.Dataset);

        // Axes: raw[0..W] → real[0..ScanSize] µm.
        Assert.Equal("um", dataset.X.Unit.Symbol);
        Assert.Equal(3, dataset.X.Count);
        Assert.Equal(1.0, dataset.X.Origin);
        Assert.Equal(10.0 / 3.0, dataset.X.Step, 10);
        Assert.Equal(2, dataset.Y.Count);
        Assert.Equal(8.0 / 2.0, dataset.Y.Step, 10);

        // Channel: unit from header, kind inferred from the unit's dimension (nm → Length → Topography).
        Assert.Equal("nm", dataset.Channel.Unit.Symbol);
        Assert.Equal(ChannelKind.Topography, dataset.Channel.Kind);
        Assert.Equal("Height", dataset.Channel.Key);

        // Values: physical = raw*DataGain + ZOffset.
        var span = dataset.Data.Memory.Span;
        Assert.Equal(6, span.Length);
        double[] expected = [100, 105, 110, 115, 120, 125];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], span[i], 4);
        }

        // Lineage/source (ADR-013): original/root; file origin + content hash live on Source.
        Assert.True(dataset.Provenance.IsRoot);
        Assert.Equal("psia-tiff", dataset.Source.FormatId);
        Assert.Equal(path, dataset.Source.OriginalFilePath);
        Assert.False(string.IsNullOrEmpty(dataset.Source.ContentHash));
    }

    [Theory]
    [InlineData(1)] // int
    [InlineData(2)] // float
    public async Task Reads_int_and_float_data_types(int dataType)
    {
        double[] raw = [1, 2, 3, 4];
        var path = WriteScan(width: 2, height: 2, dataType: dataType, raw: raw, dataGain: 2.0, zOffset: 0.0);

        var result = await NewReader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        using var dataset = Assert.IsType<ScanImageDataset>(result.Dataset);
        var span = dataset.Data.Memory.Span;
        double[] expected = [2, 4, 6, 8];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], span[i], 4);
        }
    }

    [Fact]
    public async Task Unknown_unit_symbol_falls_back_to_dimensionless_unknown_channel()
    {
        var path = WriteScan(width: 1, height: 1, dataType: 2, raw: [1.0], unit: "zzz");

        var result = await NewReader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        using var dataset = Assert.IsType<ScanImageDataset>(result.Dataset);
        Assert.Equal("1", dataset.Channel.Unit.Symbol); // dimensionless fallback
        Assert.Equal(ChannelKind.Unknown, dataset.Channel.Kind);
    }

    // --- Typed failures (values, not exceptions) ---

    [Fact]
    public async Task Missing_magic_tag_is_not_psia_tiff()
    {
        var header = PsiaTiffTestWriter.BuildHeader(0, 2, 2, 10, 10, 0, 0, 1, 0, "nm", "Height", "AFM", 2);
        var data = PsiaTiffTestWriter.PackPixels([1, 2, 3, 4], 2);
        var path = TempPath();
        PsiaTiffTestWriter.WriteFile(path, header, data, includeMagic: false);

        var result = await NewReader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.NotPsiaTiff, result.Error!.Kind);
    }

    [Fact]
    public async Task Short_header_is_corrupt()
    {
        var path = TempPath();
        PsiaTiffTestWriter.WriteFile(path, new byte[100], PsiaTiffTestWriter.PackPixels([1], 2));

        var result = await NewReader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.Corrupt, result.Error!.Kind);
    }

    [Theory]
    [InlineData(1)] // line profile
    [InlineData(2)] // spectroscopy
    public async Task Non_2d_image_types_route_to_unsupported(int imageType)
    {
        var path = WriteScan(width: 2, height: 2, dataType: 2, raw: [1, 2, 3, 4], imageType: imageType);

        var result = await NewReader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.UnsupportedImageType, result.Error!.Kind);
    }

    [Fact]
    public async Task Truncated_pixel_payload_is_reported()
    {
        // Header declares 3x2 short (12 bytes) but only 4 bytes of pixel data are written.
        var header = PsiaTiffTestWriter.BuildHeader(0, 3, 2, 10, 8, 0, 0, 1, 0, "nm", "Height", "AFM", 0);
        var path = TempPath();
        PsiaTiffTestWriter.WriteFile(path, header, new byte[4]);

        var result = await NewReader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.Truncated, result.Error!.Kind);
    }

    [Fact]
    public async Task Missing_file_is_io_error()
    {
        var result = await NewReader().ReadAsync(
            Path.Combine(Path.GetTempPath(), "does_not_exist_psia.tiff"), ScanReadOptions.Default, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileReadErrorKind.Io, result.Error!.Kind);
    }

    // --- CanRead + DI wiring ---

    [Theory]
    [InlineData("scan.tiff", true)]
    [InlineData("scan.TIF", true)]
    [InlineData("scan.png", false)]
    [InlineData("", false)]
    public void CanRead_matches_tiff_extensions(string path, bool expected)
        => Assert.Equal(expected, NewReader().CanRead(path)); // a non-existent path can only be judged by its name

    [Fact]
    public void CanRead_accepts_a_real_tiff_whatever_it_is_named()
    {
        // FF05: identification is by content. A TIFF saved as ".dat" is still a TIFF, and the legacy
        // extension-only check would have refused it.
        var renamed = Path.Combine(Path.GetTempPath(), $"sa-canread-{Guid.NewGuid():N}.dat");
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");
        File.Copy(fixture, renamed, overwrite: true);
        try
        {
            Assert.True(NewReader().CanRead(renamed));
        }
        finally
        {
            File.Delete(renamed);
        }
    }

    [Fact]
    public void CanRead_refuses_a_file_that_is_merely_named_tiff()
    {
        // The other half: the name says TIFF but the bytes do not, so this reader must not claim it.
        var fake = Path.Combine(Path.GetTempPath(), $"sa-canread-{Guid.NewGuid():N}.tiff");
        File.WriteAllText(fake, "not a scan at all");
        try
        {
            Assert.False(NewReader().CanRead(fake));
        }
        finally
        {
            File.Delete(fake);
        }
    }

    [Fact]
    public void Registers_via_di_and_binds_the_port()
    {
        using var provider = new ServiceCollection().AddPsiaTiffReader().BuildServiceProvider();

        var reader = provider.GetRequiredService<IScanFileReader>();

        Assert.IsType<PsiaTiffReader>(reader);
        Assert.NotNull(provider.GetService<IUnitRegistry>());
    }
}
