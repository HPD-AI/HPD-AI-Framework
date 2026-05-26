using BenchmarkDotNet.Attributes;
using Helium.Finance.Distributions;

namespace Helium.Finance.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class DistributionBenchmarks
{
    private readonly double[] _points = [-8.0, -3.0, -1.0, 0.0, 1.0, 3.0, 8.0];
    private readonly double[] _probabilities = [1e-12, 0.001, 0.025, 0.5, 0.975, 0.999, 1.0 - 1e-12];

    [Benchmark]
    public double NormalPdfBatch()
    {
        var sum = 0.0;
        foreach (var point in _points)
            sum += NormalDistribution.Pdf(point);

        return sum;
    }

    [Benchmark]
    public double NormalCdfBatch()
    {
        var sum = 0.0;
        foreach (var point in _points)
            sum += NormalDistribution.Cdf(point);

        return sum;
    }

    [Benchmark]
    public double NormalInverseCdfBatch()
    {
        var sum = 0.0;
        foreach (var probability in _probabilities)
            sum += NormalDistribution.InverseCdf(probability);

        return sum;
    }

    [Benchmark]
    public double NormalDerivativeBatch()
    {
        var sum = 0.0;
        foreach (var point in _points)
            sum += NormalDistribution.PdfDerivative(point) + NormalDistribution.CdfDerivative(point);

        return sum;
    }
}
