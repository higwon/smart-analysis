using System.Globalization;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.Export;
using Xunit;

namespace SmartAnalysis.Tests.Export;

/// <summary>
/// V05 CSV export: the numbers behind an analysis leave the app in a form another tool can read — invariant-culture
/// values, unit-bearing headers, and the provenance preamble that keeps a result traceable (doc 16).
/// </summary>
public sealed class CsvDataExporterTests
{
    private static string TempCsv() => Path.Combine(Path.GetTempPath(), $"sa-v05-{Guid.NewGuid():N}.csv");

    private static LineProfileDataset Curve(ProvenanceRecord? provenance = null)
        => new(
            DatasetId.New(), new DataSource("test", null),
            new Axis("Distance", StandardUnits.Micrometre, 0.0, 0.5, 3),   // 0, 0.5, 1.0
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre, "Topography"),
            ScanBuffer<float>.TakeOwnership([1.5f, -2.25f, 3f], 3, 1),
            ScanMetadata.Unknown, provenance ?? ProvenanceRecord.Root);

    private static ScanImageDataset Image()
        => new(
            DatasetId.New(), new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre, "Topography"),
            ScanBuffer<float>.TakeOwnership([1f, 2f, 3f, 4f], 2, 2),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

    private static string[] Lines(string path) => File.ReadAllLines(path);

    private static string[] DataLines(string path) => Lines(path).Where(l => !l.StartsWith('#')).ToArray();

    [Fact]
    public void A_curve_exports_one_row_per_sample_with_unit_headers()
    {
        var path = TempCsv();
        using var curve = Curve();

        new CsvDataExporter().ExportCurve(curve, path);

        var rows = DataLines(path);
        Assert.Equal("Distance (um),Topography (nm)", rows[0]);
        Assert.Equal("0,1.5", rows[1]);
        Assert.Equal("0.5,-2.25", rows[2]);   // the X axis position, not the sample index
        Assert.Equal("1,3", rows[3]);
        Assert.Equal(4, rows.Length);
    }

    [Fact]
    public void An_image_exports_a_row_per_scan_row_and_records_its_axes()
    {
        var path = TempCsv();
        using var image = Image();

        new CsvDataExporter().ExportImage(image, path);

        var rows = DataLines(path);
        Assert.Equal(["1,2", "3,4"], rows);                                  // row-major Z grid
        Assert.Contains(Lines(path), l => l.StartsWith("# X (um):"));        // the grid alone has no coordinates …
        Assert.Contains(Lines(path), l => l.StartsWith("# Y (um):"));        // … so the axis extents are recorded
    }

    [Fact]
    public void A_measurement_exports_its_scalars_then_its_table()
    {
        var path = TempCsv();
        var nm = StandardUnits.Nanometre;
        var um = StandardUnits.Micrometre;
        var artifact = new AnalysisArtifact(
            DatasetId.New(), DatasetId.New(), "curve.peaks",
            new Dictionary<string, PhysicalValue> { ["PeakCount"] = new(2, StandardUnits.One), ["DominantValue"] = new(1.25, nm) },
            ProvenanceRecord.Root,
            table: new MeasurementTable(
                [new MeasurementColumn("Position", um), new MeasurementColumn("Value", nm)],
                [
                    new[] { new PhysicalValue(0.5, um), new PhysicalValue(1.25, nm) },
                    new[] { new PhysicalValue(1.5, um), new PhysicalValue(0.75, nm) },
                ]));

        new CsvDataExporter().ExportMeasurement(artifact, path);

        var rows = DataLines(path).Where(l => l.Length > 0).ToArray();
        Assert.Equal("Name,Value,Unit", rows[0]);
        Assert.Contains("PeakCount,2,1", rows);
        Assert.Contains("DominantValue,1.25,nm", rows);
        Assert.Contains("Position (um),Value (nm)", rows);   // the per-row table follows the scalars …
        Assert.Contains("0.5,1.25", rows);                    // … so a peak list leaves the app in full
        Assert.Contains("1.5,0.75", rows);
    }

    [Fact]
    public void An_export_records_the_operations_that_produced_it()
    {
        var path = TempCsv();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"), inputDatasetId: DatasetId.New(), inputVersion: 0,
            operationId: "profile.flatten", operationVersion: 1, order: 0,
            environment: ExecutionEnvironment.Unknown,
            parameters: new Dictionary<string, PhysicalValue> { ["order"] = new(1, StandardUnits.One) });
        using var curve = Curve(ProvenanceRecord.DerivedFrom(DatasetId.New(), [step]));

        new CsvDataExporter().ExportCurve(curve, path);

        Assert.Contains(Lines(path), l => l.Contains("profile.flatten") && l.Contains("order=1"));
    }

    [Fact]
    public void A_recorded_parameter_keeps_its_unit_so_the_lineage_is_reproducible()
    {
        var path = TempCsv();
        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"), inputDatasetId: DatasetId.New(), inputVersion: 0,
            operationId: "profile.filter", operationVersion: 1, order: 0,
            environment: ExecutionEnvironment.Unknown,
            parameters: new Dictionary<string, PhysicalValue>
            {
                ["cutoff"] = new(0.5, StandardUnits.Micrometre),      // a physical parameter ...
                ["order"] = new(1, StandardUnits.One),                 // ... and a dimensionless one
            });
        using var curve = Curve(ProvenanceRecord.DerivedFrom(DatasetId.New(), [step]));

        new CsvDataExporter().ExportCurve(curve, path);

        var preamble = Lines(path).Single(l => l.Contains("profile.filter"));
        Assert.Contains("cutoff=0.5 um", preamble); // "cutoff=0.5" alone would not be reproducible
        Assert.Contains("order=1", preamble);       // dimensionless stays bare (no "1" suffix)
        Assert.DoesNotContain("order=1 1", preamble);
    }

    [Fact]
    public void Values_are_invariant_culture_so_a_comma_decimal_locale_cannot_corrupt_the_file()
    {
        var path = TempCsv();
        var original = CultureInfo.CurrentCulture;
        try
        {
            // A comma-decimal locale would otherwise write "0,5" — an extra column in a comma-separated file.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            using var curve = Curve();
            new CsvDataExporter().ExportCurve(curve, path);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Equal("0.5,-2.25", DataLines(path)[2]);
    }
}
