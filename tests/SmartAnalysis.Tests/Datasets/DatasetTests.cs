using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Datasets;

public sealed class DatasetTests
{
    private static Axis Axis(int count) => new("X", StandardUnits.Nanometre, 0.0, 1.0, count);
    private static DataSource Source(string? path = null) => new("psia-tiff", path);
    private static ScanImageDataset Image(DatasetId id, DataSource src, ScanBuffer<float> buffer)
        => new(id, src, Axis(3), Axis(1), StandardUnits.Nanometre, buffer);

    // --- Identity (ADR-012): equality is by DatasetId only ---

    [Fact]
    public void DatasetId_new_is_unique() => Assert.NotEqual(DatasetId.New(), DatasetId.New());

    [Fact]
    public void Same_id_with_different_buffer_instances_is_the_same_identity()
    {
        var id = DatasetId.New();
        var a = Image(id, Source(), ScanBuffer<float>.Allocate(3, 1)); // distinct buffers
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

    // --- Buffer↔axes validation ---

    [Fact]
    public void ScanImage_exposes_axes_unit_and_buffer()
    {
        using var image = new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), StandardUnits.Nanometre, ScanBuffer<float>.Allocate(4, 3));

        Assert.Equal(4, image.X.Count);
        Assert.Equal(3, image.Y.Count);
        Assert.Equal("nm", image.ValueUnit.Symbol);
        Assert.Equal(12, image.Data.Length);
    }

    [Fact]
    public void ScanImage_rejects_buffer_not_matching_axes()
        => Assert.Throws<ArgumentException>(() => new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), StandardUnits.Nanometre, ScanBuffer<float>.Allocate(4, 2)));

    [Fact]
    public void LineProfile_requires_1d_buffer_matching_axis()
    {
        using var ok = new LineProfileDataset(DatasetId.New(), Source(), Axis(5), StandardUnits.Nanometre, ScanBuffer<float>.Allocate(5, 1));
        Assert.Equal(5, ok.Values.Length);

        Assert.Throws<ArgumentException>(() => new LineProfileDataset(
            DatasetId.New(), Source(), Axis(5), StandardUnits.Nanometre, ScanBuffer<float>.Allocate(5, 2)));
    }

    [Fact]
    public void Spectrum_requires_1d_buffer_matching_axis()
    {
        var axis = new Axis("wn", StandardUnits.PerCentimetre, 500, 1, 8);
        using var ok = new SpectrumDataset(DatasetId.New(), Source(), axis, StandardUnits.One, ScanBuffer<float>.Allocate(8, 1));
        Assert.Equal(8, ok.Intensity.Length);

        Assert.Throws<ArgumentException>(() => new SpectrumDataset(
            DatasetId.New(), Source(), axis, StandardUnits.One, ScanBuffer<float>.Allocate(7, 1)));
    }

    // --- Buffer ownership & lifetime (ADR-011/012) ---

    [Fact]
    public void Dataset_owns_and_disposes_its_buffer()
    {
        var image = new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), StandardUnits.Nanometre, ScanBuffer<float>.Allocate(4, 3));

        image.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = image.Data.Memory);
    }

    [Fact]
    public void Failed_construction_does_not_take_ownership_of_the_buffer()
    {
        var buffer = ScanBuffer<float>.Allocate(4, 2); // 8 elements, mismatched with 4x3 axes

        Assert.Throws<ArgumentException>(() => new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), StandardUnits.Nanometre, buffer));

        // Ownership stayed with the caller: the buffer is still usable and can be disposed by us.
        Assert.Equal(8, buffer.Memory.Length);
        buffer.Dispose();
    }

    // --- ForceCurveDataset ---

    [Fact]
    public void ForceCurve_requires_equal_length_and_distinct_buffers_and_disposes_both()
    {
        var sep = ScanBuffer<float>.Allocate(64, 1);
        var force = ScanBuffer<float>.Allocate(64, 1);
        var fc = new ForceCurveDataset(DatasetId.New(), Source(), sep, force, StandardUnits.Nanometre, StandardUnits.Nanonewton);
        Assert.Equal(64, fc.Length);

        fc.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = sep.Memory);
        Assert.Throws<ObjectDisposedException>(() => _ = force.Memory);
    }

    [Fact]
    public void ForceCurve_rejects_same_buffer_for_both_roles()
    {
        var shared = ScanBuffer<float>.Allocate(64, 1);
        Assert.Throws<ArgumentException>(() => new ForceCurveDataset(
            DatasetId.New(), Source(), shared, shared, StandardUnits.Nanometre, StandardUnits.Nanonewton));
    }

    [Fact]
    public void ForceCurve_rejects_length_mismatch()
        => Assert.Throws<ArgumentException>(() => new ForceCurveDataset(
            DatasetId.New(), Source(), ScanBuffer<float>.Allocate(64, 1), ScanBuffer<float>.Allocate(32, 1),
            StandardUnits.Nanometre, StandardUnits.Nanonewton));

    // --- AnalysisArtifact ---

    [Fact]
    public void Artifact_scalars_are_immutable_and_defensively_copied()
    {
        var mutable = new Dictionary<string, PhysicalValue> { ["Sq"] = new(1.5, StandardUnits.Nanometre) };
        var artifact = new AnalysisArtifact(DatasetId.New(), DatasetId.New(), "image.roughness", mutable);

        mutable["Sa"] = new(0.9, StandardUnits.Nanometre); // must not leak in
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

        Assert.Equal(a, b); // same Id ⇒ same identity, regardless of other fields
    }
}
