namespace SmartAnalysis.Analysis;

/// <summary>
/// Small argument-validation helpers used by Analysis contract types to reject invalid state at
/// construction — the Analysis-assembly counterpart to Domain's internal guard (which is not visible
/// across the assembly boundary). Kept internal: enforcement of invariants, not public API.
/// </summary>
internal static class AnalysisGuard
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
