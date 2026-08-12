using System.IO;
using SmartAnalysis.UI.DesignSystem.Theming;
using Xunit;

namespace SmartAnalysis.UiTests.Theming;

/// <summary>
/// The theme-preference persistence (no WPF needed). Round-trips a saved preference and verifies the
/// corrupt/undefined-value guard falls back to <see cref="AppTheme.System"/> rather than injecting an
/// out-of-range enum.
/// </summary>
public sealed class ThemePreferenceStoreTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "sa-uitests", Path.GetRandomFileName() + ".json");

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void Save_then_load_round_trips_the_preference(AppTheme theme)
    {
        var path = TempPath();
        try
        {
            new ThemePreferenceStore(path).Save(theme);
            Assert.Equal(theme, new ThemePreferenceStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_of_a_missing_file_is_system()
        => Assert.Equal(AppTheme.System, new ThemePreferenceStore(TempPath()).Load());

    [Fact]
    public void Load_of_a_corrupt_out_of_range_value_falls_back_to_system()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "{\"Theme\":\"999\"}"); // numeric string parses to an undefined enum
            Assert.Equal(AppTheme.System, new ThemePreferenceStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
