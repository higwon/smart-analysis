using System.Buffers.Binary;
using System.Text.Json;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Infrastructure.Persistence.Workspace;

/// <summary>
/// Directory-package <see cref="IWorkspaceStore"/> (ADR-017): a folder with <c>manifest.json</c> +
/// <c>buffers/&lt;id&gt;.bin</c> (explicit little-endian float32). Maps the serializer-free Domain
/// types to JSON DTOs and back; units resolve via the injected <see cref="IUnitRegistry"/>. MVP kind
/// is <see cref="ScanImageDataset"/>; buffers are stored inline so reopen is self-contained.
/// </summary>
public sealed class DirectoryWorkspaceStore : IWorkspaceStore
{
    public const string SchemaVersion = "1.0.0";
    private const string ManifestFile = "manifest.json";
    private const string BuffersDir = "buffers";
    private const string ScanImageKind = "ScanImage";
    private const string AppVersion = "0.0.0-dev";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IUnitRegistry _units;

    public DirectoryWorkspaceStore(IUnitRegistry units)
        => _units = units ?? throw new ArgumentNullException(nameof(units));

    public void Save(SmartAnalysis.Application.Workspaces.Workspace workspace, string path)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(path);
        var buffersPath = Path.Combine(path, BuffersDir);
        if (Directory.Exists(buffersPath))
        {
            Directory.Delete(buffersPath, recursive: true); // clear stale buffers from a previous save
        }

        Directory.CreateDirectory(buffersPath);

        var datasets = new List<DatasetDto>();
        foreach (var dataset in workspace.Datasets)
        {
            if (dataset is not ScanImageDataset image)
            {
                throw new NotSupportedException(
                    $"Workspace persistence v1 supports {nameof(ScanImageDataset)} only; got {dataset.GetType().Name}.");
            }

            string bufferFile = $"{image.Id.Value:D}.bin";
            WriteBuffer(Path.Combine(buffersPath, bufferFile), image.Data.Memory.Span);
            datasets.Add(ToDto(image, bufferFile));
        }

        var manifest = new WorkspaceManifest(
            SchemaVersion,
            CreatedUtc: DateTimeOffset.UtcNow.ToString("O"),
            AppVersion,
            Active: new ActiveContextDto(
                workspace.Active.ActiveId?.Value.ToString("D"),
                [.. workspace.Active.Comparison.Select(c => c.Value.ToString("D"))]),
            Datasets: datasets);

