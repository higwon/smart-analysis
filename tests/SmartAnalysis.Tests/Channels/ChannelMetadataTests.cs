using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Channels;

public sealed class ChannelMetadataTests
{
    [Fact]
    public void Channel_exposes_kind_unit_and_display_name()
    {
        var channel = new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre, "Height");

        Assert.Equal("height", channel.Key);
        Assert.Equal(ChannelKind.Topography, channel.Kind);
        Assert.Equal("nm", channel.Unit.Symbol);
        Assert.Equal("Height", channel.DisplayName);
    }

    [Fact]
    public void Channel_display_name_defaults_to_key()
    {
        var channel = new ChannelDescriptor("deflection", ChannelKind.Deflection, StandardUnits.Volt);
        Assert.Equal("deflection", channel.DisplayName);
    }

    [Fact]
    public void Channel_rejects_blank_key()
        => Assert.Throws<ArgumentException>(() => new ChannelDescriptor(" ", ChannelKind.Topography, StandardUnits.Nanometre));

    [Fact]
    public void Channel_rejects_null_unit()
        => Assert.Throws<ArgumentNullException>(() => new ChannelDescriptor("k", ChannelKind.Topography, null!));

    [Fact]
    public void Channel_rejects_undefined_kind()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ChannelDescriptor("k", (ChannelKind)999, StandardUnits.Nanometre));

    [Fact]
    public void Unknown_kind_is_explicit_not_guessed()
    {
        // No string.Contains guessing — a channel is Unknown only when explicitly declared so.
        var channel = new ChannelDescriptor("force-something", ChannelKind.Unknown, StandardUnits.Nanonewton);
        Assert.Equal(ChannelKind.Unknown, channel.Kind);
    }

    [Fact]
    public void Metadata_core_and_extended_bag()
    {
        var extra = new Dictionary<string, string> { ["ScanRate"] = "1.0 Hz" };
        var meta = new ScanMetadata("NX10", DateTimeOffset.UnixEpoch, extra);

        Assert.Equal("NX10", meta.InstrumentModel);
        Assert.Equal(DateTimeOffset.UnixEpoch, meta.AcquiredAt);
        Assert.Equal("1.0 Hz", meta.Extended["ScanRate"]);
    }

    [Fact]
    public void Metadata_extended_is_read_only_and_defensively_copied()
    {
        var extra = new Dictionary<string, string> { ["a"] = "1" };
        var meta = new ScanMetadata("NX10", DateTimeOffset.UnixEpoch, extra);

        extra["b"] = "2"; // must not leak in
        Assert.Single(meta.Extended);
        Assert.Throws<InvalidCastException>(() => _ = (Dictionary<string, string>)meta.Extended);
    }

    [Fact]
    public void Metadata_rejects_blank_instrument_model()
        => Assert.Throws<ArgumentException>(() => new ScanMetadata("", DateTimeOffset.UnixEpoch));

    [Fact]
    public void Unknown_metadata_is_available_for_derived_datasets()
    {
        Assert.Equal("unknown", ScanMetadata.Unknown.InstrumentModel);
        Assert.Empty(ScanMetadata.Unknown.Extended);
    }
}
