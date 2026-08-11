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

    private static string DesignSystemDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate repo root (SmartAnalysis.sln).");
        return Path.Combine(dir!.FullName, "src", "SmartAnalysis.UI", "DesignSystem");
    }

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
