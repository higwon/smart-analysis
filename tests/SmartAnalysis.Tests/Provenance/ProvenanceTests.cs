using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Provenance;

public sealed class ProvenanceTests
{
    private static ProvenanceStep Step(int order = 0)
        => new(
            stepId: $"s{order}",
            inputDatasetId: DatasetId.New(),
            inputVersion: 1,
            operationId: "image.flatten",
            operationVersion: 1,
            order: order,
            environment: ExecutionEnvironment.Unknown,
            parameters: new Dictionary<string, PhysicalValue> { ["order"] = new(1, StandardUnits.One) });

    [Fact]
    public void Root_is_root_with_no_parent_or_steps()
    {
        Assert.True(ProvenanceRecord.Root.IsRoot);
        Assert.Null(ProvenanceRecord.Root.ParentId);
        Assert.Empty(ProvenanceRecord.Root.Steps);
    }

    [Fact]
    public void DerivedFrom_records_parent_and_steps()
    {
        var parent = DatasetId.New();
        var prov = ProvenanceRecord.DerivedFrom(parent, [Step()]);

        Assert.False(prov.IsRoot);
        Assert.Equal(parent, prov.ParentId);
        Assert.Single(prov.Steps);
    }

    [Fact]
    public void Append_is_immutable_and_ordered()
    {
        var prov = ProvenanceRecord.Root.Append(Step(0)).Append(Step(1));

        Assert.Empty(ProvenanceRecord.Root.Steps); // original unchanged
        Assert.Equal(2, prov.Steps.Count);
        Assert.Equal(0, prov.Steps[0].Order);
        Assert.Equal(1, prov.Steps[1].Order);
    }

    // --- ProvenanceStep ---

    [Fact]
    public void Step_captures_parameters_with_units()
    {
        var step = new ProvenanceStep(
            "s1", DatasetId.New(), 1, "image.flatten", 2, 0, ExecutionEnvironment.Unknown,
            parameters: new Dictionary<string, PhysicalValue> { ["threshold"] = new(5, StandardUnits.Nanometre) });

        Assert.Equal("image.flatten", step.OperationId);
        Assert.Equal(2, step.OperationVersion);
        Assert.Equal(5.0, step.Parameters["threshold"].Value);
        Assert.Equal("nm", step.Parameters["threshold"].Unit.Symbol);
    }

    [Fact]
    public void Step_parameters_are_defensively_copied_and_read_only()
    {
        var p = new Dictionary<string, PhysicalValue> { ["a"] = new(1, StandardUnits.One) };
        var step = new ProvenanceStep("s1", DatasetId.New(), 0, "op", 0, 0, ExecutionEnvironment.Unknown, parameters: p);

        p["b"] = new(2, StandardUnits.One); // must not leak in
        Assert.Single(step.Parameters);
        Assert.Throws<InvalidCastException>(() => _ = (Dictionary<string, PhysicalValue>)step.Parameters);
    }

    [Fact]
    public void Step_preserves_typed_warnings_and_errors()
    {
        var step = new ProvenanceStep(
            "s1", DatasetId.New(), 0, "op", 0, 0, ExecutionEnvironment.Unknown,
            warnings: [new OperationWarning("W1", "clipped")],
            errors: [new OperationError("E1", "diverged")]);

        Assert.Single(step.Warnings);
        Assert.Equal("clipped", step.Warnings[0].Message);
        Assert.Single(step.Errors);
        Assert.Equal("E1", step.Errors[0].Code);
    }

    [Fact]
    public void Step_carries_ai_and_ml_annotations()
    {
        var step = new ProvenanceStep(
            "s1", DatasetId.New(), 0, "op", 0, 0, ExecutionEnvironment.Unknown,
            ai: new AiInvolvement(AiProposed: true, ApprovedBy: "user", ApprovedAt: DateTimeOffset.UnixEpoch),
            model: new MlModelRef("ez-flatten", "1.2.0"));

        Assert.True(step.Ai!.AiProposed);
        Assert.Equal("user", step.Ai.ApprovedBy);
        Assert.Equal("ez-flatten", step.Model!.ModelId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Step_rejects_blank_ids(string bad)
    {
        Assert.Throws<ArgumentException>(() => new ProvenanceStep(bad, DatasetId.New(), 0, "op", 0, 0, ExecutionEnvironment.Unknown));
        Assert.Throws<ArgumentException>(() => new ProvenanceStep("s1", DatasetId.New(), 0, bad, 0, 0, ExecutionEnvironment.Unknown));
    }

    [Fact]
    public void Step_rejects_negative_versions_and_order()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProvenanceStep("s1", DatasetId.New(), -1, "op", 0, 0, ExecutionEnvironment.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProvenanceStep("s1", DatasetId.New(), 0, "op", -1, 0, ExecutionEnvironment.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProvenanceStep("s1", DatasetId.New(), 0, "op", 0, -1, ExecutionEnvironment.Unknown));
    }

    [Fact]
    public void Diagnostics_reject_blank_message()
    {
        Assert.Throws<ArgumentException>(() => new OperationWarning("W1", " "));
        Assert.Throws<ArgumentException>(() => new OperationError(" ", "msg"));
    }
}
