using Microsoft.Extensions.DependencyInjection;
using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Reference;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Operations;

/// <summary>
/// TASK-F04: the operation contract + registry, exercised end-to-end by the reference operation
/// (doc 13, ADR-003/005). Proves: explicit-DI registration + discovery, headless run with provenance,
/// duplicate-id rejection, unregistered-op safety, and typed validation — with no central switch.
/// </summary>
public sealed class OperationContractTests
{
    private static readonly IExecutionEnvironmentProvider Env = new FixedEnvironment();

    private sealed class FixedEnvironment : IExecutionEnvironmentProvider
    {
        public ExecutionEnvironment Capture() => ExecutionEnvironment.Unknown;
    }

    private sealed class ProgressCollector : IProgress<OperationProgress>
    {
        public List<double> Fractions { get; } = [];

        public void Report(OperationProgress value) => Fractions.Add(value.Fraction);
    }

    private static ScanImageDataset NewImage()
        => new(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Nanometre, 0.0, 1.0, 3),
            new Axis("Y", StandardUnits.Nanometre, 0.0, 1.0, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(3, 2),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    private static LineProfileDataset NewProfile()
        => new(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Nanometre, 0.0, 1.0, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    // --- Explicit-DI registration + discovery (ADR-005, no central switch) ---

    [Fact]
    public void Reference_operation_registers_via_explicit_di_and_is_discoverable()
    {
        using var provider = new ServiceCollection()
            .AddReferenceAnalysis()
            .AddOperationRegistry()
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<IOperationRegistry>();

        Assert.Contains(registry.All, d => d.Id == "reference.identity");
        Assert.True(registry.TryGet("reference.identity", out var op));
        Assert.IsType<IdentityMeasurementOperation>(op);
        Assert.Contains(registry.ApplicableTo(DataKind.ScanImage), d => d.Id == "reference.identity");
        Assert.DoesNotContain(registry.ApplicableTo(DataKind.Spectrum), d => d.Id == "reference.identity");
    }

    [Fact]
    public void Empty_registry_discovers_and_finds_nothing()
    {
        var registry = new OperationRegistry([]);

        Assert.Empty(registry.All);
        Assert.False(registry.TryGet("reference.identity", out var op));
        Assert.Null(op);
        Assert.Empty(registry.ApplicableTo(DataKind.ScanImage));
    }

    [Fact]
    public void Registry_rejects_duplicate_operation_ids()
    {
        var duplicate = new IAnalysisOperation[]
        {
            new IdentityMeasurementOperation(Env),
            new IdentityMeasurementOperation(Env), // same Descriptor.Id
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new OperationRegistry(duplicate));
        Assert.Contains("reference.identity", ex.Message);
    }

    [Fact]
    public void Registry_rejects_null_operations() =>
        Assert.Throws<ArgumentException>(() => new OperationRegistry([null!]));

    // --- Headless run + provenance (doc 13: "no result without a provenance step") ---

    [Fact]
    public async Task Reference_operation_runs_headless_and_emits_provenance()
    {
        var op = new IdentityMeasurementOperation(Env);
        using var image = NewImage();
        var input = new OperationInput(image);
        var progress = new ProgressCollector();

        var result = await op.RunAsync(input, ParameterSet.Empty, progress, CancellationToken.None);

        // Output shape: a measurement artifact, not a derived dataset.
        Assert.Null(result.DerivedDataset);
        Assert.NotNull(result.Artifact);
        Assert.Empty(result.Warnings);

        // Scalar payload.
        Assert.Equal(1.0, result.Artifact!.Scalars[IdentityMeasurementOperation.ConstantKey].Value);
        Assert.Equal(StandardUnits.One, result.Artifact.Scalars[IdentityMeasurementOperation.ConstantKey].Unit);

        // Provenance is read from the output object only (single source of truth — ADR-014):
        // derived from the input, carrying exactly one emitted step.
        Assert.False(result.Artifact.Provenance.IsRoot);
        Assert.Equal(image.Id, result.Artifact.Provenance.ParentId);
        Assert.Equal(image.Id, result.Artifact.SourceId);
        var step = Assert.Single(result.Artifact.Provenance.Steps);
        Assert.Equal("reference.identity", step.OperationId);
        Assert.Equal(1, step.OperationVersion);
        Assert.Equal(0, step.Order);
        Assert.Equal(image.Id, step.InputDatasetId);

        // Progress reported from start to finish.
        Assert.Equal(0.0, progress.Fractions[0]);
        Assert.Equal(1.0, progress.Fractions[^1]);
    }

    [Fact]
    public async Task Reference_operation_honors_cancellation()
    {
        var op = new IdentityMeasurementOperation(Env);
        using var image = NewImage();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => op.RunAsync(new OperationInput(image), ParameterSet.Empty, null, cts.Token));
    }

    // --- Typed validation (preconditions are values, not exceptions) ---

    [Fact]
    public void Validate_accepts_a_scan_image()
    {
        var op = new IdentityMeasurementOperation(Env);
        using var image = NewImage();

        Assert.True(op.Validate(new OperationInput(image), ParameterSet.Empty).IsValid);
    }

    [Fact]
    public void Validate_rejects_a_non_scan_image_primary()
    {
        var op = new IdentityMeasurementOperation(Env);
        using var profile = NewProfile();

        var result = op.Validate(new OperationInput(profile), ParameterSet.Empty);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Running_on_an_invalid_input_throws()
    {
        var op = new IdentityMeasurementOperation(Env);
        using var profile = NewProfile();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => op.RunAsync(new OperationInput(profile), ParameterSet.Empty, null, CancellationToken.None));
    }

    // --- Descriptor is self-describing and immutable (basis for menus + AI discovery) ---

    [Fact]
    public void Descriptor_is_well_formed()
    {
        var descriptor = new IdentityMeasurementOperation(Env).Descriptor;

        Assert.Equal("reference.identity", descriptor.Id);
        Assert.Equal(OutputKind.Artifact, descriptor.Output);
        Assert.True(descriptor.IsDeterministic);
        Assert.True(descriptor.Accepts(DataKind.ScanImage));
        Assert.False(descriptor.Accepts(DataKind.ForceCurve));
        Assert.Same(ParameterSchema.Empty, descriptor.Parameters);
        Assert.Contains("reference", descriptor.Tags);
    }

    // --- Operation contract types reference Domain only (no null tolerance for inputs) ---

    [Fact]
    public void OperationInput_rejects_null_primary() =>
        Assert.Throws<ArgumentNullException>(() => new OperationInput(null!));

    [Fact]
    public void OperationInput_rejects_null_secondary_element()
    {
        using var image = NewImage();
        Assert.Throws<ArgumentException>(() => new OperationInput(image, [null!]));
    }

    [Fact]
    public void ValidationResult_failure_requires_at_least_one_error() =>
        Assert.Throws<ArgumentException>(() => ValidationResult.Fail());

    // --- OperationProgress is a validated value (0..1, finite) ---

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void OperationProgress_rejects_out_of_range_fraction(double fraction) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationProgress(fraction));

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void OperationProgress_accepts_a_fraction_in_range(double fraction) =>
        Assert.Equal(fraction, new OperationProgress(fraction).Fraction);

