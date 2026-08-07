namespace SmartAnalysis.Domain;

/// <summary>
/// Small argument-validation helpers used by Domain foundation types to reject invalid state at
/// construction. Kept internal — these are enforcement of domain invariants, not public API.
/// </summary>
internal static class DomainGuard
{
    public static string Text(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must be a non-empty, non-whitespace string.", paramName);
        }

        return value;
    }

    public static T NotNull<T>(T? value, string paramName)
        where T : class
        => value ?? throw new ArgumentNullException(paramName);

    public static double Finite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException($"Value must be finite (was {value}).", paramName);
        }

        return value;
    }

    public static double FinitePositive(double value, string paramName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentException($"Value must be finite and greater than zero (was {value}).", paramName);
        }

        return value;
    }

    public static double FiniteNonZero(double value, string paramName)
    {
        if (!double.IsFinite(value) || value == 0.0)
        {
            throw new ArgumentException($"Value must be finite and non-zero (was {value}).", paramName);
        }

        return value;
    }

    public static int NonNegative(int value, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, paramName);
        return value;
    }

    public static TEnum DefinedEnum<TEnum>(TEnum value, string paramName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Undefined {typeof(TEnum).Name} value.");
        }

        return value;
    }
}
