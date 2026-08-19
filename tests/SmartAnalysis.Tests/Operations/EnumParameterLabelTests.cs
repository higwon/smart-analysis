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

    [Fact]
    public void An_enum_parameter_code_maps_to_its_member_name()
    {
        var launcher = NewLauncher();

        // FourierFilterKind: LowPass=0, HighPass=1, BandPass=2, BandStop=3.
        Assert.Equal("BandStop", launcher.EnumParameterLabel("image.fourier", "kind", 3));
        Assert.Equal("LowPass", launcher.EnumParameterLabel("image.fourier", "kind", 0));
    }

    [Fact]
    public void A_non_enum_parameter_yields_null()
    {
        var launcher = NewLauncher();

        Assert.Null(launcher.EnumParameterLabel("image.fourier", "lowCutoff", 0.25)); // a Number, not an enum
    }

    [Fact]
    public void An_unknown_operation_or_parameter_or_out_of_range_code_yields_null()
    {
        var launcher = NewLauncher();

        Assert.Null(launcher.EnumParameterLabel("image.nope", "kind", 3));      // unknown op
        Assert.Null(launcher.EnumParameterLabel("image.fourier", "nope", 3));   // unknown parameter
        Assert.Null(launcher.EnumParameterLabel("image.fourier", "kind", 99));  // code outside the enum
        Assert.Null(launcher.EnumParameterLabel("image.roughness", "kind", 0)); // parameterless op has no such field
    }
}
