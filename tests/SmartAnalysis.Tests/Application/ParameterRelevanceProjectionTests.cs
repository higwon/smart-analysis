using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Spectroscopy;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using Xunit;

namespace SmartAnalysis.Tests.Application;

/// <summary>
/// TASK-U09, the seam: the Analysis schema states a rule in CLR values, the form holds UI primitives. If the
/// rule is not projected the same way the defaults and choices are, the two never compare equal and the field
/// is dead in every setting — with nothing failing anywhere to say so.
/// </summary>
public sealed class ParameterRelevanceProjectionTests
{
    private static IOperationLauncher Launcher()
    {
        var env = new SystemExecutionEnvironmentProvider();
        return new OperationLauncherUseCase(
            new Workspace(), new OperationRegistry([new VolumeImageOperation(env)]), new MeasurementStore());
    }

    [Fact]
    public void An_enum_rule_is_projected_to_the_member_names_the_choice_control_holds()
    {
        var form = Launcher().GetForm("force-volume.volume-image")!;
        var threshold = form.Fields.Single(f => f.Name == VolumeImageOperation.ThresholdParameter);
        var measure = form.Fields.Single(f => f.Name == VolumeImageOperation.MeasureParameter);

        Assert.NotNull(threshold.RelevantWhen);
        var rule = threshold.RelevantWhen!;
        Assert.Equal(VolumeImageOperation.MeasureParameter, rule.Parameter);

        // The values the rule names must be findable among the deciding field's own option values — that is the
        // only comparison the form can make.
        var choices = measure.Options.Select(o => o.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var value in rule.Values)
        {
            Assert.IsType<string>(value);
            Assert.Contains((string)value, choices);
        }

        Assert.Equal(
            new[] { nameof(VolumeMeasure.Stiffness), nameof(VolumeMeasure.Deformation) },
            rule.Values.Cast<string>());
    }

    [Fact]
    public void A_parameter_with_no_rule_projects_none()
    {
        var form = Launcher().GetForm("force-volume.volume-image")!;

        Assert.Null(form.Fields.Single(f => f.Name == VolumeImageOperation.MeasureParameter).RelevantWhen);
        Assert.Null(form.Fields.Single(f => f.Name == VolumeImageOperation.PhaseParameter).RelevantWhen);
    }
}
