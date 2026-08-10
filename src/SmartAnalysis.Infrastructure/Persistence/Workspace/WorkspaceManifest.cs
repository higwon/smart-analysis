namespace SmartAnalysis.Infrastructure.Persistence.Workspace;

// JSON DTOs for the workspace directory-package (ADR-017). These live in Infrastructure so the Domain
// provenance/dataset types stay serializer-free (ADR-013). Units persist as their symbol and resolve
// through the unit registry on load. Schema version is validated on open.

internal sealed record WorkspaceManifest(
    string SchemaVersion,
    string CreatedUtc,
    string AppVersion,
    ActiveContextDto Active,
    List<DatasetDto> Datasets);

internal sealed record ActiveContextDto(string? ActiveId, List<string> Comparison);

internal sealed record DatasetDto(
    string Id,
    string Kind,
    DataSourceDto Source,
    AxisDto X,
    AxisDto Y,
    ChannelDto Channel,
    MetadataDto Metadata,
    ProvenanceDto Provenance,
    string BufferFile);

internal sealed record DataSourceDto(string FormatId, string? OriginalFilePath, string? ContentHash);

internal sealed record AxisDto(string Name, string Unit, double Origin, double Step, int Count, string Direction);

internal sealed record ChannelDto(string Key, string Kind, string Unit, string DisplayName);

internal sealed record MetadataDto(string InstrumentModel, string AcquiredAt, Dictionary<string, string> Extended);

internal sealed record ProvenanceDto(string? ParentId, List<StepDto> Steps);

internal sealed record StepDto(
    string StepId,
    string InputDatasetId,
    int InputVersion,
    string OperationId,
    int OperationVersion,
    int Order,
    EnvironmentDto Environment,
    List<ParameterDto> Parameters,
    List<DiagnosticDto> Warnings,
    List<DiagnosticDto> Errors,
    string? ParentResultId);

internal sealed record EnvironmentDto(string AppVersion, string OperatingSystem, string MachineName, string Timestamp);

internal sealed record ParameterDto(string Key, double Value, string Unit);

internal sealed record DiagnosticDto(string Code, string Message);
