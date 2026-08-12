using System.Text.Json;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.Persistence.Workspace;

namespace SmartAnalysis.Infrastructure.FileFormats.Tiff;

/// <summary>
/// The JSON side-car the PSIA-TIFF writer embeds in the standard <c>ImageDescription</c> tag so a written
/// result round-trips its <b>identity and provenance</b> (F05) — the pixels/axes/channel already round-trip
/// through the PSIA header. It deliberately reuses the workspace-package DTO records
/// (<see cref="ProvenanceDto"/> et al.), so the provenance JSON is byte-identical in shape to the P01
/// directory package (one schema, two carriers). Absent or unrecognized descriptions fall back to the
/// reader's legacy behaviour (a fresh id + <see cref="ProvenanceRecord.Root"/>), so real PSIA files are
/// unaffected.
/// </summary>
internal sealed record TiffDomainSidecar(int Schema, string DatasetId, ProvenanceDto Provenance)
{
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Serializes a dataset's identity + provenance to the side-car JSON.</summary>
    public static string Serialize(ScanImageDataset image)
    {
        var sidecar = new TiffDomainSidecar(
            CurrentSchema,
            image.Id.Value.ToString("D"),
            ToProvenanceDto(image.Provenance));
        return JsonSerializer.Serialize(sidecar, Json);
    }

    /// <summary>
    /// Parses the side-car JSON, resolving parameter units through <paramref name="units"/>. Returns false
    /// for anything that is not our current-schema envelope (so a foreign ImageDescription is ignored, not
    /// mistaken for corruption).
    /// </summary>
    public static bool TryParse(string? json, IUnitRegistry units, out DatasetId id, out ProvenanceRecord provenance)
    {
        id = default;
        provenance = ProvenanceRecord.Root;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var sidecar = JsonSerializer.Deserialize<TiffDomainSidecar>(json, Json);
            if (sidecar is null || sidecar.Schema != CurrentSchema || sidecar.Provenance is null
                || !Guid.TryParse(sidecar.DatasetId, out var guid))
            {
                return false;
            }

            id = new DatasetId(guid);
            provenance = FromProvenanceDto(sidecar.Provenance, units);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            // Malformed or foreign description → treat as no side-car (legacy read path).
            return false;
        }
    }

    // --- Domain → DTO (pure; mirrors DirectoryWorkspaceStore so both carriers share the schema) ---

    private static ProvenanceDto ToProvenanceDto(ProvenanceRecord record) => new(
        record.ParentId?.Value.ToString("D"),
        [.. record.Steps.Select(ToStepDto)]);

    private static StepDto ToStepDto(ProvenanceStep step) => new(
        step.StepId,
        step.InputDatasetId.Value.ToString("D"),
        step.InputVersion,
        step.OperationId,
        step.OperationVersion,
        step.Order,
        new EnvironmentDto(step.Environment.AppVersion, step.Environment.OperatingSystem, step.Environment.MachineName, step.Environment.Timestamp.ToString("O")),
        [.. step.Parameters.Select(p => new ParameterDto(p.Key, p.Value.Value, p.Value.Unit.Symbol))],
        [.. step.Warnings.Select(w => new DiagnosticDto(w.Code, w.Message))],
        [.. step.Errors.Select(e => new DiagnosticDto(e.Code, e.Message))],
        step.ParentResultId?.Value.ToString("D"));

    // --- DTO → Domain (throws on bad content; TryParse turns that into "no side-car") ---

    private static ProvenanceRecord FromProvenanceDto(ProvenanceDto dto, IUnitRegistry units)
    {
        if (string.IsNullOrEmpty(dto.ParentId) && (dto.Steps is null || dto.Steps.Count == 0))
        {
            return ProvenanceRecord.Root;
        }

        var steps = (dto.Steps ?? []).Select(s => FromStepDto(s, units)).ToArray();
        return ProvenanceRecord.DerivedFrom(ParseId(dto.ParentId!), steps);
    }

    private static ProvenanceStep FromStepDto(StepDto s, IUnitRegistry units) => new(
        s.StepId,
        ParseId(s.InputDatasetId),
        s.InputVersion,
        s.OperationId,
        s.OperationVersion,
        s.Order,
        new ExecutionEnvironment(s.Environment.AppVersion, s.Environment.OperatingSystem, s.Environment.MachineName, ParseTimestamp(s.Environment.Timestamp)),
        (s.Parameters ?? []).ToDictionary(p => p.Key, p => new PhysicalValue(p.Value, ResolveUnit(p.Unit, units))),
        [.. (s.Warnings ?? []).Select(w => new OperationWarning(w.Code, w.Message))],
        [.. (s.Errors ?? []).Select(e => new OperationError(e.Code, e.Message))],
        string.IsNullOrEmpty(s.ParentResultId) ? null : ParseId(s.ParentResultId));

    private static DatasetId ParseId(string value)
        => Guid.TryParse(value, out var guid) ? new DatasetId(guid) : throw new FormatException($"Invalid dataset id '{value}'.");

    private static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts)
            ? ts
            : throw new FormatException($"Invalid timestamp '{value}'.");

    private static Unit ResolveUnit(string symbol, IUnitRegistry units)
        => units.TryGetUnit(symbol, out var unit) ? unit : throw new FormatException($"Unknown unit symbol '{symbol}'.");
}
