using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class BatchPricingTests
{
    [Fact]
    public void Black76BatchPriceMatchesScalar()
    {
        Black76Input[] inputs =
        [
            new(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.97),
            new(OptionRight.Put, 95.0, 100.0, 0.5, 0.25, 0.98)
        ];
        Span<double> destination = stackalloc double[inputs.Length];

        Black76.BatchPrice(inputs, destination);

        AssertClose(Black76.Price(inputs[0]), destination[0], 1e-15);
        AssertClose(Black76.Price(inputs[1]), destination[1], 1e-15);
    }

    [Fact]
    public void BachelierBatchPriceMatchesScalar()
    {
        BachelierInput[] inputs =
        [
            new(OptionRight.Call, 100.0, 100.0, 1.0, 20.0, 0.97),
            new(OptionRight.Put, -1.0, 0.5, 0.5, 12.0, 0.98)
        ];
        Span<double> destination = stackalloc double[inputs.Length];

        Bachelier.BatchPrice(inputs, destination);

        AssertClose(Bachelier.Price(inputs[0]), destination[0], 1e-15);
        AssertClose(Bachelier.Price(inputs[1]), destination[1], 1e-15);
    }

    [Fact]
    public void BlackScholesBatchPriceMatchesScalar()
    {
        BlackScholesInput[] inputs =
        [
            new(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01),
            new(OptionRight.Put, 95.0, 100.0, 0.5, 0.25, 0.02, 0.00)
        ];
        Span<double> destination = stackalloc double[inputs.Length];

        BlackScholes.BatchPrice(inputs, destination);

        AssertClose(BlackScholes.Price(inputs[0]), destination[0], 1e-15);
        AssertClose(BlackScholes.Price(inputs[1]), destination[1], 1e-15);
    }

    [Fact]
    public void BatchPriceRejectsShortDestination()
    {
        Black76Input[] inputs =
        [
            new(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.97),
            new(OptionRight.Call, 101.0, 100.0, 1.0, 0.20, 0.97)
        ];
        var destination = new double[1];

        Assert.Throws<ArgumentException>(() => Black76.BatchPrice(inputs, destination));
    }

    [Fact]
    public void BlackScholesBatchPriceRejectsShortDestination()
    {
        BlackScholesInput[] inputs =
        [
            new(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01),
            new(OptionRight.Put, 95.0, 100.0, 0.5, 0.25, 0.02, 0.00)
        ];
        var destination = new double[1];

        Assert.Throws<ArgumentException>(() => BlackScholes.BatchPrice(inputs, destination));
    }

    [Fact]
    public void BachelierBatchPriceRejectsShortDestination()
    {
        BachelierInput[] inputs =
        [
            new(OptionRight.Call, 100.0, 100.0, 1.0, 20.0, 0.97),
            new(OptionRight.Put, -1.0, 0.5, 0.5, 12.0, 0.98)
        ];
        var destination = new double[1];

        Assert.Throws<ArgumentException>(() => Bachelier.BatchPrice(inputs, destination));
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
