namespace SmartAnalysis.Application.Analysis;

/// <summary>One labelled scalar readout of a measurement (name · value · unit), for the Inspector result card.</summary>
public sealed record StatisticsReadout(string Name, double Value, string Unit);

/// <summary>
/// The outcome of computing image statistics for the UI (U03 Measure → Result). A measurement is
/// <b>attached to</b> its source image and does not change the active dataset (doc 22/26). The readouts +
/// histogram are surfaced for display; persisting the artifact into the workspace is a documented follow-up.
/// </summary>
public sealed record StatisticsResult(
    bool Success,
    string? SourceLabel,
    IReadOnlyList<StatisticsReadout> Readouts,
    IReadOnlyList<int> Histogram,
    string? Error)
{
    public static StatisticsResult Failed(string error) => new(false, null, [], [], error);
}
