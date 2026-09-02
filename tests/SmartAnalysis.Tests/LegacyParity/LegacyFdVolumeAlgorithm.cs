using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Spectroscopy;

namespace SmartAnalysis.Tests.LegacyParity;

/// <summary>
/// The legacy engine's force-volume measures, transcribed from its source.
/// <para>
/// The legacy calculators cannot be compiled by path the way the MV00 goldens are: the arithmetic sits in
/// <c>FDSpectroscopyCalculator</c>, but the decisions that feed it — which channel, which half of the curve,
/// which defaults — live in <c>SpectroscopyAnalysisModel</c> (FW.UI.Common) and pull in DevExpress, SciChart
/// and WPF. This is therefore a <b>reading</b> of that code rather than a run of it, so every method names the
/// legacy method it came from and a reviewer can check it against the original.
/// </para>
/// <para>
/// It is not taken on trust: <see cref="LegacyFdVolumeParityTests"/> holds it against the numbers the legacy
/// application actually exported for the committed fixture, and it reproduces all three maps at all 64 points
/// to the precision that export prints.
/// </para>
/// </summary>
internal static class LegacyFdVolumeAlgorithm
{
    /// <summary>
    /// The names <c>SpectroscopyLineModel.GetIsZDetector</c> accepts. <b>"separation" is in this list</b>, and
    /// that matters: the file carries both a <c>Z Height</c> and a <c>Separation</c> channel, both match, and
    /// the caller uses <c>LastOrDefault</c> — so the abscissa is whichever the file declares <b>later</b>.
    /// <para>
    /// The near-identical list in <c>LIB.File.Tiff/Spectroscopy/SpectroscopyLine.cs</c> omits "separation" and
    /// is the wrong one to read: the analysis model works on <c>SpectroscopyLineModel</c>. Reading that other
    /// list puts <c>Z Height</c> on the abscissa, which is a plausible-looking answer that gets stiffness and
    /// deformation wrong by a factor of about four in opposite directions.
    /// </para>
    /// </summary>
    private static readonly string[] ZDetectorNames = ["z detector", "height", "z height", "zheight", "separation"];

    /// <summary>
    /// Legacy's default force threshold for both stiffness and deformation.
    /// <c>SpectroscopyFDViewModel.Initialize()</c> sets <c>DeformationThreshold = 0</c>, and
    /// <c>SpectroscopyImageViewModel.CalculateVolumeImageValue</c> passes the model's thresholds straight
    /// through. Zero makes the target force zero, so the window edge is the curve's own zero crossing.
    /// </summary>
    public const double DefaultThreshold = 0.0;

    /// <summary>
    /// Legacy's default baseline offset. <c>SpectroscopyFDViewModel.Initialize()</c> sets
    /// <c>IsCheckedApplyBaseLine = false</c>, and the model then receives <c>FD_OffsetBaseLineThreshold = 0</c>
    /// — which makes <c>GetBaseLineOffsetValue</c> average zero samples and return zero. No correction at all.
    /// </summary>
    public const double DefaultOffsetThreshold = 0.0;

    /// <summary>
    /// The abscissa legacy measures against: <c>LastOrDefault(t =&gt; t.GetIsZDetector())</c> over the file's
    /// channels, in declaration order.
    /// </summary>
    public static int AbscissaIndex(SpectroscopyChannelSet channels)
    {
        int found = -1;
        for (int c = 0; c < channels.ChannelCount; c++)
        {
            if (ZDetectorNames.Contains(channels.Channels[c].DisplayName.Trim().ToLowerInvariant()))
            {
                found = c;
            }
        }

        return found;
    }

    /// <summary>
    /// The approach half of a curve. <c>SpectroscopyDataService.GetTraceData</c> only consults the
    /// approach/retract classifier when <c>OpenFileType == PS_PPT</c>; for a plain TIFF it takes the else
    /// branch and returns <c>channelData[0 .. length/2)</c> verbatim. The segmentation modes and their
    /// parameters are PinPoint-only and do not apply to this fixture.
    /// </summary>
    public static (double[] Force, double[] Separation) Trace(
        ForceVolumeDataset map, SpectroscopyChannelSet channels, int point)
    {
        int half = map.SampleCount / 2;
        var f = map.ForceAt(point).Span;
        var s = channels.At(AbscissaIndex(channels), point).Span;
        var force = new double[half];
        var separation = new double[half];
        for (int i = 0; i < half; i++)
        {
            force[i] = f[i];
            separation[i] = s[i];
        }

        return (force, separation);
    }

