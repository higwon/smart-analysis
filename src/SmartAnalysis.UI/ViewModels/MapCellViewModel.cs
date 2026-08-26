using SmartAnalysis.UI.Mvvm;

namespace SmartAnalysis.UI.ViewModels;

/// <summary>
/// One cell of a force–volume map's point picker: the curve at that grid position, and whether it is the one on
/// the stage. The cell knows its <see cref="Column"/>/<see cref="Row"/> so a tooltip can say where on the sample
/// it is without the view recomputing the geometry.
/// </summary>
public sealed class MapCellViewModel : ObservableObject
{
    private bool _isSelected;

    public MapCellViewModel(int index, int column, int row, string tooltip)
    {
        Index = index;
        Column = column;
        Row = row;
        Tooltip = tooltip;
    }

    /// <summary>The point index this cell selects — the same index the extract operation takes.</summary>
    public int Index { get; }

    /// <summary>1-based, as a person counts.</summary>
    public int Column { get; }

    /// <summary>1-based, as a person counts.</summary>
    public int Row { get; }

    /// <summary>Where on the sample this curve was measured.</summary>
    public string Tooltip { get; }

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}
