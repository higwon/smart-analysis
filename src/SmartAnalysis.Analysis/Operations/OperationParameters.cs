using System.Diagnostics.CodeAnalysis;
using SmartAnalysis.Analysis;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations;

/// <summary>
/// Declares one operation parameter: name, CLR type, optional default, allowed numeric range, unit,
/// and help text. Part of an operation's self-describing schema (doc 13). Immutable value object whose
/// invariants are enforced at construction — an inconsistent schema (default of the wrong type, an
/// inverted or non-finite range, a range/unit on a non-numeric type) is unrepresentable.
/// <para>
/// <b>Value convention (doc 13):</b> the runtime value in an <see cref="IParameterSet"/> is the raw
/// CLR value of <see cref="Type"/>; <see cref="Unit"/> is metadata naming the unit that value is
/// expressed in (so it applies only to numeric parameters). An operation pairs the value with
/// <see cref="Unit"/> to form a <see cref="PhysicalValue"/> when it records provenance.
/// </para>
/// </summary>
public sealed record ParameterDescriptor
{
    public ParameterDescriptor(
        string name,
        Type type,
        object? defaultValue = null,
        double? min = null,
        double? max = null,
        Unit? unit = null,
        string? help = null)
    {
        Name = AnalysisGuard.Text(name, nameof(name));
        Type = AnalysisGuard.NotNull(type, nameof(type));

        var numeric = IsNumericType(type);

        if (min is { } mn && !double.IsFinite(mn))
        {
            throw new ArgumentException("Min must be finite.", nameof(min));
        }

        if (max is { } mx && !double.IsFinite(mx))
        {
            throw new ArgumentException("Max must be finite.", nameof(max));
        }

        if (min is { } lo && max is { } hi && lo > hi)
        {
            throw new ArgumentException($"Min ({lo}) must not exceed Max ({hi}).", nameof(min));
        }

        if ((min is not null || max is not null) && !numeric)
        {
            throw new ArgumentException($"A numeric range is only valid for a numeric parameter type (was {type.Name}).", nameof(type));
        }

        if (unit is not null && !numeric)
        {
            throw new ArgumentException($"A Unit is only valid for a numeric parameter type (was {type.Name}).", nameof(unit));
        }

        if (defaultValue is not null)
        {
            if (!type.IsInstanceOfType(defaultValue))
            {
                throw new ArgumentException(
                    $"Default value of type {defaultValue.GetType().Name} is not assignable to parameter type {type.Name}.",
                    nameof(defaultValue));
            }

            if ((min is not null || max is not null) && TryToDouble(defaultValue, out var dv))
            {
                if (min is { } dlo && dv < dlo)
                {
                    throw new ArgumentException($"Default ({dv}) is below Min ({dlo}).", nameof(defaultValue));
                }

                if (max is { } dhi && dv > dhi)
                {
                    throw new ArgumentException($"Default ({dv}) is above Max ({dhi}).", nameof(defaultValue));
                }
            }
        }

        Default = defaultValue;
        Min = min;
        Max = max;
        Unit = unit;
        Help = help ?? string.Empty;
    }

    public string Name { get; }

    public Type Type { get; }

    public object? Default { get; }

    public double? Min { get; }

    public double? Max { get; }

    public Unit? Unit { get; }

    public string Help { get; }

    internal static bool IsNumericType(Type type) =>
        type == typeof(double) || type == typeof(float) || type == typeof(decimal)
        || type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(sbyte)
        || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(byte);

    internal static bool TryToDouble(object value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case decimal m: result = (double)m; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case short s: result = s; return true;
            case sbyte sb: result = sb; return true;
            case uint ui: result = ui; return true;
            case ulong ul: result = ul; return true;
            case ushort us: result = us; return true;
            case byte b: result = b; return true;
            default: result = double.NaN; return false;
        }
    }
}

/// <summary>The typed parameter schema of an operation (an ordered set of unique-named descriptors).</summary>
public sealed class ParameterSchema
{
    public static ParameterSchema Empty { get; } = new([]);

    private readonly Dictionary<string, ParameterDescriptor> _byName;

