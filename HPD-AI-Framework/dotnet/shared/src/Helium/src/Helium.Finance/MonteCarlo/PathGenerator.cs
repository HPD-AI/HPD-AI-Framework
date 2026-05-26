using Helium.Finance.Processes;

namespace Helium.Finance.MonteCarlo;

public static class PathGenerator
{
    public static void Generate(
        GeometricBrownianMotionProcess process,
        double timeToExpiry,
        Span<double> path,
        NormalRandomGenerator random)
    {
        ArgumentNullException.ThrowIfNull(random);

        process.Validate();

        if (!double.IsFinite(timeToExpiry) || timeToExpiry < 0.0)
            throw new ArgumentOutOfRangeException(nameof(timeToExpiry), "Time to expiry must be finite and nonnegative.");

        if (path.Length == 0)
            throw new ArgumentException("Path must contain at least the initial node.", nameof(path));

        path[0] = process.InitialValue;

        if (path.Length == 1)
            return;

        var dt = timeToExpiry / (path.Length - 1);
        for (var i = 1; i < path.Length; i++)
            path[i] = process.Evolve(path[i - 1], dt, random.NextStandardNormal());
    }
}
