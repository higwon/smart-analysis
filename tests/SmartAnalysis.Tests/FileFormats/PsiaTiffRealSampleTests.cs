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
    public async Task Profile_samples_route_to_unsupported()
    {
        var root = SamplesRoot();
        if (root is null)
        {
            output.WriteLine("No samples directory available — skipping.");
            return;
        }

        var dir = Path.Combine(root, "Profile");
        if (!Directory.Exists(dir))
        {
            output.WriteLine("No Profile samples — skipping.");
            return;
        }

        var reader = new PsiaTiffReader(StandardUnits.CreateRegistry());
        foreach (var path in Directory.EnumerateFiles(dir, "*.tiff").OrderBy(p => p))
        {
            var result = await reader.ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);
            (result.Dataset as IDisposable)?.Dispose();

            output.WriteLine($"[Profile] {Path.GetFileName(path)} -> {(result.IsSuccess ? "OK" : result.Error?.Kind.ToString())}");
            Assert.False(result.IsSuccess, $"Profile/{Path.GetFileName(path)} should not read as a 2D image.");
            Assert.Equal(FileReadErrorKind.UnsupportedImageType, result.Error!.Kind);
        }
    }

    [Fact]
    public async Task Spectroscopy_samples_read_as_force_curves()
    {
        var root = SamplesRoot();
        if (root is null)
        {
            output.WriteLine("No samples directory available — skipping.");
            return;
        }

        var dir = Path.Combine(root, "Spectroscopy");
        if (!Directory.Exists(dir))
        {
            output.WriteLine("No Spectroscopy samples — skipping.");
            return;
        }

        var reader = new PsiaTiffReader(StandardUnits.CreateRegistry());
        int curves = 0;
        foreach (var path in Directory.EnumerateFiles(dir, "*.tiff").OrderBy(p => p))
        {
            var result = await reader.ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);
            using var dataset = result.Dataset as IDisposable;
            string name = Path.GetFileName(path);

            if (result.Dataset is ForceCurveDataset curve)
            {
                output.WriteLine($"[Spec] {name} -> curve of {curve.Length}: "
                    + $"{curve.SeparationChannel.DisplayName} [{curve.SeparationChannel.Unit.Symbol}] vs "
                    + $"{curve.ForceChannel.DisplayName} [{curve.ForceChannel.Unit.Symbol}]");

                Assert.True(curve.Length > 1, $"{name}: a curve needs more than one sample.");
                Assert.Equal(StandardUnits.Length, curve.SeparationChannel.Unit.Dimension);
                Assert.Equal(StandardUnits.Force, curve.ForceChannel.Unit.Dimension);

                var separation = curve.Separation.Span;
                var force = curve.Force.Span;
                for (int i = 0; i < curve.Length; i++)
                {
                    Assert.True(float.IsFinite(separation[i]), $"{name}: separation[{i}] is not finite.");
                    Assert.True(float.IsFinite(force[i]), $"{name}: force[{i}] is not finite.");
                }

                // A ramp, not noise: a force curve sweeps Z away from where it started and comes back, so the travel
                // has to be far larger than the step between neighbouring samples. Reading the payload with the wrong
                // stride mixes channels together and collapses exactly this.
                float min = separation[0], max = separation[0], biggestStep = 0;
                for (int i = 1; i < curve.Length; i++)
                {
                    min = Math.Min(min, separation[i]);
                    max = Math.Max(max, separation[i]);
                    biggestStep = Math.Max(biggestStep, Math.Abs(separation[i] - separation[i - 1]));
                }

                Assert.True(max - min > biggestStep * 10,
                    $"{name}: Z travel {max - min} is not a ramp — the largest single step is {biggestStep}.");
                curves++;
            }
            else if (result.IsSuccess)
            {
                // Some files under Spectroscopy/ are the companion 2D images captured alongside the spectra.
                output.WriteLine($"[Spec] {name} -> {result.Dataset!.GetType().Name} (companion image)");
                Assert.IsType<ScanImageDataset>(result.Dataset);
            }
            else
            {
                // A spectrum that is not force-versus-distance (an IR/PiFM wavenumber sweep) is a typed refusal.
                output.WriteLine($"[Spec] {name} -> {result.Error!.Kind}: {result.Error.Message}");
                Assert.Equal(FileReadErrorKind.UnsupportedImageType, result.Error.Kind);
            }
        }

        Assert.True(curves > 0, "Expected at least one spectroscopy sample to read as a force curve.");
    }

    [Fact]
    public async Task PiFM_samples_are_either_2d_images_or_typed_unsupported()
    {
        var root = SamplesRoot();
        if (root is null)
        {
            output.WriteLine("No samples directory available — skipping.");
            return;
        }

        var dir = Path.Combine(root, "PiFM");
        if (!Directory.Exists(dir))
        {
            output.WriteLine("No PiFM dir — skipping.");
            return;
        }

        var reader = new PsiaTiffReader(StandardUnits.CreateRegistry());
        foreach (var path in Directory.EnumerateFiles(dir, "*.tiff").OrderBy(p => p))
        {
            var result = await reader.ReadAsync(path, ScanReadOptions.Default, CancellationToken.None);

            // PiFM mixes 2D maps (read as images) and spectra (unsupported). Both are valid outcomes;
            // any OTHER failure (Io/Corrupt/Truncated/NotPsiaTiff) is a regression.
            if (result.IsSuccess)
            {
                using var _ = Assert.IsType<ScanImageDataset>(result.Dataset);
                output.WriteLine($"[PiFM] {Path.GetFileName(path)} -> image");
            }
            else
            {
                output.WriteLine($"[PiFM] {Path.GetFileName(path)} -> {result.Error!.Kind}");
                Assert.Equal(FileReadErrorKind.UnsupportedImageType, result.Error!.Kind);
            }
        }
    }
}
