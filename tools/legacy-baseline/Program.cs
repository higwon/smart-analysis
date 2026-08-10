using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FW.Analysis.Calculate;

// TASK-MV00 — freeze the legacy numeric ground truth for the MVP operations.
// Usage: dotnet run --project tools/legacy-baseline -- <outputDir> [legacyRepoDir]
// Drives the clean legacy primitives (compiled by path via the csproj) on deterministic synthetic
// inputs and writes golden JSON + a manifest. Runs offline; the golden is committed and consumed by
// the new parity tests (T01/T02/A01/A02).

string outputDir = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "golden");
string legacyRepoDir = args.Length > 1
    ? args[1]
    : Environment.GetEnvironmentVariable("LEGACY_REPO_DIR")
      ?? @"C:\Users\HyuckJin.Kwon\SmartAnalysis-Private\SmartAnalysis-Private";

Directory.CreateDirectory(outputDir);

var json = new JsonSerializerOptions
{
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, // NaN/Infinity as named literals
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
};

const double Tolerance = 1e-9; // relative tolerance the new parity test (T02) asserts within

// ---------- Summary statistics (enables A02) ----------
var statCases = new List<StatCase>();
void Stat(string id, string cls, double[] input, string unit)
{
    var calc = new SummaryStatisticsCalculator(input);
    calc.Calculate();
    statCases.Add(new StatCase(
        id, cls, input, Sha256(input), unit, Tolerance,
        new StatOutputs(
            calc.Min, calc.Max, calc.MinMax, calc.Mid, calc.Average, calc.MeanAbsoluteError,
            calc.StandardDeviation, calc.Skewness, calc.Kurtosis, calc.BoundedPointAverageRoughness)));
}

Stat("ramp-16", "normal", Enumerable.Range(0, 16).Select(i => (double)i).ToArray(), "nm");
Stat("mixed-8", "normal", [1.5, -2.0, 3.25, 0.0, 4.5, -1.25, 2.0, 0.75], "nm");
Stat("constant-4", "edge", [5.0, 5.0, 5.0, 5.0], "nm");
Stat("empty", "edge", [], "nm");
Stat("with-nan", "edge", [1.0, 2.0, double.NaN, 4.0], "nm");
Stat("with-inf", "edge", [1.0, 2.0, double.PositiveInfinity, 4.0], "nm");

// ---------- 1D polynomial fit (Line/Whole flatten core; enables A01) ----------
var polyCases = new List<PolyCase>();
void Poly(string id, string cls, int order, double[] x, double[] y)
{
    var reg = new PolynomialLeastSquaresRegression(order);
    reg.Fit(x, y);
    polyCases.Add(new PolyCase(id, cls, order, x, y, Sha256(x, y), reg.Infer(x), Tolerance));
}

double[] xs = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
Poly("linear-order1", "normal", 1, xs, xs.Select(x => 2.0 * x + 1.0).ToArray());
Poly("quadratic-order2", "normal", 2, xs, xs.Select(x => 3.0 + 0.5 * x - 0.2 * x * x).ToArray());
Poly("mean-order0", "normal", 0, xs, [2, 4, 4, 4, 5, 5, 7, 9, 3, 1]); // order-0 fit == mean
Poly("noisy-line-order1", "normal", 1, xs, [1.1, 2.9, 5.2, 6.8, 9.1, 10.9, 13.2, 14.8, 17.1, 18.9]);

// ---------- 2D polynomial fit (Surface flatten core; enables A01) ----------
var multiCases = new List<MultiPolyCase>();
void Multi(string id, string cls, int order, double[] x1, double[] x2, double[] y)
{
    var reg = new MultiplePolynomialRegression(order);
    reg.Fit(x1, x2, y);
    multiCases.Add(new MultiPolyCase(id, cls, order, x1, x2, y, Sha256(x1, x2, y), reg.Infer(x1, x2), Tolerance));
}

// A 4x4 grid flattened into row-major (x1 = column, x2 = row).
var g1 = new List<double>();
var g2 = new List<double>();
var plane = new List<double>();
var curve = new List<double>();
for (int r = 0; r < 4; r++)
{
    for (int c = 0; c < 4; c++)
    {
        g1.Add(c);
        g2.Add(r);
        plane.Add(1.0 + 2.0 * c + 3.0 * r);
        curve.Add(2.0 + c - 0.5 * r + 0.25 * c * c + 0.1 * r * r);
    }
}

Multi("plane-order1", "normal", 1, [.. g1], [.. g2], [.. plane]);
Multi("surface-order2", "normal", 2, [.. g1], [.. g2], [.. curve]);

// ---------- Manifest ----------
var manifest = new GoldenManifest(
    Task: "MV00",
    GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
    Legacy: new LegacyInfo(legacyRepoDir, Git(legacyRepoDir, "rev-parse HEAD"), Git(legacyRepoDir, "rev-parse --abbrev-ref HEAD")),
    MathNet: "5.0.0",
    Notes: "Legacy numeric primitives from FW.Analysis.Calculate driven on synthetic inputs. "
         + "Full Whole/Line/Surface flatten orchestration is deferred (legacy orchestration is WPF/Dialogs-coupled); "
         + "these 1D/2D polynomial-fit goldens are the flatten math core A01 reuses.");

Write("manifest.json", manifest);
Write("summary-statistics.json", statCases);
Write("polynomial-fit-1d.json", polyCases);
Write("polynomial-fit-2d.json", multiCases);

Console.WriteLine($"Wrote golden data to {Path.GetFullPath(outputDir)}");
Console.WriteLine($"  legacy: {manifest.Legacy.Branch} @ {manifest.Legacy.Commit}");
Console.WriteLine($"  stats={statCases.Count} poly1d={polyCases.Count} poly2d={multiCases.Count}");
return 0;

void Write(string name, object value) => File.WriteAllText(Path.Combine(outputDir, name), JsonSerializer.Serialize(value, json));

static string Sha256(params double[][] arrays)
{
    using var ms = new MemoryStream();
    using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
    {
        foreach (var arr in arrays)
        {
            foreach (var v in arr)
            {
                w.Write(v); // little-endian double bits
            }

            w.Write(int.MinValue); // array separator so [a],[b] != [ab]
        }
    }

    return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
}

static string Git(string repoDir, string cmdArgs)
{
    try
    {
        var psi = new ProcessStartInfo("git", $"-C \"{repoDir}\" {cmdArgs}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi);
        if (p is null)
        {
            return "unknown";
        }

        string outText = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(5000);
        return string.IsNullOrEmpty(outText) ? "unknown" : outText;
    }
    catch
    {
        return "unknown";
    }
}

sealed record GoldenManifest(string Task, string GeneratedAtUtc, LegacyInfo Legacy, string MathNet, string Notes);
sealed record LegacyInfo(string RepoDir, string Commit, string Branch);

sealed record StatCase(string Id, string Class, double[] Input, string InputSha256, string Unit, double Tolerance, StatOutputs Outputs);
sealed record StatOutputs(
    double Min, double Max, double MinMax, double Mid, double Average, double MeanAbsoluteError,
    double StandardDeviation, double Skewness, double Kurtosis, double BoundedPointAverageRoughness);

sealed record PolyCase(string Id, string Class, int Order, double[] X, double[] Y, string InputSha256, double[] Fitted, double Tolerance);
sealed record MultiPolyCase(string Id, string Class, int Order, double[] X1, double[] X2, double[] Y, string InputSha256, double[] Fitted, double Tolerance);
