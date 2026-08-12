using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using SmartAnalysis.UI.DesignSystem.Theming;
using Xunit;

namespace SmartAnalysis.UiTests.Theming;

/// <summary>
/// <see cref="ThemeManager"/> behaviour. The state-machine + resolve tests need no WPF Application (a
/// <c>ThemeManager</c> with no app skips the palette swap). The live-swap test constructs a real WPF
/// Application with the design system merged, on an STA thread, and proves that <c>Apply</c> actually
/// changes the resolved <c>SA.Brush.*</c> value in the resource tree — the exact mechanism every
/// <c>DynamicResource</c> consumer repaints from.
/// </summary>
public sealed class ThemeManagerTests
{
    private static ThemePreferenceStore TempStore()
        => new(Path.Combine(Path.GetTempPath(), "sa-uitests", Path.GetRandomFileName() + ".json"));

    [Theory]
    [InlineData(AppTheme.Light, AppTheme.Light)]
    [InlineData(AppTheme.Dark, AppTheme.Dark)]
    public void ResolveEffective_maps_explicit_themes(AppTheme preference, AppTheme expected)
        => Assert.Equal(expected, ThemeManager.ResolveEffective(preference));

    [Fact]
    public void Apply_updates_state_and_raises_changed_even_without_an_application()
    {
        var manager = new ThemeManager(TempStore());
        var raised = 0;
        manager.ThemeChanged += (_, _) => raised++;

        manager.Apply(AppTheme.Dark, persist: false);

        Assert.Equal(AppTheme.Dark, manager.Preference);
        Assert.Equal(AppTheme.Dark, manager.EffectiveTheme);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Apply_normalizes_an_undefined_preference_to_system()
    {
        var manager = new ThemeManager(TempStore());

        manager.Apply((AppTheme)999, persist: false);

        Assert.Equal(AppTheme.System, manager.Preference);
    }

    [Fact]
    public void Apply_swaps_the_live_palette_brush()
    {
        var (light, dark) = RunOnSta(() =>
        {
            var app = new System.Windows.Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/SmartAnalysis.UI;component/DesignSystem/DesignSystem.xaml"),
            });

            var manager = new ThemeManager(TempStore());
            manager.Initialize(app);

            manager.Apply(AppTheme.Light, persist: false);
            var lightColor = ((SolidColorBrush)app.Resources["SA.Brush.Background.App"]).Color;

            manager.Apply(AppTheme.Dark, persist: false);
            var darkColor = ((SolidColorBrush)app.Resources["SA.Brush.Background.App"]).Color;

            return (lightColor, darkColor);
        });

        // The same brush key resolves to different colours per theme → DynamicResource consumers repaint.
        Assert.NotEqual(light, dark);
    }

    // Runs a function on a fresh STA thread (WPF Application requires STA); rethrows any failure.
    private static T RunOnSta<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }

        return result;
    }
}
