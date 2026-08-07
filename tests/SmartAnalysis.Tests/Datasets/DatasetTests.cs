using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Datasets;

public sealed class DatasetTests
{
    private static Axis Axis(int count) => new("X", StandardUnits.Nanometre, 0.0, 1.0, count);
    private static DataSource Source(string? path = null) => new("psia-tiff", path);
    private static ScanMetadata Meta => ScanMetadata.Unknown;
    private static ChannelDescriptor Height => new("height", ChannelKind.Topography, StandardUnits.Nanometre);
    private static ChannelDescriptor Intensity => new("intensity", ChannelKind.Intensity, StandardUnits.One);
    private static ChannelDescriptor SeparationCh => new("separation", ChannelKind.Topography, StandardUnits.Nanometre);
    private static ChannelDescriptor ForceChannel => new("force", ChannelKind.Force, StandardUnits.Nanonewton);

    private static ScanImageDataset Image(DatasetId id, DataSource src, ScanBuffer<float> buffer)
        => new(id, src, Axis(3), Axis(1), Height, buffer, Meta);

    // --- Identity (ADR-012): equality is by DatasetId only ---

    [Fact]
    public void DatasetId_new_is_unique() => Assert.NotEqual(DatasetId.New(), DatasetId.New());

    [Fact]
    public void Same_id_with_different_buffer_instances_is_the_same_identity()
    {
        var id = DatasetId.New();
        var a = Image(id, Source(), ScanBuffer<float>.Allocate(3, 1));
        var b = Image(id, Source(), ScanBuffer<float>.Allocate(3, 1));

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_id_same_source_and_content_is_a_different_identity()
    {
        var a = Image(DatasetId.New(), Source("C:/scan.tiff"), ScanBuffer<float>.Allocate(3, 1));
        var b = Image(DatasetId.New(), Source("C:/scan.tiff"), ScanBuffer<float>.Allocate(3, 1));

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void Empty_dataset_id_is_rejected()
        => Assert.Throws<ArgumentException>(() => Image(default, Source(), ScanBuffer<float>.Allocate(3, 1)));

    // --- Channel + metadata (D01) ---

    [Fact]
    public void ScanImage_exposes_channel_unit_and_metadata()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), Height, ScanBuffer<float>.Allocate(4, 3), ScanMetadata.Unknown);

