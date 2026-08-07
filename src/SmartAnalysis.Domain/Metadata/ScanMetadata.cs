using System.Collections.ObjectModel;

namespace SmartAnalysis.Domain.Metadata;

/// <summary>
/// Scan metadata: a small strongly-typed <b>core</b> plus a typed <see cref="Extended"/> string bag
/// for instrument-specific extras. Replaces the legacy ~60-field header struct + loose dictionaries
/// (doc 02). Immutable value object.
/// <para>
/// Only MVP-needed core fields are modeled strongly; the full legacy header mapping is a documented
/// follow-up (doc 00 gaps) and lives in <see cref="Extended"/> until promoted.
/// </para>
/// </summary>
public sealed record ScanMetadata
{
    private static readonly IReadOnlyDictionary<string, string> EmptyExtended =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <param name="instrumentModel">Instrument model/identifier (non-empty).</param>
    /// <param name="acquiredAt">Acquisition timestamp.</param>
    /// <param name="extended">Instrument-specific extra fields (defensively copied; may be null/empty).</param>
    public ScanMetadata(
        string instrumentModel,
        DateTimeOffset acquiredAt,
        IReadOnlyDictionary<string, string>? extended = null)
    {
        InstrumentModel = DomainGuard.Text(instrumentModel, nameof(instrumentModel));
        AcquiredAt = acquiredAt;
        Extended = extended is null
            ? EmptyExtended
            : new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(extended, StringComparer.Ordinal));
    }

    public string InstrumentModel { get; }

    public DateTimeOffset AcquiredAt { get; }

    /// <summary>Instrument-specific extra fields not (yet) modeled strongly (read-only).</summary>
    public IReadOnlyDictionary<string, string> Extended { get; }

    /// <summary>Placeholder metadata for derived/synthetic datasets with no acquisition header.</summary>
    public static ScanMetadata Unknown { get; } = new("unknown", DateTimeOffset.MinValue);
}
