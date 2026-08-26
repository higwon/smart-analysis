namespace SmartAnalysis.Analysis.Operations;

/// <summary>The kind of dataset an operation accepts (drives applicability + UI menus + AI search).</summary>
public enum DataKind
{
    ScanImage,
    LineProfile,
    Spectrum,
    ForceCurve,

    /// <summary>Many force curves from one acquisition — a force-volume map, or a set of hand-placed points.</summary>
    ForceVolume,
}

/// <summary>What an operation produces.</summary>
public enum OutputKind
{
    /// <summary>A new derived dataset (a transform).</summary>
    DerivedDataset,

    /// <summary>A measurement artifact (scalars/tables), not a dataset.</summary>
    Artifact,
}
