namespace SmartAnalysis.Domain.Provenance;

/// <summary>A manual user override recorded on a provenance step. Immutable.</summary>
public sealed record UserEdit
{
    public UserEdit(string description, DateTimeOffset at)
    {
        Description = DomainGuard.Text(description, nameof(description));
        At = at;
    }

    public string Description { get; }

    public DateTimeOffset At { get; }
}

/// <summary>
/// Records AI involvement in a step: whether the AI assistant proposed it, and who approved it (and
/// when). Distinguishes AI-suggested vs user-approved (doc 14). Immutable.
/// </summary>
public sealed record AiInvolvement(bool AiProposed, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null);

/// <summary>Reference to the ML model + version used by a (non-deterministic) step (doc 18). Immutable.</summary>
public sealed record MlModelRef
{
    public MlModelRef(string modelId, string version)
    {
        ModelId = DomainGuard.Text(modelId, nameof(modelId));
        Version = DomainGuard.Text(version, nameof(version));
    }

    public string ModelId { get; }

    public string Version { get; }
}
