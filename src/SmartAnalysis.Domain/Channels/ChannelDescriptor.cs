using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Channels;

/// <summary>
/// Typed classification of an AFM channel, replacing legacy stringly-typed detection
/// (<c>SourceName.Contains("force")</c>, doc 02). <see cref="Unknown"/> is explicit — never guessed.
/// </summary>
public enum ChannelKind
{
    Unknown,
    Topography,   // height / Z
    Deflection,   // cantilever deflection / error signal
    Amplitude,
    Phase,
    Frequency,
    Current,
    Voltage,
    Force,
    Adhesion,
    Modulus,
    Stiffness,
    Intensity,    // PiFM / optical
}

/// <summary>
/// Strongly-typed descriptor of a data channel: a stable <see cref="Key"/>, its
/// <see cref="ChannelKind"/>, its physical <see cref="Unit"/> (F01), and a display name. Immutable
/// value object; get-only members so a <c>with</c>-expression can't bypass validation.
/// </summary>
public sealed record ChannelDescriptor
{
    /// <param name="key">Stable channel key (e.g. "height", "deflection"); non-empty.</param>
    /// <param name="kind">Typed classification (a defined <see cref="ChannelKind"/>).</param>
    /// <param name="unit">Physical unit of the channel's values.</param>
    /// <param name="displayName">Human-facing name; defaults to <paramref name="key"/> when omitted.</param>
    public ChannelDescriptor(string key, ChannelKind kind, Unit unit, string? displayName = null)
    {
        Key = DomainGuard.Text(key, nameof(key));
        Kind = DomainGuard.DefinedEnum(kind, nameof(kind));
        Unit = DomainGuard.NotNull(unit, nameof(unit));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Key : displayName;
    }

    public string Key { get; }

    public ChannelKind Kind { get; }

    public Unit Unit { get; }

    public string DisplayName { get; }
}
