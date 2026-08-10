using System.Text.Json;
using SmartAnalysis.Analysis.Flattening;
using Xunit;

namespace SmartAnalysis.Tests.Flattening;

/// <summary>
/// TASK-A01 parity: Flatten's polynomial fit primitives reproduce the legacy engine within tolerance,
/// verified against the MV00 golden (<c>polynomial-fit-1d/2d.json</c>) — the same MathNet routines the
/// golden was generated from. Runs in CI with no legacy engine.
/// </summary>
public sealed class PolynomialParityTests
{
    private static string GoldenDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate repo root.");
        return Path.Combine(dir!.FullName, "tools", "legacy-baseline", "golden");
    }

    private static JsonDocument Load(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), name)));

    private static double[] Arr(JsonElement c, string prop) =>
        c.GetProperty(prop).EnumerateArray().Select(e => e.GetDouble()).ToArray();

    [Fact]
    public void Fit1d_matches_the_legacy_golden()
    {
        using var doc = Load("polynomial-fit-1d.json");
        int cases = 0;
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            int order = c.GetProperty("Order").GetInt32();
            double[] x = Arr(c, "X");
            double[] y = Arr(c, "Y");
            double[] goldenFitted = Arr(c, "Fitted");
            double tol = c.GetProperty("Tolerance").GetDouble();

            double[] fitted = Polynomials.Infer1D(Polynomials.Fit1D(x, y, order), x);

            Assert.Equal(goldenFitted.Length, fitted.Length);
            for (int i = 0; i < fitted.Length; i++)
            {
                Assert.True(
                    Math.Abs(goldenFitted[i] - fitted[i]) <= tol * Math.Max(1.0, Math.Abs(goldenFitted[i])),
                    $"{c.GetProperty("Id").GetString()}[{i}]: {fitted[i]} vs golden {goldenFitted[i]}");
            }

            cases++;
        }

        Assert.True(cases >= 3);
    }

    [Fact]
    public void Fit2d_matches_the_legacy_golden()
    {
        using var doc = Load("polynomial-fit-2d.json");
        int cases = 0;
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            int order = c.GetProperty("Order").GetInt32();
            double[] x1 = Arr(c, "X1");
            double[] x2 = Arr(c, "X2");
            double[] y = Arr(c, "Y");
            double[] goldenFitted = Arr(c, "Fitted");
            double tol = c.GetProperty("Tolerance").GetDouble();

            var fit = new SurfacePolynomial(order);
            fit.Fit(x1, x2, y);
            double[] fitted = fit.Infer(x1, x2);

            Assert.Equal(goldenFitted.Length, fitted.Length);
            for (int i = 0; i < fitted.Length; i++)
            {
                Assert.True(
                    Math.Abs(goldenFitted[i] - fitted[i]) <= tol * Math.Max(1.0, Math.Abs(goldenFitted[i])),
                    $"{c.GetProperty("Id").GetString()}[{i}]: {fitted[i]} vs golden {goldenFitted[i]}");
            }

            cases++;
        }

        Assert.True(cases >= 2);
    }
}
