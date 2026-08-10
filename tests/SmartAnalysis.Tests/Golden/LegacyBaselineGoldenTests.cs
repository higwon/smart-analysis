using System.Text.Json;
using Xunit;

namespace SmartAnalysis.Tests.Golden;

/// <summary>
/// TASK-MV00 guard: validates the committed legacy-baseline golden data (structure + self-consistency)
/// <b>without</b> the legacy engine, so it runs in CI. The golden itself is produced offline by
/// <c>tools/legacy-baseline</c> against the real legacy primitives; these tests ensure it stays
/// well-formed and internally consistent, and are the seam the parity tests (T02/A01/A02) build on.
/// </summary>
public sealed class LegacyBaselineGoldenTests
{
    private static string GoldenDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate repo root (SmartAnalysis.sln).");
        return Path.Combine(dir!.FullName, "tools", "legacy-baseline", "golden");
    }

    private static JsonDocument Load(string name)
    {
        var path = Path.Combine(GoldenDir(), name);
        Assert.True(File.Exists(path), $"Missing golden file: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Manifest_records_the_legacy_commit_and_branch()
    {
        using var doc = Load("manifest.json");
        var root = doc.RootElement;

        Assert.Equal("MV00", root.GetProperty("Task").GetString());
        var legacy = root.GetProperty("Legacy");
        var commit = legacy.GetProperty("Commit").GetString();
        Assert.False(string.IsNullOrWhiteSpace(commit));
        Assert.NotEqual("unknown", commit); // the harness must have captured a real legacy commit
        Assert.Matches("^[0-9a-fA-F]{7,40}$", commit);
        Assert.False(string.IsNullOrWhiteSpace(legacy.GetProperty("Branch").GetString()));
    }

    [Fact]
    public void Summary_statistics_cases_are_well_formed_with_a_known_value()
    {
        using var doc = Load("summary-statistics.json");
        var cases = doc.RootElement.EnumerateArray().ToList();
        Assert.True(cases.Count >= 6);

        foreach (var c in cases)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("Id").GetString()));
            Assert.Matches("^(normal|edge)$", c.GetProperty("Class").GetString()!);
            Assert.Matches("^[0-9A-F]{64}$", c.GetProperty("InputSha256").GetString()!);
            Assert.True(c.GetProperty("Tolerance").GetDouble() > 0);
            Assert.True(c.GetProperty("Outputs").TryGetProperty("Average", out _));
        }

        // Spot-check a hand-verifiable case: ramp 0..15 → Average 7.5, Min 0, Max 15.
        var ramp = cases.Single(c => c.GetProperty("Id").GetString() == "ramp-16").GetProperty("Outputs");
        Assert.Equal(7.5, ramp.GetProperty("Average").GetDouble(), 9);
        Assert.Equal(0.0, ramp.GetProperty("Min").GetDouble(), 9);
        Assert.Equal(15.0, ramp.GetProperty("Max").GetDouble(), 9);
    }

    [Fact]
    public void Polynomial_1d_golden_reproduces_an_exact_line_within_tolerance()
    {
        using var doc = Load("polynomial-fit-1d.json");
        var cases = doc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(cases);

        // An exact line fit at order 1 must reproduce y (self-consistency of the frozen golden).
        var linear = cases.Single(c => c.GetProperty("Id").GetString() == "linear-order1");
        AssertFittedMatchesY(linear, 1e-6);

        foreach (var c in cases)
        {
            Assert.Equal(
                c.GetProperty("X").GetArrayLength(),
                c.GetProperty("Fitted").GetArrayLength());
        }
    }

    [Fact]
    public void Polynomial_2d_golden_reproduces_an_exact_plane_within_tolerance()
    {
        using var doc = Load("polynomial-fit-2d.json");
        var plane = doc.RootElement.EnumerateArray()
            .Single(c => c.GetProperty("Id").GetString() == "plane-order1");
        AssertFittedMatchesY(plane, 1e-6);
    }

    private static void AssertFittedMatchesY(JsonElement c, double tolerance)
    {
        var y = c.GetProperty("Y").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var fitted = c.GetProperty("Fitted").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        Assert.Equal(y.Length, fitted.Length);
        for (int i = 0; i < y.Length; i++)
        {
            Assert.True(Math.Abs(y[i] - fitted[i]) <= tolerance,
                $"fitted[{i}]={fitted[i]} vs y[{i}]={y[i]} exceeds {tolerance}");
        }
    }
}
