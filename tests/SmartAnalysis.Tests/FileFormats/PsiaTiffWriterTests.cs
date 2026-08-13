using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Analysis.Filtering;
using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// FF02 — the PSIA-TIFF writer. Round-trips a written dataset through the real <see cref="PsiaTiffReader"/>:
/// pixels/axes/channel via the PSIA header, and identity + provenance (F05) via the embedded
/// <c>ImageDescription</c> side-car. A file without the side-car keeps the reader's legacy Root behaviour.
/// </summary>
public sealed class PsiaTiffWriterTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static PsiaTiffReader NewReader() => new(StandardUnits.CreateRegistry());

    private static async Task<ScanImageDataset> LoadFixtureAsync()
    {
        var read = await NewReader().ReadAsync(FixturePath, ScanReadOptions.Default, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(read.Dataset);
    }

    // A real derived dataset: run a spatial filter so the result carries a genuine provenance step
    // (operation id + recorded parameters + environment), not a hand-built one.
    private static async Task<ScanImageDataset> DerivedAsync(ScanImageDataset source)
    {
        var op = new SpatialFilterOperation(new SystemExecutionEnvironmentProvider());
        var parameters = new ParameterSet(new Dictionary<string, object?>
        {
            [SpatialFilterOperation.KindParameter] = FilterKind.Mean,
            [SpatialFilterOperation.SizeParameter] = 3,
        });
        var result = await op.RunAsync(new OperationInput(source), parameters, null, CancellationToken.None);
        return Assert.IsType<ScanImageDataset>(result.DerivedDataset);
    }

    private static string TempTiffPath() => Path.Combine(Path.GetTempPath(), $"sa-ff02-{Guid.NewGuid():N}.tiff");

    private static async Task<(ScanImageDataset written, ScanImageDataset readBack)> RoundTripAsync(ScanImageDataset dataset)
    {
        var path = TempTiffPath();
        try
        {
            var write = await new PsiaTiffWriter().WriteAsync(dataset, path, CancellationToken.None);
            Assert.True(write.IsSuccess, write.Error?.Message);

            var read = await NewReader().ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);
            Assert.True(read.IsSuccess, read.Error?.Message);
            return (dataset, Assert.IsType<ScanImageDataset>(read.Dataset));
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort temp cleanup */ }
        }
    }

    [Fact]
    public async Task Round_trips_pixels_axes_and_channel_through_the_reader()
    {
        var source = await LoadFixtureAsync();
        var derived = await DerivedAsync(source);

        var (written, readBack) = await RoundTripAsync(derived);

        Assert.Equal(written.X.Count, readBack.X.Count);
        Assert.Equal(written.Y.Count, readBack.Y.Count);
        Assert.Equal(written.X.Origin, readBack.X.Origin, 9);
        Assert.Equal(written.X.Step, readBack.X.Step, 9);
        Assert.Equal(written.Y.Step, readBack.Y.Step, 9);
        Assert.Equal(written.X.Unit.Symbol, readBack.X.Unit.Symbol);
        Assert.Equal(written.Channel.Key, readBack.Channel.Key);
        Assert.Equal(written.Channel.Unit.Symbol, readBack.Channel.Unit.Symbol);
        Assert.Equal(written.Channel.Kind, readBack.Channel.Kind);

        // Float write with gain=1/offset=0 → the reader returns identical Z values.
        var a = written.Data.Memory.Span;
        var b = readBack.Data.Memory.Span;
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i], b[i], 6);
        }
    }

    [Fact]
    public async Task Round_trips_identity_and_provenance_via_the_sidecar()
    {
        var source = await LoadFixtureAsync();
        var derived = await DerivedAsync(source);
        Assert.False(derived.Provenance.IsRoot); // precondition: the derived dataset has a real step

        var (written, readBack) = await RoundTripAsync(derived);

        Assert.Equal(written.Id, readBack.Id); // identity preserved by the side-car
        Assert.False(readBack.Provenance.IsRoot);

        var writtenStep = written.Provenance.Steps[^1];
        var readStep = readBack.Provenance.Steps[^1];
        Assert.Equal(writtenStep.OperationId, readStep.OperationId);
        Assert.Equal(writtenStep.InputDatasetId, readStep.InputDatasetId);
        Assert.Equal(writtenStep.Order, readStep.Order);
        // A recorded parameter (value + unit) survives the JSON round-trip.
        Assert.Equal(
            writtenStep.Parameters[SpatialFilterOperation.SizeParameter].Value,
            readStep.Parameters[SpatialFilterOperation.SizeParameter].Value,
            9);
    }

    [Fact]
    public async Task Canonicalizes_non_micrometre_axes_to_the_PSIA_micrometre_coordinate_system()
    {
        // The PSIA header stores X/Y in µm, so a nm-axis dataset must be converted on write — else the reader
        // (which always reads µm) would rescale the geometry by 1000×. Origin is an affine coordinate
        // conversion; step is a delta (scale ratio only).
        using var nmImage = new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Nanometre, origin: 100.0, step: 10.0, count: 4),
            new Axis("Y", StandardUnits.Nanometre, origin: 2000.0, step: 20.0, count: 3),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(new float[12], 4, 3),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        var (_, readBack) = await RoundTripAsync(nmImage);

        Assert.Equal("um", readBack.X.Unit.Symbol);
        Assert.Equal(0.1, readBack.X.Origin, 9);   // 100 nm  → 0.1 µm
        Assert.Equal(0.01, readBack.X.Step, 9);    // 10 nm   → 0.01 µm
        Assert.Equal(2.0, readBack.Y.Origin, 9);   // 2000 nm → 2.0 µm
        Assert.Equal(0.02, readBack.Y.Step, 9);    // 20 nm   → 0.02 µm
    }

    [Theory]
    [InlineData(AxisDirection.Reverse, AxisDirection.Forward)]
    [InlineData(AxisDirection.Reverse, AxisDirection.Reverse)]
    [InlineData(AxisDirection.Forward, AxisDirection.Reverse)]
    public async Task Preserves_axis_direction_so_physical_coordinates_survive(AxisDirection xDir, AxisDirection yDir)
    {
        // The PSIA header has no faithful scan-direction field, so a Reverse axis (e.g. from an A07 flip/rotate)
        // must be carried in the side-car — otherwise the reader rebuilds Forward and every pixel's physical
        // coordinate shifts. Uses µm axes so RawToReal is directly comparable to the read-back µm axis.
        using var image = new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, origin: 0.0, step: 1.0, count: 4, direction: xDir),
            new Axis("Y", StandardUnits.Micrometre, origin: 0.0, step: 1.0, count: 3, direction: yDir),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Micrometre),
            ScanBuffer<float>.TakeOwnership(new float[12], 4, 3),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        var (written, readBack) = await RoundTripAsync(image);

        Assert.Equal(xDir, readBack.X.Direction);
        Assert.Equal(yDir, readBack.Y.Direction);
        for (int x = 0; x < written.X.Count; x++)
        {
            Assert.Equal(written.X.RawToReal(x), readBack.X.RawToReal(x), 9);
        }

        for (int y = 0; y < written.Y.Count; y++)
        {
            Assert.Equal(written.Y.RawToReal(y), readBack.Y.RawToReal(y), 9);
        }
    }

    [Fact]
    public async Task A_file_without_the_sidecar_reads_as_a_root_dataset()
    {
        // The committed fixture was not written by FF02, so it has no ImageDescription side-car — the reader
        // must keep its legacy behaviour (a fresh id + Root lineage), proving real PSIA files are unaffected.
        var fixture = await LoadFixtureAsync();

        Assert.True(fixture.Provenance.IsRoot);
    }

    [Fact]
    public async Task Rejects_a_non_image_dataset()
    {
        var writer = new PsiaTiffWriter();
        using var profile = new LineProfileDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Nanometre, 0.0, 1.0, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

        Assert.False(writer.CanWrite(profile));

        var result = await writer.WriteAsync(profile, TempTiffPath(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileWriteErrorKind.Unsupported, result.Error!.Kind);
    }

    [Fact]
    public void The_composition_root_resolves_the_writer_port()
    {
        var services = new ServiceCollection().AddPsiaTiffWriter().BuildServiceProvider();

        Assert.IsType<PsiaTiffWriter>(services.GetRequiredService<IScanFileWriter>());
    }
}
