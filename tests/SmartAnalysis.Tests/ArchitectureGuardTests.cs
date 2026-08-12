using System.Xml.Linq;
using Xunit;

namespace SmartAnalysis.Tests;

/// <summary>
/// TASK-F00 minimal Architecture Guard.
///
/// Validates the solution's <c>ProjectReference</c> graph at the project level only, by reading the
/// <c>.csproj</c> files from disk. It does NOT do type/namespace analysis and installs no extra
/// package (no NetArchTest) — the full type/namespace Architecture-Test matrix is TASK-F02.
///
/// The expected reference map is intentionally hard-coded (ADR-007/009/010). Failure messages name
/// exactly which project reference is wrong.
/// </summary>
public sealed class ArchitectureGuardTests
{
    // Expected ProjectReferences per project (product project names only). ADR-007/009/010.
    private static readonly IReadOnlyDictionary<string, string[]> Expected = new Dictionary<string, string[]>
    {
        ["SmartAnalysis.Domain"] = [],
        ["SmartAnalysis.Analysis"] = ["SmartAnalysis.Domain"],
        ["SmartAnalysis.Visualization"] = ["SmartAnalysis.Domain"],
        ["SmartAnalysis.Application"] = ["SmartAnalysis.Domain", "SmartAnalysis.Analysis", "SmartAnalysis.Visualization"],
        ["SmartAnalysis.Infrastructure"] = ["SmartAnalysis.Domain", "SmartAnalysis.Application"],
        ["SmartAnalysis.UI"] = ["SmartAnalysis.Application", "SmartAnalysis.Visualization"],
        ["SmartAnalysis.App"] = ["SmartAnalysis.UI", "SmartAnalysis.Application", "SmartAnalysis.Infrastructure"],
        // References the projects under test (Domain, Analysis, Application, Infrastructure, Visualization).
        ["SmartAnalysis.Tests"] = ["SmartAnalysis.Domain", "SmartAnalysis.Analysis", "SmartAnalysis.Application", "SmartAnalysis.Infrastructure", "SmartAnalysis.Visualization"],
        // WPF-dependent tests (net8.0-windows): references only the UI assembly it tests (e.g. ThemeManager).
        ["SmartAnalysis.UiTests"] = ["SmartAnalysis.UI"],
    };

    // Edges that must never exist (product project -> product project). ADR-009/010.
    private static readonly (string From, string To)[] ForbiddenEdges =
    [
        ("SmartAnalysis.Application", "SmartAnalysis.Infrastructure"),
        ("SmartAnalysis.UI", "SmartAnalysis.Infrastructure"),
        ("SmartAnalysis.Analysis", "SmartAnalysis.Infrastructure"),
        ("SmartAnalysis.Visualization", "SmartAnalysis.UI"),
        ("SmartAnalysis.Infrastructure", "SmartAnalysis.UI"),
    ];

    private static IReadOnlyDictionary<string, HashSet<string>> LoadGraph()
    {
        var root = FindRepoRoot();
        var graph = new Dictionary<string, HashSet<string>>();
        foreach (var dir in new[] { "src", "tests" })
        {
            var full = Path.Combine(root, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var csproj in Directory.EnumerateFiles(full, "*.csproj", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(csproj);
                var refs = XDocument.Load(csproj)
                    .Descendants("ProjectReference")
                    .Select(e => (string?)e.Attribute("Include"))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => Path.GetFileNameWithoutExtension(s!.Replace('\\', Path.DirectorySeparatorChar)))
                    .ToHashSet(StringComparer.Ordinal);
                graph[name] = refs;
            }
        }
        return graph;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate repo root (no SmartAnalysis.sln found walking up from the test output dir).");
        return dir!.FullName;
    }

    [Fact]
    public void Solution_has_exactly_the_nine_expected_projects()
    {
        var graph = LoadGraph();
        var actual = graph.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var expected = Expected.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.True(
            actual.SequenceEqual(expected, StringComparer.Ordinal),
            $"Expected exactly these {expected.Length} projects:\n  {string.Join("\n  ", expected)}\nbut found:\n  {string.Join("\n  ", actual)}");
    }

    [Fact]
    public void Each_project_references_exactly_its_allowed_projects()
    {
        var graph = LoadGraph();
        foreach (var (project, expectedRefs) in Expected)
        {
            Assert.True(graph.ContainsKey(project), $"Missing project '{project}'.");
            var actual = graph[project].OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var expected = expectedRefs.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.True(
                actual.SequenceEqual(expected, StringComparer.Ordinal),
                $"'{project}' must reference exactly [{string.Join(", ", expected)}] " +
                $"but references [{string.Join(", ", actual)}]. (ADR-007/009/010)");
        }
    }

    [Fact]
    public void No_forbidden_reference_exists()
    {
        var graph = LoadGraph();
        foreach (var (from, to) in ForbiddenEdges)
        {
            Assert.False(
                graph.TryGetValue(from, out var refs) && refs.Contains(to),
                $"Forbidden reference present: '{from}' -> '{to}'. (ADR-009/010)");
        }
    }

    [Fact]
    public void Only_App_references_Infrastructure()
    {
        var graph = LoadGraph();
        // The rule is about PRODUCT wiring: only the App composition root may reference Infrastructure.
        // The test project is exempt — it references Infrastructure to test the adapters (FF01).
        var offenders = graph
            .Where(kv => kv.Key != "SmartAnalysis.App" && kv.Key != "SmartAnalysis.Tests"
                && kv.Value.Contains("SmartAnalysis.Infrastructure"))
            .Select(kv => kv.Key)
            .ToArray();
        Assert.True(
            offenders.Length == 0,
            $"Only 'SmartAnalysis.App' (the composition root) may reference Infrastructure, " +
            $"but these also do: [{string.Join(", ", offenders)}]. (ADR-009/010)");
    }

    [Fact]
    public void Domain_references_no_other_product_project()
    {
        var graph = LoadGraph();
        Assert.True(
            graph.TryGetValue("SmartAnalysis.Domain", out var refs) && refs.Count == 0,
            $"'SmartAnalysis.Domain' must reference no other product project but references " +
            $"[{string.Join(", ", graph.GetValueOrDefault("SmartAnalysis.Domain") ?? [])}].");
    }

    [Fact]
    public void Project_reference_graph_is_acyclic()
    {
        var graph = LoadGraph();
        var visiting = new HashSet<string>();
        var done = new HashSet<string>();
        var stack = new List<string>();

        void Visit(string node)
        {
            if (done.Contains(node)) return;
            Assert.False(
                !visiting.Add(node),
                $"Circular ProjectReference detected: {string.Join(" -> ", stack.Append(node))}.");
            stack.Add(node);
            foreach (var next in graph.GetValueOrDefault(node) ?? [])
                if (graph.ContainsKey(next))
                    Visit(next);
            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(node);
            done.Add(node);
        }

        foreach (var node in graph.Keys)
            Visit(node);
    }
}
