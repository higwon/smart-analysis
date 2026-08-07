using System.Collections.ObjectModel;

namespace SmartAnalysis.Domain.Metadata;

/// <summary>
/// Scan metadata: a small strongly-typed <b>core</b> plus a typed <see cref="Extended"/> string bag
/// for instrument-specific extras. Replaces the legacy ~60-field header struct + loose dictionaries
/// (doc 02). Immutable <b>value object</b> with <b>structural equality</b> — two instances are equal
/// when their core fields and their <see cref="Extended"/> key/value pairs match (order-independent).
/// <para>
/// Only MVP-needed core fields are modeled strongly; the full legacy header mapping is a documented
/// follow-up (doc 00 gaps) and lives in <see cref="Extended"/> until promoted. <see cref="Extended"/>
/// keys are compared with <see cref="StringComparer.Ordinal"/>; a per-instrument key-normalization
/// policy (e.g. "ScanRate" vs "scan rate") is a parser/import concern (documented follow-up).
/// </para>
/// </summary>
public sealed class ScanMetadata : IEquatable<ScanMetadata>
{
    private static readonly IReadOnlyDictionary<string, string> EmptyExtended =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <param name="instrumentModel">Instrument model/identifier (non-empty).</param>
    /// <param name="acquiredAt">Acquisition timestamp.</param>
    /// <param name="extended">Instrument-specific extra fields (defensively copied; keys non-empty, values non-null).</param>
    public ScanMetadata(
        string instrumentModel,
        DateTimeOffset acquiredAt,
        IReadOnlyDictionary<string, string>? extended = null)
    {
        InstrumentModel = DomainGuard.Text(instrumentModel, nameof(instrumentModel));
        AcquiredAt = acquiredAt;
        Extended = BuildExtended(extended);
    }

    public string InstrumentModel { get; }

    public DateTimeOffset AcquiredAt { get; }

    /// <summary>Instrument-specific extra fields not (yet) modeled strongly (read-only).</summary>
    public IReadOnlyDictionary<string, string> Extended { get; }

    /// <summary>Placeholder metadata for derived/synthetic datasets with no acquisition header.</summary>
    public static ScanMetadata Unknown { get; } = new("unknown", DateTimeOffset.MinValue);

    private static IReadOnlyDictionary<string, string> BuildExtended(IReadOnlyDictionary<string, string>? extended)
    {
        if (extended is null || extended.Count == 0)
        {
            return EmptyExtended;
        }

        var copy = new Dictionary<string, string>(extended.Count, StringComparer.Ordinal);
        foreach (var kv in extended)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                throw new ArgumentException("Extended keys must be non-empty.", nameof(extended));
            }

            if (kv.Value is null)
            {
                throw new ArgumentException($"Extended value for key '{kv.Key}' must not be null.", nameof(extended));
            }

            copy[kv.Key] = kv.Value;
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }

    // --- Structural equality (value object) ---

    public bool Equals(ScanMetadata? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!string.Equals(InstrumentModel, other.InstrumentModel, StringComparison.Ordinal)
            || AcquiredAt != other.AcquiredAt
            || Extended.Count != other.Extended.Count)
        {
            return false;
        }

        foreach (var kv in Extended)
        {
            if (!other.Extended.TryGetValue(kv.Key, out var value)
                || !string.Equals(value, kv.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ScanMetadata);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(InstrumentModel, StringComparer.Ordinal);
        hash.Add(AcquiredAt);

        // Order-independent contribution from the Extended bag.
        var bag = 0;
        foreach (var kv in Extended)
        {
            bag ^= HashCode.Combine(kv.Key, kv.Value);
        }

        hash.Add(bag);
        return hash.ToHashCode();
    }

    public static bool operator ==(ScanMetadata? left, ScanMetadata? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ScanMetadata? left, ScanMetadata? right) => !(left == right);
}
