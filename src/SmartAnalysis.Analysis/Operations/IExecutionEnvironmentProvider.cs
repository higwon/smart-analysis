using System.Runtime.InteropServices;
using SmartAnalysis.Domain.Provenance;

namespace SmartAnalysis.Analysis.Operations;

/// <summary>
/// Captures the <see cref="ExecutionEnvironment"/> a provenance step ran in (doc 16). Injected into
/// operations so every run records where/when it executed — the operation itself owns no clock or
/// host lookup. The composition root supplies the real implementation; tests supply a fixed
/// environment so runs stay reproducible.
/// </summary>
public interface IExecutionEnvironmentProvider
{
    ExecutionEnvironment Capture();
}

/// <summary>
/// Default provider: snapshots the running OS/machine and the current UTC time, with the app version
/// supplied by the composition root. The timestamp is the only non-deterministic part (it belongs to
/// the environment, not the numeric result — an operation's <c>IsDeterministic</c> concerns its output).
/// </summary>
public sealed class SystemExecutionEnvironmentProvider : IExecutionEnvironmentProvider
{
    private readonly string _appVersion;

    public SystemExecutionEnvironmentProvider(string appVersion = "0.0.0-dev")
        => _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "0.0.0-dev" : appVersion;

    public ExecutionEnvironment Capture()
        => new(_appVersion, RuntimeInformation.OSDescription, Environment.MachineName, DateTimeOffset.UtcNow);
}
