using System.Linq;
using SmartAnalysis.Visualization.Colormaps;
using Xunit;

namespace SmartAnalysis.Tests.Visualization;

/// <summary>
/// The predefined colormap catalog (clean-room ports of the legacy procedural palettes). Verifies the LUT
/// shape and a few known endpoints so the ports stay faithful.
/// </summary>
public sealed class ColormapCatalogTests
{
    [Fact]
    public void The_catalog_lists_named_256_entry_colormaps_with_Gold_default()
    {
        Assert.NotEmpty(ColormapCatalog.All);
        Assert.Equal("Gold", ColormapCatalog.Default.Name);
        Assert.Equal(ColormapCatalog.All.Select(c => c.Name), ColormapCatalog.Names);

        foreach (var entry in ColormapCatalog.All)
        {
            Assert.Equal(Colormap.Size, entry.Map.Entries.Count);
        }
    }

    [Fact]
    public void ByName_is_case_insensitive_and_falls_back_to_the_default()
    {
        Assert.Same(ColormapCatalog.ByName("grayscale"), ColormapCatalog.ByName("Grayscale"));
        Assert.Same(ColormapCatalog.Default.Map, ColormapCatalog.ByName("no-such-map"));
    }

    [Fact]
    public void Grayscale_ramps_black_to_white()
    {
        var gray = ColormapCatalog.ByName("Grayscale");

        Assert.Equal(new Rgb(0, 0, 0), gray.Entries[0]);
        Assert.Equal(new Rgb(255, 255, 255), gray.Entries[255]);
        Assert.Equal(new Rgb(128, 128, 128), gray.Entries[128]);
    }

    [Fact]
    public void Gold_starts_black_and_ends_near_white_through_gold()
    {
        var gold = ColormapCatalog.ByName("Gold");

        // Legacy Gold: R=2i, G=2i-128, B=2i-256 (clamped). i=0 → black; i=255 → all clamped high.
        Assert.Equal(new Rgb(0, 0, 0), gold.Entries[0]);
        Assert.Equal(new Rgb(255, 255, 254), gold.Entries[255]);
        Assert.Equal(new Rgb(200, 72, 0), gold.Entries[100]); // 2·100=200, 200-128=72, 200-256<0→0
    }

    [Fact]
    public void Red_stays_pure_red_in_the_lower_half()
    {
        var red = ColormapCatalog.ByName("Red");

        Assert.Equal(new Rgb(0, 0, 0), red.Entries[0]);
        Assert.Equal(new Rgb(120, 0, 0), red.Entries[60]);   // below mid → G=B=0
        Assert.Equal(0, red.Entries[60].G);
        Assert.True(red.Entries[200].G > 0);                 // above mid → green/blue climb in
    }
}
