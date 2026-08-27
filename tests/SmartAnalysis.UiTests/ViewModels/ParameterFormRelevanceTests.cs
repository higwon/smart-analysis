using SmartAnalysis.Application.Operations;
using SmartAnalysis.UI.ViewModels;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.ViewModels;

/// <summary>
/// TASK-U09, the form's end: a field the current settings do not use must show that it does not, or the user
/// tunes a control that cannot change the result and has no way to tell.
/// </summary>
public sealed class ParameterFormRelevanceTests
{
    private const string Measure = "measure";
    private const string Threshold = "threshold";

    private static OperationForm Form()
        => new(
            "test.op", "Test", "summary", OperationCategory.Process,
            [
                new ParameterFieldDescriptor(
                    Measure, "Measure", ParameterFieldKind.Choice, "Peak", null, null,
                    [new ParameterFieldOption("Peak", "Peak"), new ParameterFieldOption("Slope", "Slope")],
                    null, ""),
                new ParameterFieldDescriptor(
                    Threshold, "Threshold", ParameterFieldKind.Number, 50.0, 0.0, 100.0, [], null, "",
                    new ParameterFieldRelevance(Measure, ["Slope"])),
            ],
            DerivesImage: true);

    private static ParameterFormViewModel NewForm()
        => new(new NullLauncher(), Form(), _ => { });

    private static ParameterFieldViewModel Field(ParameterFormViewModel form, string name)
        => form.Fields.Single(f => f.Name == name);

    [Fact]
    public void A_field_the_opening_settings_do_not_use_starts_inert()
    {
        // Evaluated at construction, not only on the first edit: a form opened and left alone would otherwise
        // present a live-looking control the operation is about to ignore.
        var form = NewForm();

        Assert.False(Field(form, Threshold).IsRelevant);
        Assert.True(Field(form, Measure).IsRelevant);
    }

    [Fact]
    public void Changing_the_deciding_field_wakes_the_one_that_depends_on_it()
    {
        var form = NewForm();

        Field(form, Measure).Value = "Slope";

        Assert.True(Field(form, Threshold).IsRelevant);

        Field(form, Measure).Value = "Peak";

        Assert.False(Field(form, Threshold).IsRelevant);
    }

    [Fact]
    public void An_inert_field_keeps_its_value_and_still_submits()
    {
        // Relevance is about what the result uses, not about what the form holds. Dropping the value would lose
        // the user's setting the moment they looked at another measure, and restore a default behind their back.
        var form = NewForm();

        Field(form, Threshold).Value = 30.0;
        Field(form, Measure).Value = "Peak";

        Assert.False(Field(form, Threshold).IsRelevant);
        Assert.Equal(30.0, form.Values[Threshold]);
    }

    [Fact]
    public void A_relevance_change_still_refreshes_the_preview()
    {
        // Turning a field on or off is itself a settings change: the picture the preview shows was computed with
        // the old measure, so it has to be recomputed like any other edit.
        var form = NewForm();
        int changes = 0;
        form.ParametersChanged += (_, _) => changes++;

        Field(form, Measure).Value = "Slope";

        Assert.Equal(1, changes);
    }

    private sealed class NullLauncher : IOperationLauncher
    {
        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() => Array.Empty<OperationLauncherItem>();

        public OperationForm? GetForm(string operationId) => null;

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromResult(OperationRunResult.Failed("not used"));
    }
}
