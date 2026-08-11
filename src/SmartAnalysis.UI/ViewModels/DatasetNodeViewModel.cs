using System.Collections.ObjectModel;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.UI.Mvvm;

namespace SmartAnalysis.UI.ViewModels;

/// <summary>
/// A node in the workspace explorer lineage tree (doc 22/24). Represents a dataset (can be active) or an
/// attached measurement (never independently active). Selected / active / comparison are distinct states,
/// each surfaced to the view so the design system can style them differently (accent rail for active).
/// </summary>
public sealed class DatasetNodeViewModel : ObservableObject
{
    private bool _isActive;
    private bool _isInComparison;

    public DatasetNodeViewModel(DatasetId id, string label, string iconKey, bool isMeasurement)
    {
        Id = id;
        Label = label;
        IconKey = iconKey;
        IsMeasurement = isMeasurement;
    }

    public DatasetId Id { get; }

    public string Label { get; }

    /// <summary>An <c>SA.Icon.*</c> key resolved to a geometry by the view (never a WPF type here).</summary>
    public string IconKey { get; }

    /// <summary>True for an attached analysis result (not a navigable/active dataset).</summary>
    public bool IsMeasurement { get; }

    public ObservableCollection<DatasetNodeViewModel> Children { get; } = new();

    /// <summary>The workspace <c>ActiveContext.ActiveId</c> — distinct from mere selection (accent rail).</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>A member of the current Before/After comparison set.</summary>
    public bool IsInComparison
    {
        get => _isInComparison;
        set => SetProperty(ref _isInComparison, value);
    }
}
