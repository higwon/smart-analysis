using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Export;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Export;

/// <summary>
/// V05 export use case: what the current selection can be exported as, and routing the write to the port — without
/// touching the workspace or the active context (exporting is a read-only side trip).
/// </summary>
public sealed class ExportUseCaseTests
{
    private static ScanImageDataset Image()
        => new(
            DatasetId.New(), new DataSource("psia-tiff", @"C:\scans\cheese.tiff"),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(2, 2), ScanMetadata.Unknown, ProvenanceRecord.Root);

    private static LineProfileDataset Curve()
        => new(
            DatasetId.New(), new DataSource("test", null),
            new Axis("Distance", StandardUnits.Micrometre, 0.0, 1.0, 3),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(3, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

    private static AnalysisArtifact Measurement(DatasetId sourceId)
        => new(
            DatasetId.New(), sourceId, "image.roi-statistics",
            new Dictionary<string, PhysicalValue> { ["rms"] = new(1.0, StandardUnits.Nanometre) },
            ProvenanceRecord.Root);

    private sealed class RecordingExporter : IDataExporter
    {
        public string Extension => "csv";

        public List<string> Calls { get; } = new();

        public Exception? Throw { get; set; }

        public void ExportCurve(LineProfileDataset curve, string path) => Record("curve", path);

        public void ExportImage(ScanImageDataset image, string path) => Record("image", path);

        public void ExportMeasurement(AnalysisArtifact measurement, string path) => Record("measurement", path);

        private void Record(string what, string path)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            Calls.Add($"{what}:{path}");
        }
    }

    private static (Workspace ws, MeasurementStore store, RecordingExporter exporter, IExportUseCase useCase) Setup()
    {
        var ws = new Workspace();
        var store = new MeasurementStore();
        var exporter = new RecordingExporter();
        return (ws, store, exporter, new ExportUseCase(ws, store, exporter));
    }

    [Fact]
    public void An_active_image_is_described_and_exported_as_image_data()
    {
        var (ws, _, exporter, useCase) = Setup();
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        var target = useCase.DescribeActive();
        Assert.Equal(ExportTargetKind.Image, target!.Kind);
        Assert.Equal("cheese", target.SuggestedName); // the source file's name, so the export is recognisable

        Assert.True(useCase.ExportActive("out.csv").Success);
        Assert.Equal(["image:out.csv"], exporter.Calls);
        Assert.Equal(image.Id, ws.Active.ActiveId); // exporting never changes the active context
    }

    [Fact]
    public void An_active_curve_is_described_and_exported_as_curve_data()
    {
        var (ws, _, exporter, useCase) = Setup();
        var curve = Curve();
        ws.Add(curve);
        ws.SetActive(curve.Id);

        Assert.Equal(ExportTargetKind.Curve, useCase.DescribeActive()!.Kind);
        Assert.True(useCase.ExportActive("c.csv").Success);
        Assert.Equal(["curve:c.csv"], exporter.Calls);
    }

    [Fact]
    public void An_attached_measurement_is_described_and_exported()
    {
        var (ws, store, exporter, useCase) = Setup();
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);
        var artifact = Measurement(image.Id);
        store.Attach(artifact);

        Assert.Equal(ExportTargetKind.Measurement, useCase.DescribeMeasurement(artifact.Id)!.Kind);
        Assert.True(useCase.ExportMeasurement(artifact.Id, "m.csv").Success);
        Assert.Equal(["measurement:m.csv"], exporter.Calls);
    }

    [Fact]
    public void Nothing_active_describes_and_exports_nothing()
    {
        var (_, _, exporter, useCase) = Setup();

        Assert.Null(useCase.DescribeActive());
        var outcome = useCase.ExportActive("x.csv");
        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Empty(exporter.Calls);
    }

    [Fact]
    public void An_unknown_measurement_describes_and_exports_nothing()
    {
        var (_, _, _, useCase) = Setup();

        Assert.Null(useCase.DescribeMeasurement(DatasetId.New()));
        Assert.False(useCase.ExportMeasurement(DatasetId.New(), "x.csv").Success);
    }

    [Fact]
    public void An_io_failure_comes_back_typed_instead_of_throwing_at_the_ui()
    {
        var (ws, _, exporter, useCase) = Setup();
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);
        exporter.Throw = new IOException("the file is locked");

        var outcome = useCase.ExportActive("locked.csv");

        Assert.False(outcome.Success);
        Assert.Contains("locked", outcome.Error);
    }
}
