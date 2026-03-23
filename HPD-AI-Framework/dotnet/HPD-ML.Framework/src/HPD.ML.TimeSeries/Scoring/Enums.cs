namespace HPD.ML.TimeSeries;

public enum ErrorFunction
{
    SignedDifference,
    AbsoluteDifference,
    SignedProportion,
    AbsoluteProportion,
    SquaredDifference
}

public enum AlertingMode
{
    RawScore,
    PValueScore,
    MartingaleScore
}

public enum AnomalySide
{
    TwoSided,
    PositiveOnly,
    NegativeOnly
}

public enum MartingaleType
{
    None,
    Power,
    Mixture
}

public enum RankSelectionMethod
{
    Fixed,
    Exact
}
