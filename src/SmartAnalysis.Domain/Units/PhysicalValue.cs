namespace SmartAnalysis.Domain.Units;

/// <summary>
/// The typed outcome of a unit conversion. Cross-dimension conversion fails with a message rather
/// than throwing or silently returning zero (a legacy defect, doc 07 M5).
/// </summary>
public readonly record struct UnitConversion
{
    private UnitConversion(bool success, PhysicalValue value, string? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>Whether the conversion succeeded.</summary>
    public bool Success { get; }

    /// <summary>The converted value (valid only when <see cref="Success"/> is true).</summary>
    public PhysicalValue Value { get; }

    /// <summary>The failure reason (non-null only when <see cref="Success"/> is false).</summary>
    public string? Error { get; }

    public static UnitConversion Ok(PhysicalValue value) => new(true, value, null);

    public static UnitConversion Fail(string error) => new(false, default, error);
}

/// <summary>
/// A scalar quantity paired with its <see cref="Units.Unit"/>. Immutable value type.
/// Conversion goes through the affine unit maps only (single source of truth — no duplicated
/// gain/offset math, fixing the legacy duplication in doc 02).
/// </summary>
public readonly record struct PhysicalValue(double Value, Unit Unit)
{
    /// <summary>The value expressed in the dimension's base unit.</summary>
    public double ToBase() => Value * Unit.ScaleToBase + Unit.OffsetToBase;

    /// <summary>
    /// Converts to <paramref name="target"/>. Returns a typed failure if the target measures a
    /// different dimension.
    /// </summary>
    public UnitConversion TryConvertTo(Unit target)
    {
        if (!Unit.IsConvertibleTo(target))
        {
            return UnitConversion.Fail(
                $"Cannot convert '{Unit.Symbol}' ({Unit.Dimension.Name}) to " +
                $"'{target.Symbol}' ({target.Dimension.Name}): different dimensions.");
        }

        double converted = (ToBase() - target.OffsetToBase) / target.ScaleToBase;
        return UnitConversion.Ok(new PhysicalValue(converted, target));
    }
}
