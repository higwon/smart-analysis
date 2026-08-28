using SmartAnalysis.Analysis.Operations;
using Xunit;

namespace SmartAnalysis.Tests.Operations;

/// <summary>
/// TASK-U10: some parameters are only used for some settings of another. A form that offers every control at
/// once puts inert ones in front of the user — they change it, nothing happens, and nothing says whether the
/// setting was ignored or the data simply did not respond.
/// </summary>
public sealed class ParameterRelevanceTests
{
    public enum Measure
    {
        Peak,
        Slope,
        Travel,
    }

    private static ParameterSchema Schema()
        => new(
        [
            new ParameterDescriptor("measure", typeof(Measure), defaultValue: Measure.Peak),
            new ParameterDescriptor(
                "threshold", typeof(double), defaultValue: 50.0, min: 0.0, max: 100.0,
                relevantWhen: new ParameterRelevance("measure", [Measure.Slope, Measure.Travel])),
        ]);

    private static ParameterSet Values(params (string Name, object? Value)[] pairs)
        => new(pairs.ToDictionary(p => p.Name, p => p.Value));

    [Fact]
    public void A_parameter_with_no_rule_is_always_used()
        => Assert.True(Schema().IsRelevant("measure", Values()));

    [Theory]
    [InlineData(Measure.Slope, true)]
    [InlineData(Measure.Travel, true)]
    [InlineData(Measure.Peak, false)]
    public void A_rule_follows_the_setting_it_names(Measure measure, bool expected)
        => Assert.Equal(expected, Schema().IsRelevant("threshold", Values(("measure", measure))));

    [Fact]
    public void An_absent_deciding_value_is_read_as_its_own_default()
    {
        // The run will use the default, so relevance has to answer for the same setting the run will see.
        Assert.False(Schema().IsRelevant("threshold", Values()));

        // The discriminating case: a schema whose DEFAULT is one of the relevant settings. Treating an absent
        // value as "nothing selected" would call this irrelevant and open the form with the field already dead,
        // even though the run about to happen uses it.
        var relevantByDefault = new ParameterSchema(
        [
            new ParameterDescriptor("measure", typeof(Measure), defaultValue: Measure.Slope),
            new ParameterDescriptor(
                "threshold", typeof(double), defaultValue: 50.0,
                relevantWhen: new ParameterRelevance("measure", [Measure.Slope])),
        ]);

        Assert.True(relevantByDefault.IsRelevant("threshold", Values()));
    }

    [Fact]
    public void An_unknown_parameter_is_not_claimed_to_be_irrelevant()
    {
        // Saying "irrelevant" about a name the schema does not have would silently disable a field over a typo.
        Assert.True(Schema().IsRelevant("nonesuch", Values()));
    }

    [Fact]
    public void A_rule_naming_a_parameter_the_schema_does_not_declare_is_refused()
    {
        // At construction, where the schema is written — not at render time, where it would show as a control
        // that is permanently disabled for no visible reason.
        var ex = Assert.Throws<ArgumentException>(() => new ParameterSchema(
        [
            new ParameterDescriptor(
                "threshold", typeof(double), defaultValue: 50.0,
                relevantWhen: new ParameterRelevance("measure", [Measure.Slope])),
        ]));

        Assert.Contains("measure", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rule_whose_values_are_the_wrong_type_is_refused()
    {
        // "Slope" the string never equals Measure.Slope, so the field would be dead in every setting.
        Assert.Throws<ArgumentException>(() => new ParameterSchema(
        [
            new ParameterDescriptor("measure", typeof(Measure), defaultValue: Measure.Peak),
            new ParameterDescriptor(
                "threshold", typeof(double), defaultValue: 50.0,
                relevantWhen: new ParameterRelevance("measure", ["Slope"])),
        ]));
    }

    [Fact]
    public void A_parameter_cannot_depend_on_itself()
        => Assert.Throws<ArgumentException>(() => new ParameterSchema(
        [
            new ParameterDescriptor(
                "threshold", typeof(double), defaultValue: 50.0,
                relevantWhen: new ParameterRelevance("threshold", [1.0])),
        ]));

    [Fact]
    public void A_rule_with_no_values_is_refused()
    {
        // It would make the parameter permanently irrelevant, which is a parameter that should not exist.
        Assert.Throws<ArgumentException>(() => new ParameterRelevance("measure", []));
    }

    [Fact]
    public void An_irrelevant_parameter_still_validates()
    {
        // Relevance is about what the result uses, not about what is allowed. A value that is out of range is
        // still an error even when the current settings would ignore it — the setting can change.
        var schema = Schema();

        Assert.True(schema.Validate(Values(("measure", Measure.Peak), ("threshold", 50.0))).IsValid);
        Assert.False(schema.Validate(Values(("measure", Measure.Peak), ("threshold", 500.0))).IsValid);
    }
}
