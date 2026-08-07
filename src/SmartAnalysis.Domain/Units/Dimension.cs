namespace SmartAnalysis.Domain.Units;

/// <summary>
/// A physical dimension (e.g. Length, Force, Current). Two units are convertible only when they
/// share the same <see cref="Dimension"/>. Value equality is by <see cref="Name"/>.
/// </summary>
public sealed record Dimension(string Name);
