using System;
using System.IO;
using System.Text.Json;

namespace SmartAnalysis.UI.DesignSystem.Theming;

/// <summary>
/// Persists the UI-chrome theme preference (a small local settings file — NOT workspace/domain data,
/// so this stays inside the UI project and does not go through the workspace store). Best-effort:
/// read/write failures fall back to <see cref="AppTheme.System"/> and never throw into the UI.
/// </summary>
public sealed class ThemePreferenceStore
{
    private readonly string _path;

    /// <summary>Creates a store at the default per-user location (<c>%APPDATA%/SmartAnalysis/ui-settings.json</c>).</summary>
    public ThemePreferenceStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartAnalysis",
            "ui-settings.json"))
    {
    }

    /// <summary>Creates a store at an explicit path (used by tests).</summary>
    public ThemePreferenceStore(string path)
        => _path = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>Reads the saved preference, or <see cref="AppTheme.System"/> if none/unreadable.</summary>
    public AppTheme Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return AppTheme.System;
            }

            using var stream = File.OpenRead(_path);
            var dto = JsonSerializer.Deserialize<PreferenceDto>(stream);
            // Enum.TryParse also succeeds for numeric strings like "999" (→ an undefined enum value), so a
            // corrupt settings file could otherwise inject an out-of-range AppTheme. Require a DEFINED value.
            return dto is not null
                   && Enum.TryParse<AppTheme>(dto.Theme, ignoreCase: true, out var theme)
                   && Enum.IsDefined(theme)
                ? theme
                : AppTheme.System;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppTheme.System;
        }
    }

    /// <summary>Writes the preference; best-effort (a persistence failure must not disrupt the UI).</summary>
    public void Save(AppTheme theme)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var stream = File.Create(_path);
            JsonSerializer.Serialize(stream, new PreferenceDto(theme.ToString()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // UI-chrome preference is non-critical; ignore.
        }
    }

    private sealed record PreferenceDto(string Theme);
}
