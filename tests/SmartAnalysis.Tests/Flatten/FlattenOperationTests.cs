using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Analysis.Flattening;
using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Flattening;

/// <summary>TASK-A01: the Flatten operation on the F04 contract (DI + run + derived dataset + provenance).</summary>
public sealed class FlattenOperationTests
{
    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => ExecutionEnvironment.Unknown;
    }

    private static ScanImageDataset Image(float[] pixels, int width, int height)
        => new(
            DatasetId.New(),
            new DataSource("psia-tiff", "f.tiff"),
            new Axis("X", StandardUnits.Micrometre, 0, 1, width),
            new Axis("Y", StandardUnits.Micrometre, 0, 1, height),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership(pixels, width, height),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    private static FlattenOperation NewOp() => new(new FixedEnvironment());

    private static ParameterSet Params(params (string, object?)[] kv)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in kv)
        {
            d[k] = v;
        }

        return new ParameterSet(d);
    }

    [Fact]
    public void Registers_via_image_module_and_is_discoverable()
    {
        using var provider = new ServiceCollection().AddImageAnalysis().AddOperationRegistry().BuildServiceProvider();
        var registry = provider.GetRequiredService<IOperationRegistry>();

        Assert.Contains(registry.ApplicableTo(DataKind.ScanImage), d => d.Id == "image.flatten");
        Assert.True(registry.TryGet("image.flatten", out var op));
        Assert.IsType<FlattenOperation>(op);
    }

    [Fact]
    public async Task Produces_a_flattened_derived_dataset_with_provenance()
    {
        const int w = 4, h = 3;
        var pixels = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                pixels[(y * w) + x] = (2f * x) + 5f; // pure tilt, shared per row
            }
        }

        using var image = Image(pixels, w, h);
        var parameters = Params((FlattenOperation.ScopeParameter, FlattenScope.Whole));

        var result = await NewOp().RunAsync(new OperationInput(image), parameters, null, CancellationToken.None);

        // Output shape: a derived dataset, not an artifact.
        Assert.Null(result.Artifact);
        using var derived = Assert.IsType<ScanImageDataset>(result.DerivedDataset);

        // Flattened to ~0; axes + channel/unit preserved.
        foreach (var v in derived.Data.Memory.Span.ToArray())
        {
            Assert.True(Math.Abs(v) < 1e-3f, $"expected ~0 but was {v}");
        }

        Assert.Equal(w, derived.X.Count);
        Assert.Equal("nm", derived.Channel.Unit.Symbol);
        Assert.Equal(DataSource.Derived.FormatId, derived.Source.FormatId);

        // Provenance: derived-from the input, single flatten step (single source of truth — ADR-014).
        Assert.Equal(image.Id, derived.Provenance.ParentId);
        var step = Assert.Single(derived.Provenance.Steps);
        Assert.Equal("image.flatten", step.OperationId);

        // The input image is untouched (the op owns only its new output buffer).
        Assert.Equal(5f, image.Data.Memory.Span[0]);
    }

    [Fact]
    public void Validate_rejects_an_out_of_range_order()
    {
        using var image = Image([1, 2, 3, 4], 2, 2);
        Assert.False(NewOp().Validate(new OperationInput(image), Params((FlattenOperation.OrderParameter, 9))).IsValid);
        Assert.True(NewOp().Validate(new OperationInput(image), Params((FlattenOperation.OrderParameter, 1))).IsValid);
    }
}
