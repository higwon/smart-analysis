using System.Text.Json;
using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Parity;

/// <summary>
/// TASK-T02 parity (A29): the clean-room <see cref="AlsBaseline"/> reproduces the legacy ALS baseline within
/// tolerance, verified against the frozen MV00 golden (<c>tools/legacy-baseline/golden/als-baseline.json</c>).
/// Runs in CI with no legacy engine — the golden's recorded inputs are fed through the new code and compared to the
/// legacy outputs. This is the numeric contract behind "the new product computes what the old one did".
/// </summary>
public sealed class AlsBaselineParityTests
{
    private static string GoldenFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate repo root.");
        return Path.Combine(dir!.FullName, "tools", "legacy-baseline", "golden", "als-baseline.json");
    }

    [Fact]
    public void Matches_the_frozen_legacy_golden_for_every_case()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GoldenFile()));
        int cases = 0;

        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var id = c.GetProperty("Id").GetString()!;
            var y = c.GetProperty("Y").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            double lambda = c.GetProperty("Lambda").GetDouble();
            double p = c.GetProperty("P").GetDouble();
            int iterations = c.GetProperty("Iterations").GetInt32();
            double tol = c.GetProperty("Tolerance").GetDouble();
            var expected = c.GetProperty("Baseline").EnumerateArray().Select(v => v.GetDouble()).ToArray();

            if (y.Length < 3)
            {
                // Documented divergence (primitive level only): legacy returns the input unchanged for a profile too
                // short for a second-difference penalty; the clean-room primitive rejects it so a caller cannot get a
                // silently meaningless "baseline". The OPERATION (A29 profile.baseline) matches legacy behaviour — it
                // guards the length and leaves the profile unchanged with a "low-rank" warning (ProfileBaselineOperationTests).
                Assert.Throws<ArgumentException>(() => AlsBaseline.Compute(y.Select(v => (float)v).ToArray(), lambda, p, iterations));
                cases++;
                continue;
            }

            var actual = AlsBaseline.Compute(y.Select(v => (float)v).ToArray(), lambda, p, iterations);

            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                // The inputs round-trip through float (our datasets are float-backed), so compare on a tolerance
                // scaled to the value — the legacy engine is double throughout.
                double scale = Math.Max(1.0, Math.Abs(expected[i]));
                Assert.True(
                    Math.Abs(expected[i] - actual[i]) <= Math.Max(tol, 1e-4) * scale,
                    $"{id}[{i}]: legacy {expected[i]} vs new {actual[i]}");
            }

            cases++;
        }

        Assert.True(cases >= 6, $"Expected the golden to cover every recorded case; saw {cases}.");
    }
}