        File.WriteAllText(Path.Combine(path, ManifestFile), JsonSerializer.Serialize(manifest, Json));
    }

    public WorkspaceOpenResult Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return WorkspaceOpenResult.Failure(WorkspaceOpenErrorKind.Io, $"Workspace directory not found: '{path}'.");
        }

        var manifestPath = Path.Combine(path, ManifestFile);
        if (!File.Exists(manifestPath))
        {
            return WorkspaceOpenResult.Failure(WorkspaceOpenErrorKind.NotAWorkspace, $"No {ManifestFile} in '{path}'.");
        }

        WorkspaceManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(manifestPath), Json);
        }
        catch (JsonException ex)
        {
            return WorkspaceOpenResult.Failure(WorkspaceOpenErrorKind.NotAWorkspace, $"Unreadable manifest: {ex.Message}");
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.SchemaVersion))
        {
            return WorkspaceOpenResult.Failure(WorkspaceOpenErrorKind.NotAWorkspace, "Manifest is empty or has no schema version.");
        }

        if (manifest.SchemaVersion != SchemaVersion)
        {
            return WorkspaceOpenResult.Failure(
                WorkspaceOpenErrorKind.UnsupportedSchemaVersion,
                $"Schema version '{manifest.SchemaVersion}' is not supported (this build reads '{SchemaVersion}'; migration is P03).");
        }

        var workspace = new SmartAnalysis.Application.Workspaces.Workspace();
        try
        {
            foreach (var dto in manifest.Datasets ?? [])
            {
                workspace.Add(FromDto(dto, Path.Combine(path, BuffersDir)));
            }

            RestoreActiveContext(workspace, manifest.Active);
        }
        catch (WorkspaceCorruptException ex)
        {
            workspace.Dispose();
            return WorkspaceOpenResult.Failure(WorkspaceOpenErrorKind.Corrupt, ex.Message);
        }
        catch (Exception ex)
        {
            workspace.Dispose();
            return WorkspaceOpenResult.Failure(WorkspaceOpenErrorKind.Corrupt, $"Failed to restore workspace: {ex.Message}");
        }

        return WorkspaceOpenResult.Success(workspace);
    }

    // --- Save mapping ---

    private DatasetDto ToDto(ScanImageDataset image, string bufferFile) => new(
        Id: image.Id.Value.ToString("D"),
        Kind: ScanImageKind,
        Source: new DataSourceDto(image.Source.FormatId, image.Source.OriginalFilePath, image.Source.ContentHash),
        X: ToAxisDto(image.X),
        Y: ToAxisDto(image.Y),
        Channel: new ChannelDto(image.Channel.Key, image.Channel.Kind.ToString(), image.Channel.Unit.Symbol, image.Channel.DisplayName),
        Metadata: new MetadataDto(
            image.Metadata.InstrumentModel,
            image.Metadata.AcquiredAt.ToString("O"),
            new Dictionary<string, string>(image.Metadata.Extended)),
        Provenance: ToProvenanceDto(image.Provenance),
        BufferFile: bufferFile);

    private static AxisDto ToAxisDto(Axis axis)
        => new(axis.Name, axis.Unit.Symbol, axis.Origin, axis.Step, axis.Count, axis.Direction.ToString());

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

    private static void WriteBuffer(string file, ReadOnlySpan<float> data)
    {
        var bytes = new byte[data.Length * sizeof(float)];
        for (int i = 0; i < data.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), data[i]);
        }

        File.WriteAllBytes(file, bytes);
    }

    // --- Load mapping (throws WorkspaceCorruptException on any inconsistency) ---

    private ScanImageDataset FromDto(DatasetDto dto, string buffersPath)
    {
        if (!string.Equals(dto.Kind, ScanImageKind, StringComparison.Ordinal))
        {
            throw new WorkspaceCorruptException($"Unsupported dataset kind '{dto.Kind}' for id '{dto.Id}'.");
        }

        var id = ParseId(dto.Id, "dataset id");
        var x = FromAxisDto(dto.X);
        var y = FromAxisDto(dto.Y);
        var channel = new ChannelDescriptor(dto.Channel.Key, ParseEnum<ChannelKind>(dto.Channel.Kind, "channel kind"), ResolveUnit(dto.Channel.Unit), dto.Channel.DisplayName);
        var metadata = new ScanMetadata(dto.Metadata.InstrumentModel, ParseTimestamp(dto.Metadata.AcquiredAt), dto.Metadata.Extended ?? []);
        var source = new DataSource(dto.Source.FormatId, dto.Source.OriginalFilePath, dto.Source.ContentHash);
        var provenance = FromProvenanceDto(dto.Provenance);

        var buffer = ReadBuffer(Path.Combine(buffersPath, dto.BufferFile), x.Count, y.Count);
        try
        {
            return new ScanImageDataset(id, source, x, y, channel, buffer, metadata, provenance);
        }
        catch (Exception ex) when (ex is not WorkspaceCorruptException)
        {
            buffer.Dispose(); // ctor rejected the reconstructed state → we still own the buffer
            throw new WorkspaceCorruptException($"Dataset '{dto.Id}' could not be reconstructed: {ex.Message}");
        }
    }

    private Axis FromAxisDto(AxisDto dto)
        => new(dto.Name, ResolveUnit(dto.Unit), dto.Origin, dto.Step, dto.Count, ParseEnum<AxisDirection>(dto.Direction, "axis direction"));

    private ProvenanceRecord FromProvenanceDto(ProvenanceDto dto)
    {
        if (string.IsNullOrEmpty(dto.ParentId) && (dto.Steps is null || dto.Steps.Count == 0))
        {
            return ProvenanceRecord.Root;
        }

        var steps = (dto.Steps ?? []).Select(FromStepDto).ToArray();
        return ProvenanceRecord.DerivedFrom(ParseId(dto.ParentId!, "provenance parent id"), steps);
    }

    private ProvenanceStep FromStepDto(StepDto s) => new(
        s.StepId,
        ParseId(s.InputDatasetId, "step input dataset id"),
        s.InputVersion,
        s.OperationId,
        s.OperationVersion,
        s.Order,
        new ExecutionEnvironment(s.Environment.AppVersion, s.Environment.OperatingSystem, s.Environment.MachineName, ParseTimestamp(s.Environment.Timestamp)),
        (s.Parameters ?? []).ToDictionary(p => p.Key, p => new PhysicalValue(p.Value, ResolveUnit(p.Unit))),
        [.. (s.Warnings ?? []).Select(w => new OperationWarning(w.Code, w.Message))],
        [.. (s.Errors ?? []).Select(e => new OperationError(e.Code, e.Message))],
        string.IsNullOrEmpty(s.ParentResultId) ? null : ParseId(s.ParentResultId, "step parent result id"));

    private ScanBuffer<float> ReadBuffer(string file, int width, int height)
    {
        if (!File.Exists(file))
        {
            throw new WorkspaceCorruptException($"Missing buffer file '{Path.GetFileName(file)}'.");
        }

        int count = width * height;
        var bytes = File.ReadAllBytes(file);
        if (bytes.Length < count * sizeof(float))
        {
            throw new WorkspaceCorruptException($"Buffer '{Path.GetFileName(file)}' is {bytes.Length} bytes; expected {count * sizeof(float)}.");
        }

        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
        }

        return ScanBuffer<float>.TakeOwnership(data, width, height);
    }

    private static void RestoreActiveContext(SmartAnalysis.Application.Workspaces.Workspace workspace, ActiveContextDto? active)
    {
        if (active is null)
        {
            return;
        }

        var comparison = (active.Comparison ?? [])
            .Select(c => ParseId(c, "comparison id"))
            .Where(workspace.Contains)
            .ToList();
        if (comparison.Count > 0)
        {
            workspace.SetComparison(comparison);
        }

        if (!string.IsNullOrEmpty(active.ActiveId))
        {
            var id = ParseId(active.ActiveId, "active id");
            if (workspace.Contains(id))
            {
                workspace.SetActive(id);
            }
        }
    }

    private Unit ResolveUnit(string symbol)
        => _units.TryGetUnit(symbol, out var unit)
            ? unit
            : throw new WorkspaceCorruptException($"Unknown unit symbol '{symbol}'.");

    private static DatasetId ParseId(string value, string what)
        => Guid.TryParse(value, out var guid) ? new DatasetId(guid) : throw new WorkspaceCorruptException($"Invalid {what}: '{value}'.");

    private static TEnum ParseEnum<TEnum>(string value, string what) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, out var e) && Enum.IsDefined(e) ? e : throw new WorkspaceCorruptException($"Invalid {what}: '{value}'.");

    private static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts)
            ? ts
            : throw new WorkspaceCorruptException($"Invalid timestamp: '{value}'.");
}

/// <summary>Internal signal that a package is structurally broken; mapped to a typed Corrupt failure.</summary>
internal sealed class WorkspaceCorruptException(string message) : Exception(message);
