using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.FileFormats.Tiff;
using Xunit;
using Xunit.Abstractions;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// Env-gated real-file check (ADR-015): reads actual PSIA-TIFF samples if a samples directory is
/// available, else no-ops. Directory resolves from <c>SMARTANALYSIS_TIFF_SAMPLES_DIR</c> or the
/// default SmartAnalysis 2.0 install path. This validates the reader against real instrument files;
/// numeric legacy parity (golden values) is a separate MV00/T01 concern.
/// </summary>
public sealed class PsiaTiffRealSampleTests(ITestOutputHelper output)
{
    private static string? SamplesRoot()
    {
        var env = Environment.GetEnvironmentVariable("SMARTANALYSIS_TIFF_SAMPLES_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            return env;
        }

        const string def = @"C:\Park Systems\SmartAnalysis 2.0\Samples";
        return Directory.Exists(def) ? def : null;
    }

    [Fact]
    public async Task Reads_real_2d_image_samples()
    {
        var root = SamplesRoot();
        if (root is null)
        {
            output.WriteLine("No samples directory available — skipping.");
            return;
        }

        var imageDir = Path.Combine(root, "Image");
        if (!Directory.Exists(imageDir))
        {
            output.WriteLine($"No Image dir at {imageDir} — skipping.");
            return;
        }

        var reader = new PsiaTiffReader(StandardUnits.CreateRegistry());
        int ok = 0, fail = 0;

        foreach (var path in Directory.EnumerateFiles(imageDir, "*.tiff").OrderBy(p => p))
        {
            var result = await reader.ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);
            if (result.IsSuccess && result.Dataset is ScanImageDataset img)
            {
                using (img)
                {
                    var span = img.Data.Memory.Span;
                    output.WriteLine(
                        $"OK   {Path.GetFileName(path)}: {img.X.Count}x{img.Y.Count} " +
                        $"X.step={img.X.Step:G4}{img.X.Unit.Symbol} ch={img.Channel.Key}/{img.Channel.Kind}/{img.Channel.Unit.Symbol} " +
                        $"v0={span[0]:G6}");
                    ok++;
                }
            }
            else
            {
                output.WriteLine($"FAIL {Path.GetFileName(path)}: {result.Error?.Kind} — {result.Error?.Message}");
                fail++;
            }
        }

        output.WriteLine($"--- Image: {ok} ok, {fail} failed ---");
        Assert.True(ok > 0, "Expected at least one readable 2D image sample.");
        Assert.Equal(0, fail); // every file under Samples/Image is a 2D scan image and must read
    }

    [Fact]
    public async Task Reports_non_2d_samples_as_typed_results()
    {
        var root = SamplesRoot();
        if (root is null)
        {
            output.WriteLine("No samples directory available — skipping.");
            return;
        }

        var reader = new PsiaTiffReader(StandardUnits.CreateRegistry());
        foreach (var sub in new[] { "Profile", "Spectroscopy", "PiFM" })
        {
            var dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(dir, "*.tiff").OrderBy(p => p).Take(4))
            {
                var result = await reader.ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);
                string outcome = result.IsSuccess
                    ? $"OK {((result.Dataset as ScanImageDataset) is { } d ? $"{d.X.Count}x{d.Y.Count}" : "non-image")}"
                    : $"{result.Error?.Kind}";
                if (result.Dataset is IDisposable disp)
                {
                    disp.Dispose();
                }

                output.WriteLine($"[{sub}] {Path.GetFileName(path)} -> {outcome}");
            }
        }
    }
}
