using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SmartAnalysis.Tests.DesignSystem;

/// <summary>
/// Text/XML validation of the first-party design system (UIX03). Runs on net8.0 (no WPF) by reading the
/// DesignSystem XAML as files — it enforces the design-system contracts that a build alone cannot:
/// Light/Dark key parity, the no-hardcoded-values rule, and brush-reference integrity.
/// </summary>
public sealed class DesignSystemStyleTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate repo root (SmartAnalysis.sln).");
        return dir!.FullName;
    }

    private static string UiProjectDir() => Path.Combine(RepoRoot(), "src", "SmartAnalysis.UI");
    private static string DesignSystemDir() => Path.Combine(UiProjectDir(), "DesignSystem");

    private static string LightPath() => Path.Combine(DesignSystemDir(), "Palettes", "LightColors.xaml");
    private static string DarkPath() => Path.Combine(DesignSystemDir(), "Palettes", "DarkColors.xaml");

    private static HashSet<string> KeysOf(string xamlPath)
    {
        var doc = XDocument.Load(xamlPath);
        return doc.Descendants()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet();
    }

    [Fact]
    public void Light_and_dark_palettes_declare_identical_keys()
    {
        var light = KeysOf(LightPath());
        var dark = KeysOf(DarkPath());

        var onlyInLight = light.Except(dark).OrderBy(k => k).ToArray();
        var onlyInDark = dark.Except(light).OrderBy(k => k).ToArray();

        Assert.True(
            onlyInLight.Length == 0 && onlyInDark.Length == 0,
            $"Light/Dark palette keys must be identical (theme swap relies on it).\n" +
            $"Only in Light: {string.Join(", ", onlyInLight)}\n" +
            $"Only in Dark: {string.Join(", ", onlyInDark)}");

        // Sanity: the palette is non-trivial and pairs every Color with a Brush.
        Assert.Contains("SA.Brush.Text.Primary", light);
        Assert.Contains("SA.Brush.Accent.OnSurface", light);
        Assert.Contains("SA.Brush.Banner.Error.Foreground", light);
    }

    [Fact]
    public void Error_banner_light_foreground_is_the_AA_fixed_tone()
    {
        // doc 23 §2/§9: the Light Error-banner fg must be the darker #B91C1C (~5.30:1 on the tint),
        // never #DC2626 (~3.95:1, fails AA). Guards the regression the reviewer caught in UIX01.
        var light = XDocument.Load(LightPath());
        var fg = light.Descendants()
            .First(e => (string?)e.Attribute(X + "Key") == "SA.Color.Banner.Error.Foreground")
            .Value.Trim();

        Assert.Equal("#FFB91C1C", fg, ignoreCase: true);
    }

    [Fact]
    public void No_raw_hex_colors_outside_the_palette_files()
    {
        // The no-hardcoded-values rule: raw hex may appear ONLY in Palettes/. Tokens hold metrics (no hex);
        // Controls/Components/Adapters/entry consume SA.* tokens only. Scans attribute values + element
        // text (not comments) so documentation hex can't trip it.
        var hex = new Regex(@"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})\b");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(DesignSystemDir(), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/Palettes/"))
            {
                continue; // raw hex is defined here on purpose
            }

            var doc = XDocument.Load(file);
            foreach (var element in doc.Descendants())
            {
                foreach (var attr in element.Attributes())
                {
                    if (hex.IsMatch(attr.Value))
                    {
                        offenders.Add($"{Path.GetFileName(file)}: {attr.Name}=\"{attr.Value}\"");
                    }
                }

                if (element.HasElements == false && hex.IsMatch(element.Value))
                {
                    offenders.Add($"{Path.GetFileName(file)}: <{element.Name.LocalName}> text \"{element.Value.Trim()}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Raw hex colors are only allowed in Palettes/. Offenders:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Screen_xaml_uses_tokens_not_ad_hoc_metrics_or_hex()
    {
        // The no-hardcoded-values rule at the SCREEN/view level (doc 21 §6, the reviewer's split):
        // view XAML (everything in SmartAnalysis.UI OUTSIDE DesignSystem/) must not hard-code hex or the
        // design metrics FontSize/Margin/Padding/CornerRadius/BorderThickness — they use SA.* tokens.
        // ControlTemplate implementation geometry inside DesignSystem/ is intentionally exempt (that is
        // template mechanics, not screen design). No screens exist yet, so this actively guards U01/U02.
        var metricAttrs = new HashSet<string> { "FontSize", "Margin", "Padding", "CornerRadius", "BorderThickness" };
        var hex = new Regex(@"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})\b");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(UiProjectDir(), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/DesignSystem/"))
            {
                continue; // the design system itself defines the tokens / template geometry
            }

            var doc = XDocument.Load(file);
            foreach (var element in doc.Descendants())
            {
                foreach (var attr in element.Attributes())
                {
                    var value = attr.Value.Trim();
                    var isResourceRef = value.StartsWith('{'); // {StaticResource ...} / {DynamicResource ...}

                    if (metricAttrs.Contains(attr.Name.LocalName) && !isResourceRef && value != "0")
                    {
                        offenders.Add($"{Path.GetFileName(file)}: {attr.Name.LocalName}=\"{value}\" (use an SA.* token)");
                    }

                    if (hex.IsMatch(value))
                    {
                        offenders.Add($"{Path.GetFileName(file)}: {attr.Name.LocalName}=\"{value}\" (hex — use an SA.Brush.* token)");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Screen/view XAML must use design tokens, not ad-hoc metrics/hex. Offenders:\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("ComboBox")]
    [InlineData("ComboBoxItem")]
    [InlineData("TextBox")]
    [InlineData("CheckBox")]
    [InlineData("ListBox")]
    [InlineData("ScrollBar")]
    [InlineData("RadioButton")]
    [InlineData("Slider")]
    [InlineData("Expander")]
    [InlineData("TabControl")]
    [InlineData("MenuItem")]
    [InlineData("ContextMenu")]
    [InlineData("DataGrid")]
    [InlineData("DataGridColumnHeader")]
    [InlineData("DataGridCell")]
    public void Interactive_control_ships_a_full_control_template(string control)
    {
        // A setter-only style leaves the DEFAULT WPF template in place, which paints its own system-coloured
        // chrome (e.g. ComboBox's toggle button) and ignores the SA Background/Foreground — giving unreadable,
        // off-theme controls (esp. in Dark). Every interactive control the shell can render must carry a full
        // ControlTemplate. This guards the exact regression that shipped a setter-only ComboBox.
        var xaml = File.ReadAllText(Path.Combine(DesignSystemDir(), "Controls", "ControlStyles.xaml"));
        // A ControlTemplate targeting the type — with or without an x:Key (MenuItem uses keyed role templates).
        Assert.Matches($@"<ControlTemplate( x:Key=""[^""]*"")? TargetType=""\{{x:Type {control}\}}""", xaml);
    }

    [Theory]
    [InlineData("DataGrid.SelectAllCommand")]           // select-all corner button
    [InlineData("PART_LeftHeaderGripper")]              // column resize handles
    [InlineData("PART_RightHeaderGripper")]
    [InlineData("SortArrow")]                           // sort-direction glyph element
    [InlineData("Property=\"SortDirection\"")]          // …driven by the header's SortDirection
    [InlineData("DataGridRowHeader")]                   // row headers (HeadersVisibility=Row/All)
    [InlineData("Validation.HasError")]                 // cell validation visual
    [InlineData("CellsPanelHorizontalOffset")]          // row-header/corner alignment
    [InlineData("NonFrozenColumnsViewportHorizontalOffset")] // frozen-column-aware h-scrollbar
    public void DataGrid_template_preserves_advanced_features(string marker)
    {
        // Owning the DataGrid template must NOT drop WPF's built-in DataGrid features. This locks the
        // regression where the first template shipped without a sort glyph, select-all corner, row headers,
        // resize grippers, validation visual, or frozen-column support.
        var xaml = File.ReadAllText(Path.Combine(DesignSystemDir(), "Controls", "ControlStyles.xaml"));
        Assert.Contains(marker, xaml);
    }

    [Fact]
    public void Icon_geometries_are_present_and_non_empty()
    {
        // UIX04: every SA.Icon.* is a PathGeometry (Figures) or a GeometryGroup of PathGeometry, each with
        // non-empty Figures. Guards an accidental empty/malformed conversion and keeps the set from silently
        // shrinking. (Visual correctness is checked by rendering a sheet; the split-per-primitive invariant
        // lives in the converter, tools/icon-import.)
        var iconsPath = Path.Combine(DesignSystemDir(), "Icons", "Icons.xaml");
        Assert.True(File.Exists(iconsPath), "Icons.xaml is missing.");

        var doc = XDocument.Load(iconsPath);
        var iconKeys = doc.Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet();

        // Exact-set contract for the MVP icons: a dropped/renamed/extra icon fails, not just a size change.
        var expected = new HashSet<string>
        {
            "SA.Icon.Assistant", "SA.Icon.Check", "SA.Icon.ChevronDown", "SA.Icon.ChevronRight",
            "SA.Icon.Circle", "SA.Icon.Close", "SA.Icon.Colormap", "SA.Icon.Compare", "SA.Icon.Cursor",
            "SA.Icon.Dataset", "SA.Icon.Dot", "SA.Icon.Error", "SA.Icon.FolderOpen", "SA.Icon.Import",
            "SA.Icon.Parameters", "SA.Icon.Refresh", "SA.Icon.Save", "SA.Icon.Scalebar",
            "SA.Icon.Statistics", "SA.Icon.Theme", "SA.Icon.Warning", "SA.Icon.ZoomFit",
        };
        var missing = expected.Except(iconKeys).OrderBy(k => k).ToArray();
        var unexpected = iconKeys.Except(expected).OrderBy(k => k).ToArray();
        Assert.True(missing.Length == 0 && unexpected.Length == 0,
            $"MVP icon set changed.\nMissing: {string.Join(", ", missing)}\nUnexpected: {string.Join(", ", unexpected)}");

        var empties = doc.Descendants()
            .Where(e => e.Name.LocalName == "PathGeometry")
            .Where(e => string.IsNullOrWhiteSpace((string?)e.Attribute("Figures")))
            .Select(e => (string?)e.Parent?.Attribute(X + "Key") ?? "(inline)")
            .ToArray();
        Assert.True(empties.Length == 0, "PathGeometry with empty Figures: " + string.Join(", ", empties));
    }

    [Fact]
    public void Lucide_license_notice_is_retained()
    {
        // ISC (Lucide) + MIT (Feather-derived icons) notices must travel with the vendored icons — assert
        // the substantive permission text, not just the acronym, so a truncated/stubbed file fails.
        var license = Path.Combine(DesignSystemDir(), "Icons", "LUCIDE-LICENSE.txt");
        Assert.True(File.Exists(license), "Icons/LUCIDE-LICENSE.txt must be committed with the vendored icons.");
        var text = File.ReadAllText(license);
        Assert.Contains("ISC License", text);
        Assert.Contains("Permission to use, copy, modify", text); // ISC grant body
        Assert.Contains("MIT License", text);                     // Feather-derived icons
    }

    [Fact]
    public void Every_referenced_brush_token_exists_in_the_palette()
    {
        // Guards typos: each SA.Brush.* referenced by Controls/Components must be a real palette key.
        var brushRef = new Regex(@"SA\.Brush\.[A-Za-z0-9.]+");
        var defined = KeysOf(LightPath());
        var missing = new SortedSet<string>();

        foreach (var file in Directory.EnumerateFiles(DesignSystemDir(), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/Palettes/"))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match m in brushRef.Matches(text))
            {
                if (!defined.Contains(m.Value))
                {
                    missing.Add($"{m.Value} (in {Path.GetFileName(file)})");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "Referenced brush tokens with no palette definition:\n" + string.Join("\n", missing));
    }
}
