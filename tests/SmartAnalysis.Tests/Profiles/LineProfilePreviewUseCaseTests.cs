using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// V07 live preview use case: the split-view chart the shell draws while the line is dragged. It samples the
/// same effective line the operation would but returns only a render input — no workspace mutation, no
/// provenance. Degenerate/non-spatial cases return null so the chart clears.
/// </summary>
public sealed class LineProfilePreviewUseCaseTests
{
    private static ScanImageDataset RampImage(Unit? xUnit = null, Unit? yUnit = null)
    {
        const int w = 5, h = 5;
        var z = new float[w * h];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = i % w; // z = column index
        }

        return new ScanImageDataset(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", xUnit ?? StandardUnits.Micrometre, 0.0, 1.0, w),
            new Axis("Y", yUnit ?? StandardUnits.Micrometre, 0.0, 1.0, h),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(z, w, h),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }

    [Fact]
    public void Previews_a_curve_render_input_for_a_valid_line()
    {
        using var image = RampImage();
        ILineProfilePreview preview = new LineProfilePreviewUseCase();

        var input = preview.Preview(image, 0, 2, 4, 2, samples: 5);

        Assert.NotNull(input);
        var series = Assert.Single(input!.Series);
        Assert.Equal(5, series.Y.Length);
        Assert.Equal("um", input.X.Unit);              // arc length in the X length unit
        Assert.Equal(4.0, input.X.End, 6);             // 4 px × 1 µm
    }

    [Fact]
    public void Preview_uses_the_effective_clamped_line()
    {
        using var image = RampImage();
        ILineProfilePreview preview = new LineProfilePreviewUseCase();

        // Overhang endpoint (x1 = 30) clamps to 4 → same arc length as (0,2)→(4,2), never the raw 30.
        var input = preview.Preview(image, 0, 2, 30, 2, samples: 5);

        Assert.NotNull(input);
        Assert.Equal(4.0, input!.X.End, 6);
    }

    [Fact]
    public void A_degenerate_line_returns_null()
    {
        using var image = RampImage();
        ILineProfilePreview preview = new LineProfilePreviewUseCase();

        Assert.Null(preview.Preview(image, 2, 2, 2, 2, samples: 5));   // coincident endpoints
        Assert.Null(preview.Preview(image, 20, 2, 30, 2, samples: 5)); // both clamp to the same edge
    }

    [Fact]
    public void A_non_spatial_axis_returns_null()
    {
        using var image = RampImage(xUnit: StandardUnits.PerMetre); // frequency X → no metric arc length
        ILineProfilePreview preview = new LineProfilePreviewUseCase();

        Assert.Null(preview.Preview(image, 0, 2, 4, 2, samples: 5));
    }
}
