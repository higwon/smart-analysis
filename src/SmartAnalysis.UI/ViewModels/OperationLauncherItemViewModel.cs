using System;
using System.Windows.Input;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.UI.Mvvm;

namespace SmartAnalysis.UI.ViewModels;

/// <summary>
/// One entry in the Analyze ▾ launcher, projected from an <see cref="OperationLauncherItem"/> (which itself
/// came from the operation registry — the shell never hardcodes the list). Grouped in the popover by
/// <see cref="CategoryLabel"/>; invoking <see cref="LaunchCommand"/> asks the shell to resolve an editor
/// (a semantic override or the generic schema form) for this operation id.
/// </summary>
public sealed class OperationLauncherItemViewModel
{
    public OperationLauncherItemViewModel(OperationLauncherItem item, Action launch)
    {
        ArgumentNullException.ThrowIfNull(item);
        Id = item.Id;
        DisplayName = item.DisplayName;
        Summary = item.Summary;
        Category = item.Category;
        LaunchCommand = new RelayCommand(launch ?? throw new ArgumentNullException(nameof(launch)));
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Summary { get; }

    public OperationCategory Category { get; }

    /// <summary>Upper-case category name — the group header the launcher renders (PROCESS / MEASURE / …).</summary>
    public string CategoryLabel => Category.ToString().ToUpperInvariant();

    /// <summary>An <c>SA.Icon.*</c> key resolved to a geometry by the view.</summary>
    public string IconKey => Category == OperationCategory.Measure ? "SA.Icon.Statistics" : "SA.Icon.Parameters";

    public ICommand LaunchCommand { get; }
}
