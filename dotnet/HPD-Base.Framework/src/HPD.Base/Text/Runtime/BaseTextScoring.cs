namespace HPD.Base;

/// <summary>Owns the exact candidate-local fixed-point lexical score.</summary>
public static class BaseTextScoring
{
    /// <summary>The stable v1 scoring identity.</summary>
    public const string ContractId = "hpd.base.text.score.local-saturation-fixed-v1";
    /// <summary>The exact fixed-point scale.</summary>
    public const ulong Scale = 1_000_000;

    /// <summary>Computes one weighted feature contribution.</summary>
    public static ulong Feature(int weight, int termFrequency, int fieldTokenCount)
    {
        if (weight is < 1 or > 16) throw Invalid();
        if (termFrequency is < 1 or > 65_535) throw Invalid();
        if (fieldTokenCount is < 0 or > BaseTextAnalyzers.MaximumTokensPerField) throw Invalid();

        UInt128 numerator = checked((UInt128)Scale * 22u * (uint)termFrequency);
        UInt128 denominator = checked((UInt128)(10u * (uint)termFrequency) + 3u + (UInt128)(9u * (uint)fieldTokenCount));
        UInt128 units = RoundHalfEven(numerator, denominator);
        UInt128 weighted = checked(units * (uint)weight);
        if (weighted > ulong.MaxValue) throw Invalid();
        return (ulong)weighted;
    }

    /// <summary>Computes the checked sum of distinct feature contributions.</summary>
    public static BaseTextScore Sum(IEnumerable<ulong> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        UInt128 total = 0;
        foreach (ulong feature in features) total = checked(total + feature);
        if (total > ulong.MaxValue) throw Invalid();
        return new BaseTextScore { Units = (ulong)total };
    }

    internal static UInt128 RoundHalfEven(UInt128 numerator, UInt128 denominator)
    {
        if (denominator == 0) throw Invalid();
        UInt128 quotient = numerator / denominator;
        UInt128 remainder = numerator % denominator;
        UInt128 doubled = checked(remainder * 2);
        if (doubled < denominator) return quotient;
        if (doubled > denominator) return checked(quotient + 1);
        return (quotient & 1) == 0 ? quotient : checked(quotient + 1);
    }

    private static InvalidOperationException Invalid() => new("Invalid lexical score evidence.", new ArgumentException(BaseTextErrorCodes.ProviderContractInvalid));
}