        Assert.Equal(4, image.X.Count);
        Assert.Equal(3, image.Y.Count);
        Assert.Equal(ChannelKind.Topography, image.Channel.Kind);
        Assert.Equal("nm", image.Channel.Unit.Symbol);
        Assert.Equal(12, image.Data.Length);
        Assert.Same(ScanMetadata.Unknown, image.Metadata);
    }

    [Fact]
    public void Metadata_is_required_and_carried()
    {
        var meta = new ScanMetadata("NX10", DateTimeOffset.UnixEpoch);
        using var image = new ScanImageDataset(
            DatasetId.New(), Source(), Axis(2), Axis(2), Height, ScanBuffer<float>.Allocate(2, 2), meta);

        Assert.Equal("NX10", image.Metadata.InstrumentModel);
    }

    [Fact]
    public void Null_metadata_is_rejected()
        => Assert.Throws<ArgumentNullException>(() => new ScanImageDataset(
            DatasetId.New(), Source(), Axis(2), Axis(2), Height, ScanBuffer<float>.Allocate(2, 2), null!));

    // --- Buffer↔axes validation ---

    [Fact]
    public void ScanImage_rejects_buffer_not_matching_axes()
        => Assert.Throws<ArgumentException>(() => new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), Height, ScanBuffer<float>.Allocate(4, 2), Meta));

    [Fact]
    public void LineProfile_requires_1d_buffer_matching_axis()
    {
        using var ok = new LineProfileDataset(DatasetId.New(), Source(), Axis(5), Height, ScanBuffer<float>.Allocate(5, 1), Meta);
        Assert.Equal(5, ok.Values.Length);

        Assert.Throws<ArgumentException>(() => new LineProfileDataset(
            DatasetId.New(), Source(), Axis(5), Height, ScanBuffer<float>.Allocate(5, 2), Meta));
    }

    [Fact]
    public void Spectrum_requires_1d_buffer_matching_axis()
    {
        var axis = new Axis("wn", StandardUnits.PerCentimetre, 500, 1, 8);
        using var ok = new SpectrumDataset(DatasetId.New(), Source(), axis, Intensity, ScanBuffer<float>.Allocate(8, 1), Meta);
        Assert.Equal(8, ok.Intensity.Length);

        Assert.Throws<ArgumentException>(() => new SpectrumDataset(
            DatasetId.New(), Source(), axis, Intensity, ScanBuffer<float>.Allocate(7, 1), Meta));
    }

    // --- Buffer ownership & lifetime (ADR-011/012) ---

    [Fact]
    public void Dataset_owns_and_disposes_its_buffer()
    {
        var image = new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), Height, ScanBuffer<float>.Allocate(4, 3), Meta);

        image.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = image.Data.Memory);
    }

    [Fact]
    public void Failed_construction_does_not_take_ownership_of_the_buffer()
    {
        var buffer = ScanBuffer<float>.Allocate(4, 2); // mismatched with 4x3 axes

        Assert.Throws<ArgumentException>(() => new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), Height, buffer, Meta));

        Assert.Equal(8, buffer.Memory.Length); // still owned by the caller
        buffer.Dispose();
    }

    // --- ForceCurveDataset ---

    [Fact]
    public void ForceCurve_requires_equal_length_and_distinct_buffers_and_disposes_both()
    {
        var sep = ScanBuffer<float>.Allocate(64, 1);
        var force = ScanBuffer<float>.Allocate(64, 1);
        var fc = new ForceCurveDataset(DatasetId.New(), Source(), sep, force, SeparationCh, ForceChannel, Meta);
        Assert.Equal(64, fc.Length);
        Assert.Equal(ChannelKind.Force, fc.ForceChannel.Kind);

        fc.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = sep.Memory);
        Assert.Throws<ObjectDisposedException>(() => _ = force.Memory);
    }

    [Fact]
    public void ForceCurve_rejects_same_buffer_for_both_roles()
    {
        var shared = ScanBuffer<float>.Allocate(64, 1);
        Assert.Throws<ArgumentException>(() => new ForceCurveDataset(
            DatasetId.New(), Source(), shared, shared, SeparationCh, ForceChannel, Meta));
    }

    [Fact]
    public void ForceCurve_rejects_length_mismatch()
        => Assert.Throws<ArgumentException>(() => new ForceCurveDataset(
            DatasetId.New(), Source(), ScanBuffer<float>.Allocate(64, 1), ScanBuffer<float>.Allocate(32, 1),
            SeparationCh, ForceChannel, Meta));

    // --- AnalysisArtifact ---

    [Fact]
    public void Artifact_scalars_are_immutable_and_defensively_copied()
    {
        var mutable = new Dictionary<string, PhysicalValue> { ["Sq"] = new(1.5, StandardUnits.Nanometre) };
        var artifact = new AnalysisArtifact(DatasetId.New(), DatasetId.New(), "image.roughness", mutable);

        mutable["Sa"] = new(0.9, StandardUnits.Nanometre);
        Assert.Single(artifact.Scalars);
        Assert.Equal(1.5, artifact.Scalars["Sq"].Value);
        Assert.Throws<InvalidCastException>(() => _ = (Dictionary<string, PhysicalValue>)artifact.Scalars);
    }

    [Fact]
    public void Artifact_rejects_blank_operation_id()
        => Assert.Throws<ArgumentException>(() => new AnalysisArtifact(
            DatasetId.New(), DatasetId.New(), "  ", new Dictionary<string, PhysicalValue>()));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Artifact_rejects_empty_ids(bool emptyId, bool emptySource)
    {
        var id = emptyId ? default : DatasetId.New();
        var source = emptySource ? default : DatasetId.New();
        Assert.Throws<ArgumentException>(() => new AnalysisArtifact(id, source, "op", new Dictionary<string, PhysicalValue>()));
    }

    [Fact]
    public void Artifact_equality_is_by_id()
    {
        var id = DatasetId.New();
        var a = new AnalysisArtifact(id, DatasetId.New(), "op-a", new Dictionary<string, PhysicalValue>());
        var b = new AnalysisArtifact(id, DatasetId.New(), "op-b", new Dictionary<string, PhysicalValue>());

        Assert.Equal(a, b);
    }
}
