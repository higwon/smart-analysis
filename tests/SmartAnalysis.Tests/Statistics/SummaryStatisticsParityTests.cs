using System.Globalization;
using System.Text.Json;
using SmartAnalysis.Analysis.Statistics;
using Xunit;

namespace SmartAnalysis.Tests.Statistics;

/// <summary>
/// TASK-A02 parity: the new <see cref="SummaryStatistics"/> reproduces the legacy engine within
/// tolerance, verified against the frozen MV00 golden (<c>tools/legacy-baseline/golden</c>). Runs in CI
/// with no legacy engine — it feeds the golden's recorded inputs through the new code and compares to
/// the golden outputs (NaN/Infinity-aware). The <c>empty</c> case is the one intentional divergence
/// (legacy sentinels vs NaN — ADR-016).
/// </summary>
public sealed class SummaryStatisticsParityTests
{
    private static string GoldenFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate repo root.");
        return Path.Combine(dir!.FullName, "tools", "legacy-baseline", "golden", "summary-statistics.json");
    }

    [Fact]
    public void Matches_the_frozen_legacy_golden_for_every_case()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GoldenFile()));
        int normalChecked = 0;

        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var input = c.GetProperty("Input").EnumerateArray().Select(ReadDouble).ToArray();
            double tol = c.GetProperty("Tolerance").GetDouble();
            var g = c.GetProperty("Outputs");
            var r = SummaryStatistics.Compute(input);

            if (input.Length == 0)
            {
                // Documented divergence (ADR-016): legacy sentinels vs our all-NaN.
                Assert.True(double.IsNaN(r.Min) && double.IsNaN(r.Max) && double.IsNaN(r.Mean));
                continue;
            }

            AssertClose(g, "Min", r.Min, tol);
            AssertClose(g, "Max", r.Max, tol);
            AssertClose(g, "MinMax", r.PeakToPeak, tol);
            AssertClose(g, "Mid", r.Mid, tol);
            AssertClose(g, "Average", r.Mean, tol);
            AssertClose(g, "MeanAbsoluteError", r.MeanAbsoluteDeviation, tol);
            AssertClose(g, "StandardDeviation", r.Rms, tol);
            AssertClose(g, "Skewness", r.Skewness, tol);
            AssertClose(g, "Kurtosis", r.Kurtosis, tol);
            AssertClose(g, "BoundedPointAverageRoughness", r.BoundedPointAverageRoughness, tol);
            normalChecked++;
        }

        Assert.True(normalChecked >= 2, "Expected the golden to contain non-empty statistics cases.");
    }

    private static void AssertClose(JsonElement outputs, string key, double actual, double tol)
    {
        double expected = ReadDouble(outputs.GetProperty(key));
        if (double.IsNaN(expected))
        {
            Assert.True(double.IsNaN(actual), $"{key}: expected NaN but was {actual}");
            return;
        }

        if (double.IsInfinity(expected))
        {
            Assert.Equal(expected, actual); // exact, including sign
            return;
        }

        double allowed = tol * Math.Max(1.0, Math.Abs(expected));
        Assert.True(Math.Abs(expected - actual) <= allowed, $"{key}: {actual} vs golden {expected} (tol {allowed})");
    }

    private static double ReadDouble(JsonElement e) => e.ValueKind == JsonValueKind.String
        ? e.GetString() switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            var s => double.Parse(s!, CultureInfo.InvariantCulture),
        }
        : e.GetDouble();
}
