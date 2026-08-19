using SmartAnalysis.Application.Analysis;
using SmartAnalysis.UI.ViewModels;
using Xunit;

namespace SmartAnalysis.UiTests.ViewModels;

/// <summary>The measurement result card VM projects the optional table into columns + rows, and gates its sections.</summary>
public sealed class StatisticsResultViewModelTests
{
    [Fact]
    public void Exposes_the_table_columns_and_rows_when_a_table_is_present()
    {
        var table = new MeasurementTableDto(
            ["Position (um)", "Value (nm)"],
            [["1.000", "10.00"], ["2.000", "6.000"]]);
        var vm = new StatisticsResultViewModel(new StatisticsResult(true, "curve", [], [], null, table));

        Assert.True(vm.HasTable);
        Assert.Equal(new[] { "Position (um)", "Value (nm)" }, vm.TableColumns);
        Assert.Equal(2, vm.TableRows.Count);
        Assert.Equal(new[] { "1.000", "10.00" }, vm.TableRows[0]);
        Assert.False(vm.HasHistogram);
    }

    [Fact]
    public void Has_no_table_when_none_is_projected()
    {
        var vm = new StatisticsResultViewModel(new StatisticsResult(true, "img", [], [3, 1], null));

        Assert.False(vm.HasTable);
        Assert.Empty(vm.TableRows);
        Assert.True(vm.HasHistogram);
    }
}
