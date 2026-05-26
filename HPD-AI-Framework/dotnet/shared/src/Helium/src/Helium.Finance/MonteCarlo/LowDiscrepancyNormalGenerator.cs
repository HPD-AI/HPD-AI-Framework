using Helium.Finance.Distributions;

namespace Helium.Finance.MonteCarlo;

public sealed class LowDiscrepancyNormalGenerator
{
    private readonly HaltonSequence _sequence;

    public LowDiscrepancyNormalGenerator(int startIndex = 1)
    {
        _sequence = new HaltonSequence(@base: 2, startIndex);
    }

    public double NextStandardNormal()
    {
        var uniform = _sequence.Next();
        if (!double.IsFinite(uniform) || uniform <= 0.0 || uniform >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(uniform), "Low-discrepancy uniform variate must be finite and strictly inside (0, 1).");

        var normal = NormalDistribution.InverseCdf(uniform);
        if (!double.IsFinite(normal))
            throw new ArgumentOutOfRangeException(nameof(uniform), "Low-discrepancy normal variate must be finite.");

        return normal;
    }
}
