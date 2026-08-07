using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// Reads the committed <b>real</b> PSIA-TIFF fixture (a tiny installer demo crop) end-to-end — a
/// real-file read regression guard that runs in CI without any external directory (ADR-015). Pinned
/// values are this reader's current output (behavior lock); legacy numeric parity is MV00/T01.
/// </summary>
public sealed class PsiaTiffFixtureTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    [Fact]
    public async Task Reads_the_committed_cheese_fixture()
    {
        Assert.True(File.Exists(FixturePath), $"Fixture missing: {FixturePath}");

        var reader = new PsiaTiffReader(StandardUnits.CreateRegistry());
        var result = await reader.ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        using var image = Assert.IsType<ScanImageDataset>(result.Dataset);

        // Shape + axes (real file: 15x15 topography, µm).
        Assert.Equal(15, image.X.Count);
        Assert.Equal(15, image.Y.Count);
        Assert.Equal("um", image.X.Unit.Symbol);
        Assert.Equal(0.07947, image.X.Step, 4);

        // Channel.
        Assert.Equal(ChannelKind.Topography, image.Channel.Kind);
        Assert.Equal("um", image.Channel.Unit.Symbol);

        // First physical value (raw*DataGain + ZOffset), behavior-locked.
        Assert.Equal(225, image.Data.Memory.Length);
        Assert.Equal(0.244145, image.Data.Memory.Span[0], 4);

        // Lineage/source (ADR-013).
        Assert.True(image.Provenance.IsRoot);
        Assert.Equal("psia-tiff", image.Source.FormatId);
        Assert.False(string.IsNullOrEmpty(image.Source.ContentHash));
    }
}
