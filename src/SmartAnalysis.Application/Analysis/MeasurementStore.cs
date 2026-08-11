using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// Session-scoped store of <see cref="AnalysisArtifact"/> measurements, each attached to its source dataset
/// by <see cref="AnalysisArtifact.SourceId"/> (doc 22 §Measurement). It lives <b>beside</b> the
/// <see cref="Workspaces.Workspace"/> deliberately: the workspace owns only <c>AfmDataset</c>s and its
/// <c>ActiveContext</c> is dataset-only, so a measurement is never an active/comparison target — it is a
/// result <b>bound to a source</b> and shown under/with it. Keeping the artifact here preserves the real
/// entity (Id/SourceId/OperationId/Provenance) instead of discarding it after reading a few scalars.
/// <para>MVP scope: attach + query. Orphan cleanup when a source dataset is removed is a follow-up — the
/// shell exposes no dataset removal yet.</para>
/// </summary>
public sealed class MeasurementStore
{
    private readonly Dictionary<DatasetId, List<AnalysisArtifact>> _bySource = new();
    private readonly Dictionary<DatasetId, AnalysisArtifact> _byId = new();

    /// <summary>Raised after the set of attached measurements changes.</summary>
    public event EventHandler? MeasurementsChanged;

    /// <summary>Attaches a measurement to its source (idempotent by artifact id).</summary>
    public void Attach(AnalysisArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!_byId.TryAdd(artifact.Id, artifact))
        {
            return; // already attached
        }

        if (!_bySource.TryGetValue(artifact.SourceId, out var list))
        {
            list = new List<AnalysisArtifact>();
            _bySource[artifact.SourceId] = list;
        }

        list.Add(artifact);
        MeasurementsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The measurements attached to <paramref name="sourceId"/> (empty if none), in attach order.</summary>
    public IReadOnlyList<AnalysisArtifact> ForSource(DatasetId sourceId)
        => _bySource.TryGetValue(sourceId, out var list) ? list.ToArray() : [];

    /// <summary>Looks up a measurement by its own artifact id.</summary>
    public bool TryGet(DatasetId artifactId, out AnalysisArtifact artifact)
        => _byId.TryGetValue(artifactId, out artifact!);
}
