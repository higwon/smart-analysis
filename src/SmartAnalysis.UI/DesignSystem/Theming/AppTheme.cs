namespace SmartAnalysis.UI.DesignSystem.Theming;

/// <summary>
/// The user's theme choice. <see cref="System"/> follows the OS app-theme setting; <see cref="Light"/>
/// and <see cref="Dark"/> pin an appearance regardless of the OS.
/// </summary>
public enum AppTheme
{
    /// <summary>Follow the Windows "app mode" setting (default on first run).</summary>
    System = 0,

    /// <summary>Always light.</summary>
    Light = 1,

    /// <summary>Always dark.</summary>
    Dark = 2,
}
