using System.Collections.ObjectModel;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Provenance;

/// <summary>
/// One recorded analysis step — the reproducible unit of provenance (doc 16). Captures the input
/// identity+version, the operation id+version, its <b>parameters with units</b>, execution order and
/// environment, typed warnings/errors, and optional parent-result / user-edit / AI / ML annotations.
/// Immutable; collections are defensively copied and read-only.
/// </summary>
public sealed class ProvenanceStep
{
    public ProvenanceStep(
        string stepId,
        DatasetId inputDatasetId,
        int inputVersion,
        string operationId,
        int operationVersion,
        int order,
        ExecutionEnvironment environment,
        IReadOnlyDictionary<string, PhysicalValue>? parameters = null,
        IReadOnlyList<OperationWarning>? warnings = null,
        IReadOnlyList<OperationError>? errors = null,
        DatasetId? parentResultId = null,
        UserEdit? userChange = null,
        AiInvolvement? ai = null,
        MlModelRef? model = null)
    {
        StepId = DomainGuard.Text(stepId, nameof(stepId));
        InputDatasetId = inputDatasetId;
        InputVersion = DomainGuard.NonNegative(inputVersion, nameof(inputVersion));
        OperationId = DomainGuard.Text(operationId, nameof(operationId));
        OperationVersion = DomainGuard.NonNegative(operationVersion, nameof(operationVersion));
        Order = DomainGuard.NonNegative(order, nameof(order));
        Environment = DomainGuard.NotNull(environment, nameof(environment));
        Parameters = parameters is null || parameters.Count == 0
            ? EmptyParameters
            : new ReadOnlyDictionary<string, PhysicalValue>(
                new Dictionary<string, PhysicalValue>(parameters, StringComparer.Ordinal));
        Warnings = warnings is null || warnings.Count == 0 ? [] : warnings.ToArray().AsReadOnly();
        Errors = errors is null || errors.Count == 0 ? [] : errors.ToArray().AsReadOnly();
        ParentResultId = parentResultId;
        UserChange = userChange;
        Ai = ai;
        Model = model;
    }

    private static readonly IReadOnlyDictionary<string, PhysicalValue> EmptyParameters =
        new ReadOnlyDictionary<string, PhysicalValue>(new Dictionary<string, PhysicalValue>(StringComparer.Ordinal));

    public string StepId { get; }

    /// <summary>Identity + version of the input this step consumed.</summary>
    public DatasetId InputDatasetId { get; }

    public int InputVersion { get; }

    /// <summary>Operation id + version (bumped on numeric change — doc 13).</summary>
    public string OperationId { get; }

    public int OperationVersion { get; }

    /// <summary>Execution order within the owning <see cref="Provenance"/>.</summary>
    public int Order { get; }

    public ExecutionEnvironment Environment { get; }

    /// <summary>Parameters used, <b>with units</b> (read-only).</summary>
    public IReadOnlyDictionary<string, PhysicalValue> Parameters { get; }

    public IReadOnlyList<OperationWarning> Warnings { get; }

    public IReadOnlyList<OperationError> Errors { get; }

    /// <summary>The derived-from result, if this step produced a new dataset.</summary>
    public DatasetId? ParentResultId { get; }

    public UserEdit? UserChange { get; }

    public AiInvolvement? Ai { get; }

    public MlModelRef? Model { get; }
}