    // --- Descriptor rejects an undefined DataKind; ApplicableTo rejects an undefined query ---

    [Fact]
    public void Descriptor_rejects_an_undefined_accepted_input() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationDescriptor(
            "x", 1, "name", "summary", [(DataKind)999], ParameterSchema.Empty, OutputKind.Artifact));

    [Fact]
    public void ApplicableTo_rejects_an_undefined_data_kind() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationRegistry([]).ApplicableTo((DataKind)999));

    [Fact]
    public void Descriptor_rejects_a_derivedKind_on_a_measurement() =>
        Assert.Throws<ArgumentException>(() => new OperationDescriptor(
            "x", 1, "name", "summary", [DataKind.ScanImage], ParameterSchema.Empty, OutputKind.Artifact,
            derivedKind: DataKind.ScanImage)); // a Measure derives nothing → declaring a kind is a contract lie

    [Fact]
    public void Descriptor_keeps_the_derivedKind_of_a_transform()
    {
        var descriptor = new OperationDescriptor(
            "x", 1, "name", "summary", [DataKind.ScanImage], ParameterSchema.Empty, OutputKind.DerivedDataset,
            derivedKind: DataKind.LineProfile);
        Assert.Equal(DataKind.LineProfile, descriptor.DerivedKind);
    }

    // --- ParameterDescriptor invariants: an inconsistent schema is unrepresentable ---

    [Fact]
    public void ParameterDescriptor_rejects_default_of_the_wrong_type() =>
        Assert.Throws<ArgumentException>(() => new ParameterDescriptor("order", typeof(int), defaultValue: "wrong"));

    [Fact]
    public void ParameterDescriptor_rejects_inverted_range() =>
        Assert.Throws<ArgumentException>(() => new ParameterDescriptor("threshold", typeof(double), min: 10, max: 1));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ParameterDescriptor_rejects_non_finite_range(double bad) =>
        Assert.Throws<ArgumentException>(() => new ParameterDescriptor("threshold", typeof(double), min: bad, max: bad));

    [Fact]
    public void ParameterDescriptor_rejects_default_outside_range() =>
        Assert.Throws<ArgumentException>(() => new ParameterDescriptor("order", typeof(int), defaultValue: 9, min: 0, max: 3));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ParameterDescriptor_rejects_non_finite_default(double bad) =>
        Assert.Throws<ArgumentException>(() => new ParameterDescriptor("threshold", typeof(double), defaultValue: bad, min: 0.0, max: 10.0));

    [Fact]
    public void ParameterDescriptor_rejects_non_finite_default_even_without_a_range() =>
        Assert.Throws<ArgumentException>(() => new ParameterDescriptor("threshold", typeof(double), defaultValue: double.NaN));

    [Fact]
    public void ParameterDescriptor_rejects_range_on_non_numeric_type() =>
        Assert.Throws<ArgumentException>(() => new ParameterDescriptor("name", typeof(string), min: 0, max: 1));

    [Fact]
    public void ParameterDescriptor_rejects_unit_on_non_numeric_type() =>
        Assert.Throws<ArgumentException>(() => new ParameterDescriptor("name", typeof(string), unit: StandardUnits.Nanometre));

    [Fact]
    public void ParameterDescriptor_accepts_a_valid_numeric_parameter_with_unit()
    {
        var d = new ParameterDescriptor("threshold", typeof(double), defaultValue: 2.0, min: 0.0, max: 10.0, unit: StandardUnits.Nanometre);

        Assert.Equal(2.0, d.Default);
        Assert.Equal(StandardUnits.Nanometre, d.Unit);
    }

    // --- ParameterSchema.Validate: the common check operations compose with ---

    private static ParameterSchema TwoParamSchema() => new(
    [
        new ParameterDescriptor("order", typeof(int), defaultValue: 1, min: 0, max: 3),   // optional (has default)
        new ParameterDescriptor("threshold", typeof(double), min: 0.0, max: 10.0),         // required (no default)
    ]);

    private static ParameterSet Set(params (string Name, object? Value)[] pairs)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (name, value) in pairs)
        {
            dict[name] = value;
        }

        return new ParameterSet(dict);
    }

    [Fact]
    public void Schema_validate_passes_when_required_present_and_in_range() =>
        Assert.True(TwoParamSchema().Validate(Set(("threshold", 5.0))).IsValid);

    [Fact]
    public void Schema_validate_flags_unknown_parameter() =>
        Assert.False(TwoParamSchema().Validate(Set(("threshold", 5.0), ("bogus", 1))).IsValid);

    [Fact]
    public void Schema_validate_flags_missing_required_parameter() =>
        Assert.False(TwoParamSchema().Validate(ParameterSet.Empty).IsValid);

    [Fact]
    public void Schema_validate_flags_wrong_type() =>
        Assert.False(TwoParamSchema().Validate(Set(("threshold", "not-a-double"))).IsValid);

    [Fact]
    public void Schema_validate_flags_out_of_range() =>
        Assert.False(TwoParamSchema().Validate(Set(("threshold", 99.0))).IsValid);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Schema_validate_flags_non_finite_value(double bad) =>
        Assert.False(TwoParamSchema().Validate(Set(("threshold", bad))).IsValid);

    [Fact]
    public void ParameterSet_rejects_blank_key() =>
        Assert.Throws<ArgumentException>(() => new ParameterSet(new Dictionary<string, object?> { ["  "] = 1 }));
}
