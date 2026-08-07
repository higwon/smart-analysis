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

    // --- Identity ---

    [Fact]
    public void DatasetId_new_is_unique()
        => Assert.NotEqual(DatasetId.New(), DatasetId.New());

    [Fact]
    public void Identity_is_the_id_not_the_file_path()
    {
        var x = Axis(3);
        var y = Axis(1);
        var buffer = ScanBuffer<float>.Allocate(3, 1);

        // Same source file path, different ids → different datasets.
        var a = new ScanImageDataset(DatasetId.New(), Source("C:/scan.tiff"), x, y, StandardUnits.Nanometre, buffer);
        var b = new ScanImageDataset(DatasetId.New(), Source("C:/scan.tiff"), x, y, StandardUnits.Nanometre, buffer);

        Assert.NotEqual(a, b);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Same_id_and_members_are_equal()
    {
        var id = DatasetId.New();
        var x = Axis(3);
        var y = Axis(1);
        var buffer = ScanBuffer<float>.Allocate(3, 1);
        var src = Source();

        var a = new ScanImageDataset(id, src, x, y, StandardUnits.Nanometre, buffer);
        var b = new ScanImageDataset(id, src, x, y, StandardUnits.Nanometre, buffer);

        Assert.Equal(a, b);
    }

    // --- ScanImageDataset ---

    [Fact]
    public void ScanImage_exposes_axes_unit_and_buffer()
    {
        var image = new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), StandardUnits.Nanometre, ScanBuffer<float>.Allocate(4, 3));

        Assert.Equal(4, image.X.Count);
        Assert.Equal(3, image.Y.Count);
        Assert.Equal("nm", image.ValueUnit.Symbol);
        Assert.Equal(12, image.Data.Length);
    }

    [Fact]
    public void ScanImage_rejects_buffer_not_matching_axes()
    {
        Assert.Throws<ArgumentException>(() => new ScanImageDataset(
            DatasetId.New(), Source(), Axis(4), Axis(3), StandardUnits.Nanometre, ScanBuffer<float>.Allocate(4, 2)));
    }

    [Fact]
    public void ScanImage_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ScanImageDataset(
            DatasetId.New(), Source(), Axis(2), Axis(2), StandardUnits.Nanometre, null!));
    }

    // --- LineProfileDataset / SpectrumDataset ---

    [Fact]
    public void LineProfile_requires_1d_buffer_matching_axis()
    {
        var ok = new LineProfileDataset(DatasetId.New(), Source(), Axis(5), StandardUnits.Nanometre, ScanBuffer<float>.Allocate(5, 1));
        Assert.Equal(5, ok.Values.Length);

        Assert.Throws<ArgumentException>(() => new LineProfileDataset(
            DatasetId.New(), Source(), Axis(5), StandardUnits.Nanometre, ScanBuffer<float>.Allocate(5, 2)));
    }

    [Fact]
    public void Spectrum_requires_1d_buffer_matching_axis()
    {
        var ok = new SpectrumDataset(
            DatasetId.New(), Source(), new Axis("wn", StandardUnits.PerCentimetre, 500, 1, 8), StandardUnits.One, ScanBuffer<float>.Allocate(8, 1));
        Assert.Equal(8, ok.Intensity.Length);

        Assert.Throws<ArgumentException>(() => new SpectrumDataset(
            DatasetId.New(), Source(), new Axis("wn", StandardUnits.PerCentimetre, 500, 1, 8), StandardUnits.One, ScanBuffer<float>.Allocate(7, 1)));
    }

    // --- ForceCurveDataset ---

    [Fact]
    public void ForceCurve_requires_equal_length_1d_buffers()
    {
        var ok = new ForceCurveDataset(
            DatasetId.New(), Source(), ScanBuffer<float>.Allocate(64, 1), ScanBuffer<float>.Allocate(64, 1),
            StandardUnits.Nanometre, StandardUnits.Nanonewton);
        Assert.Equal(64, ok.Length);

        Assert.Throws<ArgumentException>(() => new ForceCurveDataset(
            DatasetId.New(), Source(), ScanBuffer<float>.Allocate(64, 1), ScanBuffer<float>.Allocate(32, 1),
            StandardUnits.Nanometre, StandardUnits.Nanonewton));
    }

    // --- AnalysisArtifact ---

    [Fact]
    public void Artifact_scalars_are_immutable_and_defensively_copied()
    {
        var mutable = new Dictionary<string, PhysicalValue>
        {
            ["Sq"] = new(1.5, StandardUnits.Nanometre),
        };

        var artifact = new AnalysisArtifact(DatasetId.New(), DatasetId.New(), "image.roughness", mutable);

        // Mutating the source dictionary must not affect the artifact.
        mutable["Sa"] = new(0.9, StandardUnits.Nanometre);
        Assert.Single(artifact.Scalars);
        Assert.Equal(1.5, artifact.Scalars["Sq"].Value);

        Assert.Throws<InvalidCastException>(() => _ = (Dictionary<string, PhysicalValue>)artifact.Scalars);
    }

    [Fact]
    public void Artifact_rejects_blank_operation_id()
        => Assert.Throws<ArgumentException>(() => new AnalysisArtifact(
            DatasetId.New(), DatasetId.New(), "  ", new Dictionary<string, PhysicalValue>()));
}
