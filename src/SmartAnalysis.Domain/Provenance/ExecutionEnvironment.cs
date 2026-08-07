namespace SmartAnalysis.Domain.Provenance;

/// <summary>
/// A snapshot of the environment a provenance step ran in (for reproducibility/audit). Immutable
/// value object. Populated by the operation runner (F04); F05 defines the type.
/// </summary>
public sealed record ExecutionEnvironment(
    string AppVersion,
    string OperatingSystem,
    string MachineName,
    DateTimeOffset Timestamp)
{
    /// <summary>Placeholder for steps synthesized without a captured environment (e.g. tests).</summary>
    public static ExecutionEnvironment Unknown { get; } = new("unknown", "unknown", "unknown", DateTimeOffset.MinValue);
}
