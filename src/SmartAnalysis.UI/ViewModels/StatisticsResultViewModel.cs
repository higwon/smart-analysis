using System.Collections.ObjectModel;
using SmartAnalysis.Application.Analysis;

namespace SmartAnalysis.UI.ViewModels;

/// <summary>The Inspector role shown on the right panel (U03, doc 26 §13).</summary>
public enum InspectorRole
{
    /// <summary>Default: the active dataset's properties.</summary>
    DatasetProperties,

    /// <summary>An operation is being configured (e.g. Flatten).</summary>
    Operation,

    /// <summary>A measurement result card (e.g. Statistics), attached to the active dataset.</summary>
    Result,

    /// <summary>A read-only provenance-step inspector.</summary>
    Step,
}

/// <summary>A single scalar readout row for the Statistics result card.</summary>
public sealed class ReadoutViewModel
{
    public ReadoutViewModel(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public string Value { get; }
}

/// <summary>The measurement result card (U03 Measure → Result): readouts + a histogram + an optional table.</summary>
public sealed class StatisticsResultViewModel
{
    public StatisticsResultViewModel(StatisticsResult result)
    {
        SourceLabel = result.SourceLabel ?? "dataset";
        foreach (var r in result.Readouts)
        {
            Readouts.Add(new ReadoutViewModel(r.Name, $"{r.Value:G4} {r.Unit}".Trim()));
        }

        // Normalize the histogram to 0..1 bar heights for the view.
        var max = result.Histogram.Count > 0 ? result.Histogram.Max() : 0;
        foreach (var c in result.Histogram)
        {
            HistogramBars.Add(max > 0 ? (double)c / max : 0.0);
        }

        // The optional tabular result (e.g. the full peak list): column headers + rows of cell strings.
        if (result.Table is { } table)
        {
            foreach (var col in table.Columns)
            {
                TableColumns.Add(col);
            }

            foreach (var row in table.Rows)
            {
                TableRows.Add(new ObservableCollection<string>(row));
            }
        }
    }

    public string SourceLabel { get; }
    public ObservableCollection<ReadoutViewModel> Readouts { get; } = new();
    public ObservableCollection<double> HistogramBars { get; } = new();
    public bool HasHistogram => HistogramBars.Count > 0;
    public ObservableCollection<string> TableColumns { get; } = new();
    public ObservableCollection<ObservableCollection<string>> TableRows { get; } = new();
    public bool HasTable => TableColumns.Count > 0;
}
