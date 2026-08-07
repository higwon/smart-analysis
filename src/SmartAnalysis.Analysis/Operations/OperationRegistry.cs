namespace SmartAnalysis.Analysis.Operations;

/// <summary>
/// Default <see cref="IOperationRegistry"/> — built from the operations registered via explicit
/// per-module DI (ADR-005). Rejects duplicate operation ids at construction; an unregistered id is
/// simply not found (callers must never execute an unregistered operation).
/// </summary>
public sealed class OperationRegistry : IOperationRegistry
{
    private readonly Dictionary<string, IAnalysisOperation> _byId;

    public OperationRegistry(IEnumerable<IAnalysisOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _byId = new Dictionary<string, IAnalysisOperation>(StringComparer.Ordinal);
        foreach (var op in operations)
        {
            if (op is null)
            {
                throw new ArgumentException("Registered operations must not be null.", nameof(operations));
            }

            if (!_byId.TryAdd(op.Descriptor.Id, op))
            {
                throw new InvalidOperationException(
                    $"Duplicate operation id '{op.Descriptor.Id}': each operation id must be unique in the registry.");
            }
        }

        All = _byId.Values.Select(o => o.Descriptor).ToArray();
    }

    public IReadOnlyList<OperationDescriptor> All { get; }

    public bool TryGet(string id, out IAnalysisOperation operation)
    {
        if (id is not null && _byId.TryGetValue(id, out var op))
        {
            operation = op;
            return true;
        }

        operation = null!;
        return false;
    }

    public IEnumerable<OperationDescriptor> ApplicableTo(DataKind kind)
        => _byId.Values.Select(o => o.Descriptor).Where(d => d.Accepts(kind));
}
