using System.Diagnostics;
using Helium.Finance.Distributions;
using Helium.Finance.Options;

const int iterations = 10_000_000;

var black76Cases = new[]
{
    new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.97),
    new Black76Input(OptionRight.Put, 100.0, 105.0, 0.75, 0.25, 0.95),
    new Black76Input(OptionRight.Call, 120.0, 100.0, 2.0, 0.30, 0.92),
    new Black76Input(OptionRight.Put, 80.0, 100.0, 1.5, 0.18, 0.96)
};

var bachelierCases = new[]
{
    new BachelierInput(OptionRight.Call, 100.0, 100.0, 1.0, 20.0, 0.97),
    new BachelierInput(OptionRight.Put, 100.0, 105.0, 0.75, 18.0, 0.95),
    new BachelierInput(OptionRight.Call, 0.01, -0.01, 2.0, 0.02, 0.99),
    new BachelierInput(OptionRight.Put, -0.25, 0.10, 1.5, 0.30, 0.96)
};

var normalPoints = new[] { -8.0, -3.0, -1.0, 0.0, 1.0, 3.0, 8.0 };

Run("NormalPdfCdf", iterations, i =>
{
    var x = normalPoints[i % normalPoints.Length];
    return NormalDistribution.Pdf(x) + NormalDistribution.Cdf(x);
});

Run("Black76Price", iterations, i => Black76.Price(black76Cases[i & 3]));
Run("BachelierPrice", iterations, i => Bachelier.Price(bachelierCases[i & 3]));

static void Run(string name, int iterations, Func<int, double> operation)
{
    var sum = 0.0;
    for (var i = 0; i < 10_000; i++)
        sum += operation(i);

    var stopwatch = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
        sum += operation(i);
    stopwatch.Stop();

    Console.WriteLine($"{name},{iterations},{stopwatch.Elapsed.TotalMilliseconds:F3},{sum:R}");
}
