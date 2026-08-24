using System.Globalization;
using System.Text;
using SmartAnalysis.Application.Export;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;

namespace SmartAnalysis.Infrastructure.Export;

/// <summary>
/// The CSV adapter for the V05 <see cref="IDataExporter"/> port. Writes invariant-culture numbers (so a comma-decimal
/// locale can't corrupt a comma-separated file) with unit-bearing headers and a short <c>#</c> provenance preamble, so
/// an exported file says what it is and how it was produced. Values use round-trippable "R" formatting — an export is
/// data for another tool, not a display string.
/// </summary>
public sealed class CsvDataExporter : IDataExporter
{
    public string Extension => "csv";

    public void ExportCurve(LineProfileDataset curve, string path)
    {
        ArgumentNullException.ThrowIfNull(curve);
        using var writer = Create(path);
        WritePreamble(writer, curve.Provenance, $"curve · {curve.X.Count} samples");

        writer.WriteLine($"{Header(curve.X.Name, curve.X.Unit.Symbol)},{Header(curve.Channel.DisplayName, curve.Channel.Unit.Symbol)}");
        var values = curve.Values.Memory.Span;
        for (int i = 0; i < curve.X.Count; i++)
        {
            writer.WriteLine($"{Num(curve.X.RawToReal(i))},{Num(values[i])}");
        }
    }

    public void ExportImage(ScanImageDataset image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var writer = Create(path);
        int w = image.Data.Width, h = image.Data.Height;
        WritePreamble(writer, image.Provenance, $"image · {w}×{h} · {Header(image.Channel.DisplayName, image.Channel.Unit.Symbol)}");

        // The grid carries no per-cell coordinates; record the axes so the values stay physically meaningful.
        writer.WriteLine($"# {image.X.Name} ({image.X.Unit.Symbol}): {Num(image.X.RawToReal(0))} .. {Num(image.X.RawToReal(Math.Max(0, w - 1)))}");
        writer.WriteLine($"# {image.Y.Name} ({image.Y.Unit.Symbol}): {Num(image.Y.RawToReal(0))} .. {Num(image.Y.RawToReal(Math.Max(0, h - 1)))}");

        var z = image.Data.Memory.Span;
        var row = new StringBuilder(w * 12);
        for (int y = 0; y < h; y++)
        {
            row.Clear();
            for (int x = 0; x < w; x++)
            {
                if (x > 0)
                {
                    row.Append(',');
                }

                row.Append(Num(z[(y * w) + x]));
            }

            writer.WriteLine(row.ToString());
        }
    }

    public void ExportMeasurement(AnalysisArtifact measurement, string path)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        using var writer = Create(path);
        WritePreamble(writer, measurement.Provenance, $"measurement · {measurement.OperationId}");

        writer.WriteLine("Name,Value,Unit");
        foreach (var (key, pv) in measurement.Scalars)
        {
            writer.WriteLine($"{Field(key)},{Num(pv.Value)},{Field(pv.Unit.Symbol)}");
        }

        // The per-row table (e.g. a peak list) follows the scalars, after a blank separator line, so one file carries
        // the measurement's whole result.
        if (measurement.Table is { } table)
        {
            writer.WriteLine();
            writer.WriteLine(string.Join(",", table.Columns.Select(c => Header(c.Name, c.Unit.Symbol))));
            foreach (var tableRow in table.Rows)
            {
                writer.WriteLine(string.Join(",", tableRow.Select(cell => Num(cell.Value))));
            }
        }
    }

    private static StreamWriter Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new StreamWriter(File.Create(path), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // A short comment preamble: what this file holds and the operations that produced it (doc 16 — an exported result
    // should not lose its lineage). Comment lines start with '#', which every CSV reader can skip.
    private static void WritePreamble(TextWriter writer, ProvenanceRecord provenance, string what)
    {
        writer.WriteLine($"# SmartAnalysis export · {what}");
        foreach (var step in provenance.Steps)
        {
            var parameters = step.Parameters.Count == 0
                ? string.Empty
                : " · " + string.Join(" ", step.Parameters.Select(p => $"{p.Key}={Num(p.Value.Value)}"));
            writer.WriteLine($"# step {step.Order + 1}: {step.OperationId}{parameters}");
        }
    }

    private static string Header(string name, string unit)
        => Field(unit is "" or "1" ? name : $"{name} ({unit})");

    // Quote a field that would otherwise break the row (comma, quote, newline); double the inner quotes (RFC 4180).
    private static string Field(string value)
        => value.AsSpan().IndexOfAny(",\"\n\r") >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
