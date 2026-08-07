namespace SmartAnalysis.Domain.Units;

/// <summary>
/// A physical dimension (e.g. Length, Force, Current). Two units are convertible only when they
/// share the same <see cref="Dimension"/>. Value equality is by <see cref="Name"/>.
/// <para>Invariant: <see cref="Name"/> is non-empty (validated at construction).</para>
/// </summary>
public sealed record Dimension
{
    public Dimension(string name) => Name = DomainGuard.Text(name, nameof(name));

    /// <summary>Dimension name (non-empty).</summary>
    public string Name { get; }
}