    /// <summary>The retract half — <c>GetRetraceData</c>'s else branch, <c>channelData[length/2 .. length)</c>.</summary>
    public static double[] RetraceForce(ForceVolumeDataset map, int point)
    {
        int half = map.SampleCount / 2;
        var f = map.ForceAt(point).Span;
        var force = new double[map.SampleCount - half];
        for (int i = 0; i < force.Length; i++)
        {
            force[i] = f[half + i];
        }

        return force;
    }

    /// <summary>
    /// <c>SpectroscopyAnalysisModel.GetBaseLineOffsetValue</c>: pairs are sorted by separation <b>descending</b>
    /// — the far end, out of contact — and the first <c>threshold%</c> of them have their force averaged. A
    /// threshold of zero takes zero samples and yields no offset, which is the shipped default.
    /// </summary>
    public static double BaselineOffset(double[] force, double[] separation, double offsetThreshold)
    {
        var xs = separation.Where(v => !double.IsNaN(v));
        var ys = force.Where(v => !double.IsNaN(v));
        var sorted = xs.Zip(ys, (x, y) => new { X = x, Y = y }).OrderByDescending(p => p.X).ToArray();

        int count = (int)(sorted.Length * (offsetThreshold / 100.0));
        return count > 0 ? sorted.Take(count).Average(p => p.Y) : 0.0;
    }

    /// <summary>
    /// <c>FDSpectroscopyCalculator.FindNearestDistance</c>, including its quirks: the two arrays are stripped
    /// of NaN <b>independently</b> before being zipped, the pairs are sorted by separation ascending, and the
    /// crossing is the last sample at or above the target — <b>not</b> interpolated. A curve that never drops
    /// below the target yields its largest separation.
    /// </summary>
    public static double FindNearestDistance(double[] separation, double[] force, double target)
    {
        var validForces = force.Where(v => !double.IsNaN(v)).ToList();
        var validSeparations = separation.Where(v => !double.IsNaN(v)).ToList();
        var paired = validSeparations.Zip(validForces, (x, y) => new { X = x, Y = y }).OrderBy(p => p.X).ToArray();

        int targetIndex = paired.Length - 1;
        for (int i = 1; i < paired.Length; i++)
        {
            if (paired[i].Y < target && paired[i - 1].Y >= target)
            {
                targetIndex = i - 1;
                break;
            }
        }

        return paired[targetIndex].X;
    }

    /// <summary>
    /// <c>FDSpectroscopyCalculator.CalculateStiffness</c> in N/m, given force in nN and separation in µm.
    /// The peak is a raw <c>Max()</c> over the trace — legacy applies no baseline correction here beyond the
    /// offset already subtracted, and the shipped default subtracts nothing.
    /// </summary>
    public static double Stiffness(double[] forceNanonewtons, double[] separationMicrometres, double threshold)
    {
        double[] forceN = forceNanonewtons.Select(v => v * 1e-9).ToArray();
        double[] separationM = separationMicrometres.Select(v => v * 1e-6).ToArray();

        double maxForce = forceN.Max();
        int maxForceIndex = Array.IndexOf(forceN, maxForce);
        double targetForce = maxForce * threshold / 100;
        double targetSeparation = FindNearestDistance(separationM, forceN, targetForce);

        double deltaForce = maxForce - targetForce;
        double deltaZ = separationM[maxForceIndex] - targetSeparation;
        return deltaZ == 0 ? double.NaN : Math.Abs(deltaForce / deltaZ);
    }

    /// <summary>
    /// <c>FDSpectroscopyCalculator.CalculateDeformation</c> in nm. Same two points as
    /// <see cref="Stiffness"/>, so the two can never disagree about the geometry they read.
    /// </summary>
    public static double Deformation(double[] forceNanonewtons, double[] separationMicrometres, double threshold)
    {
        double[] forceN = forceNanonewtons.Select(v => v * 1e-9).ToArray();
        double[] separationNm = separationMicrometres.Select(v => v * 1e3).ToArray();

        double maxForce = forceN.Max();
        int maxForceIndex = Array.IndexOf(forceN, maxForce);
        double targetForce = maxForce * threshold / 100;
        double targetSeparation = FindNearestDistance(separationNm, forceN, targetForce);

        return Math.Abs(separationNm[maxForceIndex] - targetSeparation);
    }

    /// <summary>
    /// <c>SpectroscopyAnalysisModel.GetPullOff</c>: minus the minimum of the retract force. This is the map
    /// our Adhesion corresponds to — <b>not</b> <c>Adhesion_Energy</c>, which is ∫F dz in joules.
    /// </summary>
    public static double PullOff(double[] retraceForceNanonewtons)
        => -retraceForceNanonewtons.Where(v => !double.IsNaN(v)).Min();
}
