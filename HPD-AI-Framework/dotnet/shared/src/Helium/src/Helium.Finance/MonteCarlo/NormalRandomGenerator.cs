namespace Helium.Finance.MonteCarlo;

public sealed class NormalRandomGenerator
{
    private readonly Random _random;
    private bool _hasSpare;
    private double _spare;

    public NormalRandomGenerator(int seed)
    {
        _random = new Random(seed);
    }

    public double NextStandardNormal()
    {
        if (_hasSpare)
        {
            _hasSpare = false;
            return _spare;
        }

        double u;
        double v;
        double radiusSquared;

        do
        {
            u = 2.0 * _random.NextDouble() - 1.0;
            v = 2.0 * _random.NextDouble() - 1.0;
            radiusSquared = u * u + v * v;
        }
        while (radiusSquared <= 0.0 || radiusSquared >= 1.0);

        var scale = Math.Sqrt(-2.0 * Math.Log(radiusSquared) / radiusSquared);
        _spare = v * scale;
        if (!double.IsFinite(_spare))
            throw new InvalidOperationException("Normal random generator produced a nonfinite spare variate.");

        _hasSpare = true;
        var value = u * scale;
        if (!double.IsFinite(value))
            throw new InvalidOperationException("Normal random generator produced a nonfinite variate.");

        return value;
    }
}
