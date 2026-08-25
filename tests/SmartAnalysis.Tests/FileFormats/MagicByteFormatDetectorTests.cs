using System.Text;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Infrastructure.FileFormats;
using Xunit;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// TASK-FF05: a file is identified by its own bytes, with the name only as a fallback — and the caller is told which
/// of the two decided. The legacy product went by extension alone, so a renamed file was refused and a mislabelled one
/// was handed to the wrong parser.
/// </summary>
public sealed class MagicByteFormatDetectorTests
{
    private static readonly string RealTiff =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tiff", "cheese-15x15.tiff");

    private static MagicByteFormatDetector Detector() => new();

    private static string WriteTemp(string extension, params byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sa-ff05-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void A_real_tiff_is_identified_by_its_content()
    {
        var detection = Detector().Detect(RealTiff);

        Assert.Equal(ScanFileFormat.Tiff, detection.Format);
        Assert.True(detection.IsFromContent); // the bytes settled it, not the ".tiff" on the end
    }

    [Fact]
    public void A_tiff_with_no_extension_at_all_is_still_identified()
    {
        // The legacy failure: an extension-less file was simply refused. Its bytes say TIFF, so it is a TIFF.
        var path = WriteTemp(string.Empty, File.ReadAllBytes(RealTiff));

        var detection = Detector().Detect(path);

        Assert.Equal(ScanFileFormat.Tiff, detection.Format);
        Assert.True(detection.IsFromContent);
    }

    [Fact]
    public void A_tiff_named_as_something_else_is_still_identified()
    {
        var path = WriteTemp(".dat", File.ReadAllBytes(RealTiff));

        Assert.Equal(ScanFileFormat.Tiff, Detector().Detect(path).Format);
    }

    [Fact]
    public void A_file_merely_named_tiff_is_not_identified_as_one()
    {
        // The other half of the legacy bug: the name said TIFF, so the wrong parser got the file. The bytes rule.
        var path = WriteTemp(".tiff", Encoding.ASCII.GetBytes("this is a text file, not a scan"));

        var detection = Detector().Detect(path);

        // The bytes were readable and matched nothing, so that IS the answer. Falling back to the name here would
        // hand a text file to the TIFF parser — exactly the legacy behaviour this task removes.
        Assert.Equal(FormatDetection.Unknown, detection);
    }

    [Fact]
    public void Big_endian_tiff_is_recognised_too()
    {
        // MM\0* is as valid as II*\0 — the byte order marker IS the start of the format.
        var path = WriteTemp(".tif", 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08);

        var detection = Detector().Detect(path);

        Assert.Equal(ScanFileFormat.Tiff, detection.Format);
        Assert.True(detection.IsFromContent);
    }

    [Fact]
    public void Hdf5_is_recognised_by_its_signature()
    {
        var path = WriteTemp(".dat", 0x89, 0x48, 0x44, 0x46, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00);

        var detection = Detector().Detect(path);

        Assert.Equal(ScanFileFormat.Hdf5, detection.Format);
        Assert.True(detection.IsFromContent);
    }

    [Fact]
    public void Ps_ppt_is_recognised_by_its_maker_string()
    {
        var path = WriteTemp(".dat", Encoding.ASCII.GetBytes("PS-PPT/v1\n"));

        var detection = Detector().Detect(path);

        Assert.Equal(ScanFileFormat.PsPpt, detection.Format);
        Assert.True(detection.IsFromContent);
    }

    [Theory]
    [InlineData(".tif")]
    [InlineData(".h5")]
    [InlineData(".ps-ppt")]
    public void A_readable_file_whose_bytes_match_nothing_is_unknown_whatever_it_is_named(string extension)
    {
        var path = WriteTemp(extension, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07);

        // The contract: when the content CAN be examined, it decides — a matching extension does not rescue bytes
        // that say nothing.
        Assert.Equal(FormatDetection.Unknown, Detector().Detect(path));
    }

    [Fact]
    public void An_unrecognisable_file_with_an_unknown_extension_is_unknown()
    {
        var path = WriteTemp(".xyz", 0x00, 0x01, 0x02, 0x03);

        Assert.Equal(FormatDetection.Unknown, Detector().Detect(path));
    }

    [Theory]
    [InlineData(".tiff", ScanFileFormat.Tiff)]
    [InlineData(".TIFF", ScanFileFormat.Tiff)]   // the name check is case-insensitive
    [InlineData(".h5", ScanFileFormat.Hdf5)]
    [InlineData(".ps-ppt", ScanFileFormat.PsPpt)]  // the REAL extension: "128x128_329MB.ps-ppt"
    public void A_file_whose_content_cannot_be_examined_falls_back_to_its_name(string extension, ScanFileFormat expected)
    {
        // Missing (or locked): there are no bytes to judge, so the name is all that is left — and the caller is told
        // that is all it was, never that the file was identified.
        var path = Path.Combine(Path.GetTempPath(), $"sa-ff05-missing-{Guid.NewGuid():N}{extension}");

        var detection = Detector().Detect(path);

        Assert.Equal(expected, detection.Format);
        Assert.Equal(FormatEvidence.Extension, detection.Evidence);
        Assert.False(detection.IsFromContent);
    }

    [Fact]
    public void An_unreadable_real_ps_ppt_name_falls_back_correctly()
    {
        // The product's files are named "<something>.ps-ppt" (the legacy dialog filters on *.ps-ppt). Path.GetExtension
        // returns ".ps-ppt", so a table listing only ".psppt" would leave the fallback useless for the very format it
        // exists to cover.
        var path = Path.Combine(Path.GetTempPath(), $"128x128_329MB-{Guid.NewGuid():N}.ps-ppt");

        var detection = Detector().Detect(path);

        Assert.Equal(ScanFileFormat.PsPpt, detection.Format);
        Assert.Equal(FormatEvidence.Extension, detection.Evidence);
    }

    [Fact]
    public void A_powerpoint_file_is_never_guessed_to_be_a_scan()
    {
        // ".ppt" belongs to PowerPoint. Guessing a presentation is a PS-PPT scan would be a wrong answer dressed as
        // an identification — and the legacy dialog never used it either.
        var path = Path.Combine(Path.GetTempPath(), $"deck-{Guid.NewGuid():N}.ppt");

        Assert.Equal(FormatDetection.Unknown, Detector().Detect(path));
    }

    [Fact]
    public void A_missing_file_with_an_unknown_extension_is_unknown()
        => Assert.Equal(FormatDetection.Unknown, Detector().Detect(
            Path.Combine(Path.GetTempPath(), $"sa-ff05-missing-{Guid.NewGuid():N}.xyz")));

    [Fact]
    public void An_empty_file_is_not_identified_by_content()
    {
        var path = WriteTemp(".xyz");

        Assert.Equal(FormatDetection.Unknown, Detector().Detect(path));
    }

    [Fact]
    public void A_file_too_short_to_hold_a_signature_does_not_match_it_by_prefix()
    {
        // "II" alone is not a TIFF: a 2-byte file must not match a 4-byte signature on a prefix.
        var path = WriteTemp(".xyz", 0x49, 0x49);

        Assert.Equal(ScanFileFormat.Unknown, Detector().Detect(path).Format);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_path_is_unknown(string path)
        => Assert.Equal(FormatDetection.Unknown, Detector().Detect(path));
}
