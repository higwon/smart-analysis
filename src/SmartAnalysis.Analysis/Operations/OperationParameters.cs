using System.Diagnostics.CodeAnalysis;
using SmartAnalysis.Analysis;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations;

/// <summary>
/// Declares one operation parameter: name, CLR type, optional default, allowed numeric range, unit,
/// and help text. Part of an operation's self-describing schema (doc 13). Immutable value object.
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
}

/// <summary>The typed parameter schema of an operation (an ordered set of unique-named descriptors).</summary>
public sealed class ParameterSchema
{
    public static ParameterSchema Empty { get; } = new([]);

    public ParameterSchema(IReadOnlyList<ParameterDescriptor> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var copy = new ParameterDescriptor[parameters.Count];
        var names = new HashSet<string>(parameters.Count, StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i] ?? throw new ArgumentException("Schema must not contain null parameters.", nameof(parameters));
            if (!names.Add(p.Name))
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
        foreach (var p in Parameters)
        {
            if (string.Equals(p.Name, name, StringComparison.Ordinal))
            {
                descriptor = p;
                return true;
            }
        }

        descriptor = null;
        return false;
    }
}

/// <summary>The concrete parameter values passed to an operation run. Immutable.</summary>
public interface IParameterSet
{
    bool Contains(string name);

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
        _values = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    }

    public bool Contains(string name) => _values.ContainsKey(name);

    public bool TryGet<T>(string name, [MaybeNullWhen(false)] out T value)
    {
        if (_values.TryGetValue(name, out var raw) && raw is T typed)
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
