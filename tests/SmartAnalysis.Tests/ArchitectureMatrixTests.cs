using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace SmartAnalysis.Tests;

/// <summary>
/// TASK-F02 type/namespace-level Architecture matrix (doc 11), formalizing what the F00
/// <see cref="ArchitectureGuardTests"/> checks only at the ProjectReference level. Uses NetArchTest to
/// inspect compiled type dependencies, so a forbidden dependency is caught even if it slipped past the
/// project graph. UI/App are not referenced by this net8.0 test project (UI is WPF/net8.0-windows) — their
/// direction is covered by the F00 project-reference guard.
/// </summary>
public sealed class ArchitectureMatrixTests
{
    // One representative type per product assembly (loads the assembly for NetArchTest).
    private static readonly Assembly Domain = typeof(SmartAnalysis.Domain.Provenance.ProvenanceRecord).Assembly;
    private static readonly Assembly Analysis = typeof(SmartAnalysis.Analysis.Operations.Image.FlattenOperation).Assembly;
    private static readonly Assembly Application = typeof(SmartAnalysis.Application.Workspaces.Workspace).Assembly;
    private static readonly Assembly Infrastructure = typeof(SmartAnalysis.Infrastructure.Persistence.Workspace.DirectoryWorkspaceStore).Assembly;
    private static readonly Assembly Visualization = typeof(SmartAnalysis.Visualization.Colormaps.Colormap).Assembly;

    private static void AssertNoDependency(Assembly assembly, string layerNamespace, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly)
            .That().ResideInNamespaceStartingWith(layerNamespace)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{layerNamespace} must not depend on [{string.Join(", ", forbidden)}]. Offending types: " +
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Domain_depends_on_no_other_product_layer()
        => AssertNoDependency(Domain, "SmartAnalysis.Domain",
            "SmartAnalysis.Analysis", "SmartAnalysis.Application", "SmartAnalysis.Infrastructure",
            "SmartAnalysis.Visualization", "SmartAnalysis.UI");

    [Fact]
    public void Analysis_depends_only_on_Domain()
        => AssertNoDependency(Analysis, "SmartAnalysis.Analysis",
            "SmartAnalysis.Application", "SmartAnalysis.Infrastructure", "SmartAnalysis.Visualization",
            "SmartAnalysis.UI");

    [Fact]
    public void Visualization_depends_only_on_Domain()
        => AssertNoDependency(Visualization, "SmartAnalysis.Visualization",
            "SmartAnalysis.Analysis", "SmartAnalysis.Application", "SmartAnalysis.Infrastructure",
            "SmartAnalysis.UI");

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_UI()
        => AssertNoDependency(Application, "SmartAnalysis.Application",
            "SmartAnalysis.Infrastructure", "SmartAnalysis.UI");

    [Fact]
    public void Infrastructure_does_not_depend_on_UI_Analysis_or_Visualization()
        => AssertNoDependency(Infrastructure, "SmartAnalysis.Infrastructure",
            "SmartAnalysis.UI", "SmartAnalysis.Analysis", "SmartAnalysis.Visualization");
}