    public ParameterSchema(IReadOnlyList<ParameterDescriptor> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var copy = new ParameterDescriptor[parameters.Count];
        _byName = new Dictionary<string, ParameterDescriptor>(parameters.Count, StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i] ?? throw new ArgumentException("Schema must not contain null parameters.", nameof(parameters));
            if (!_byName.TryAdd(p.Name, p))
            {
                throw new ArgumentException($"Duplicate parameter name '{p.Name}'.", nameof(parameters));
            }

            copy[i] = p;
        }

        Parameters = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; }

    public bool TryGet(string name, [MaybeNullWhen(false)] out ParameterDescriptor descriptor)
    {
        if (name is not null)
        {
            return _byName.TryGetValue(name, out descriptor);
        }

        descriptor = null;
        return false;
    }

    /// <summary>
    /// Validates a set of concrete values against this schema — the common check every operation
    /// composes with its own preconditions (doc 13): rejects unknown names, missing required values
    /// (no default), wrong CLR types, and out-of-range numeric values. Returns typed failures.
    /// </summary>
    public ValidationResult Validate(IParameterSet values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var errors = new List<string>();

        foreach (var name in values.Names)
        {
            if (!_byName.ContainsKey(name))
            {
                errors.Add($"Unknown parameter '{name}'.");
            }
        }

        foreach (var descriptor in Parameters)
        {
            if (!values.TryGetValue(descriptor.Name, out var raw))
            {
                if (descriptor.Default is null)
                {
                    errors.Add($"Missing required parameter '{descriptor.Name}'.");
                }

                continue;
            }

            if (raw is null)
            {
                errors.Add($"Parameter '{descriptor.Name}' must not be null.");
                continue;
            }

            if (!descriptor.Type.IsInstanceOfType(raw))
            {
                errors.Add($"Parameter '{descriptor.Name}' must be {descriptor.Type.Name} but was {raw.GetType().Name}.");
                continue;
            }

            if ((descriptor.Min is not null || descriptor.Max is not null)
                && ParameterDescriptor.TryToDouble(raw, out var num))
            {
                if (descriptor.Min is { } lo && num < lo)
                {
                    errors.Add($"Parameter '{descriptor.Name}' ({num}) is below the minimum {lo}.");
                }

                if (descriptor.Max is { } hi && num > hi)
                {
                    errors.Add($"Parameter '{descriptor.Name}' ({num}) is above the maximum {hi}.");
                }
            }
        }

        return errors.Count == 0 ? ValidationResult.Success : ValidationResult.Fail([.. errors]);
    }
}

/// <summary>The concrete parameter values passed to an operation run. Immutable.</summary>
public interface IParameterSet
{
    /// <summary>The names present in this set (for schema validation / unknown-key detection).</summary>
    IReadOnlyCollection<string> Names { get; }

    bool Contains(string name);

    /// <summary>Gets the raw (untyped) value, if present. Used by schema validation.</summary>
    bool TryGetValue(string name, out object? value);

    bool TryGet<T>(string name, [MaybeNullWhen(false)] out T value);

    /// <summary>Gets a value or throws if missing/wrong type.</summary>
    T Get<T>(string name);
}

/// <inheritdoc />
public sealed class ParameterSet : IParameterSet
{
    public static IParameterSet Empty { get; } = new ParameterSet(new Dictionary<string, object?>());

    private readonly Dictionary<string, object?> _values;

    public ParameterSet(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in values)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                throw new ArgumentException("Parameter names must be non-empty, non-whitespace.", nameof(values));
            }

            _values[kv.Key] = kv.Value;
        }
    }

    public IReadOnlyCollection<string> Names => _values.Keys;

    public bool Contains(string name) => name is not null && _values.ContainsKey(name);

    public bool TryGetValue(string name, out object? value)
    {
        if (name is not null)
        {
            return _values.TryGetValue(name, out value);
        }

        value = null;
        return false;
    }

    public bool TryGet<T>(string name, [MaybeNullWhen(false)] out T value)
    {
        if (name is not null && _values.TryGetValue(name, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public T Get<T>(string name)
        => TryGet<T>(name, out var value)
            ? value
            : throw new KeyNotFoundException($"Missing or wrong-typed parameter '{name}' (expected {typeof(T).Name}).");
}
