namespace SmartAnalysis.Domain.Provenance;

/// <summary>
/// A non-fatal warning from an operation — a typed value, never a swallowed exception or free-text
/// comment (fixing legacy doc 07 M5 / doc 06). Immutable.
/// </summary>
public sealed record OperationWarning
{
    public OperationWarning(string code, string message)
    {
        Code = DomainGuard.Text(code, nameof(code));
        Message = DomainGuard.Text(message, nameof(message));
    }

    public string Code { get; }

    public string Message { get; }
}

/// <summary>
/// A typed operation error preserved in provenance (never swallowed). Immutable.
/// </summary>
public sealed record OperationError
{
    public OperationError(string code, string message)
    {
        Code = DomainGuard.Text(code, nameof(code));
        Message = DomainGuard.Text(message, nameof(message));
    }

    public string Code { get; }

    public string Message { get; }
}
