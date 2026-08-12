using System;
using System.Windows;

namespace SmartAnalysis.UI.DesignSystem.Theming;

/// <summary>
/// Owns the runtime Light/Dark appearance. It swaps a single color-palette <see cref="ResourceDictionary"/>
/// (Light/DarkColors — identical keys) inside the merged-dictionary tree, so every
/// <c>DynamicResource SA.Brush.*</c> consumer re-binds live. Tokens/control/component styles are loaded
/// once and never swapped. The AFM data colormap is not part of this — it is domain-owned and
/// theme-independent (ADR-008, doc 15).
/// </summary>
public sealed class ThemeManager
{
    private const string LightPaletteUri =
        "pack://application:,,,/SmartAnalysis.UI;component/DesignSystem/Palettes/LightColors.xaml";
    private const string DarkPaletteUri =
        "pack://application:,,,/SmartAnalysis.UI;component/DesignSystem/Palettes/DarkColors.xaml";

    private readonly ThemePreferenceStore _store;
    private System.Windows.Application? _app;

    /// <summary>Creates a manager, optionally with a custom preference store (tests).</summary>
    public ThemeManager(ThemePreferenceStore? store = null)
        => _store = store ?? new ThemePreferenceStore();

    /// <summary>The user's choice (may be <see cref="AppTheme.System"/>).</summary>
    public AppTheme Preference { get; private set; } = AppTheme.System;

    /// <summary>The concrete appearance currently applied (always <see cref="AppTheme.Light"/> or <see cref="AppTheme.Dark"/>).</summary>
    public AppTheme EffectiveTheme { get; private set; } = AppTheme.Light;

    /// <summary>Raised after the effective appearance changes.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>
    /// Wires the manager to the application and applies the persisted preference (System on first run).
    /// The design-system dictionaries are expected to be merged already (App.xaml → DesignSystem.xaml).
    /// </summary>
    public void Initialize(System.Windows.Application app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        Apply(_store.Load(), persist: false);
    }

    /// <summary>Sets the preference, resolves the effective appearance, swaps the palette, and persists.</summary>
    public void Apply(AppTheme preference, bool persist = true)
    {
        // Apply is public API: normalize any undefined value (e.g. an out-of-range cast) to System so
        // Preference never holds a value the reapply/compare logic can't reason about.
        if (!Enum.IsDefined(preference))
        {
            preference = AppTheme.System;
        }

        Preference = preference;
        var effective = ResolveEffective(preference);
        SwapPalette(effective);
        EffectiveTheme = effective;

        if (persist)
        {
            _store.Save(preference);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-evaluates the OS theme when following the system, swapping if it changed. U01 calls this on the
    /// window's <c>WM_SETTINGCHANGE</c>; live OS subscription needs an HWND, which exists only with a window.
    /// </summary>
    public void ReapplyIfFollowingSystem()
    {
        if (Preference != AppTheme.System)
        {
            return;
        }

        var effective = ResolveEffective(AppTheme.System);
        if (effective != EffectiveTheme)
        {
            SwapPalette(effective);
            EffectiveTheme = effective;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Maps a preference to a concrete appearance, reading the OS setting for <see cref="AppTheme.System"/>.</summary>
    public static AppTheme ResolveEffective(AppTheme preference) => preference switch
    {
        AppTheme.Light => AppTheme.Light,
        AppTheme.Dark => AppTheme.Dark,
        _ => SystemUsesLightTheme() ? AppTheme.Light : AppTheme.Dark,
    };

    private void SwapPalette(AppTheme effective)
    {
        if (_app is null)
        {
            return;
        }

        // Swap the palette at the TOP LEVEL of Application.Resources.MergedDictionaries — never the nested
        // design-time palette inside DesignSystem.xaml. Replacing a deeply-nested merged dictionary updates
        // the resource-tree lookup but does NOT reliably invalidate live DynamicResource consumers, so the
        // theme would change without a shown window repainting. Adding a top-level override (then replacing
        // it) is the mechanism WPF reliably propagates to every window; being merged last, it wins over the
        // nested design-time palette. See ThemeManagerTests.Apply_swaps_palette_at_the_top_level.
        var uri = new Uri(effective == AppTheme.Dark ? DarkPaletteUri : LightPaletteUri);
        var top = _app.Resources.MergedDictionaries;
        for (var i = 0; i < top.Count; i++)
        {
            if (IsPalette(top[i]))
            {
                top[i] = new ResourceDictionary { Source = uri };
                return;
            }
        }

        top.Add(new ResourceDictionary { Source = uri });
    }

    // A palette is the LightColors/DarkColors dictionary (lives under DesignSystem/Palettes/).
    private static bool IsPalette(ResourceDictionary dictionary)
        => dictionary.Source is { } source
           && source.OriginalString.Contains("/Palettes/", StringComparison.OrdinalIgnoreCase);

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // AppsUseLightTheme: 1 = light, 0 = dark. Absent/unreadable → default to light.
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return true;
        }
    }
}
