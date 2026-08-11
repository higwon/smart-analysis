using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartAnalysis.UI.Mvvm;

/// <summary>
/// Minimal first-party MVVM base (U01): <see cref="INotifyPropertyChanged"/> with a <see cref="SetProperty{T}"/>
/// helper. Kept in-house so the UI takes no MVVM-toolkit dependency (CommunityToolkit.Mvvm is a Candidate
/// pending an ADR, doc 20); if that toolkit is later adopted, these types are a drop-in shape match.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets <paramref name="field"/> and raises change notification only when the value differs.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
