using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using Xunit;

namespace SmartAnalysis.Tests.Operations;

/// <summary>
/// The launcher maps an enum parameter's recorded integer code back to its member name, so provenance/history
/// reads "BandStop" rather than "kind 3". Non-enum parameters, unknown ops, and out-of-range codes yield null
/// (the caller then formats the number).
/// </summary>
public sealed class EnumParameterLabelTests
{
    private static IOperationLauncher NewLauncher()
    {
        var env = new SystemExecutionEnvironmentProvider();
        var registry = new OperationRegistry([new FourierFilterOperation(env), new RoughnessOperation(env)]);
        return new OperationLauncherUseCase(new Workspace(), registry, new MeasurementStore());
    }

    // The Fourier descriptor is version 1 (matches the recorded step version in these tests).
    [Fact]
    public void An_enum_parameter_code_maps_to_its_member_name_at_the_matching_version()
    {
        var launcher = NewLauncher();

        // FourierFilterKind: LowPass=0, HighPass=1, BandPass=2, BandStop=3.
        Assert.Equal("BandStop", launcher.EnumParameterLabel("image.fourier", 1, "kind", 3));
        Assert.Equal("LowPass", launcher.EnumParameterLabel("image.fourier", 1, "kind", 0));
    }

    [Fact]
    public void A_step_from_a_different_operation_version_is_not_relabelled()
    {
        var launcher = NewLauncher();

        // A newer op version may have reassigned the enum codes, so a past step must not adopt the current name.
        Assert.Null(launcher.EnumParameterLabel("image.fourier", 2, "kind", 3));
        Assert.Null(launcher.EnumParameterLabel("image.fourier", 0, "kind", 3));
    }

    [Fact]
    public void A_non_enum_parameter_yields_null()
    {
        var launcher = NewLauncher();

        Assert.Null(launcher.EnumParameterLabel("image.fourier", 1, "lowCutoff", 0.25)); // a Number, not an enum
    }

    [Fact]
    public void A_non_integer_or_non_finite_code_yields_null()
    {
        var launcher = NewLauncher();

        Assert.Null(launcher.EnumParameterLabel("image.fourier", 1, "kind", 3.4));            // corrupt fractional code
        Assert.Null(launcher.EnumParameterLabel("image.fourier", 1, "kind", double.NaN));
    }

    [Fact]
    public void An_unknown_operation_or_parameter_or_out_of_range_code_yields_null()
    {
        var launcher = NewLauncher();

        Assert.Null(launcher.EnumParameterLabel("image.nope", 1, "kind", 3));      // unknown op
        Assert.Null(launcher.EnumParameterLabel("image.fourier", 1, "nope", 3));   // unknown parameter
        Assert.Null(launcher.EnumParameterLabel("image.fourier", 1, "kind", 99));  // code outside the enum
        Assert.Null(launcher.EnumParameterLabel("image.roughness", 1, "kind", 0)); // parameterless op has no such field
    }
}
