using Helium.Finance.Curves;
using Helium.Finance.Options;
using Helium.Finance.Volatility;

namespace Helium.Finance.Tests;

public class VolatilityCalibrationTests
{
    [Fact]
    public void Black76CalibrationRecoversInputVolatilitySmile()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 0.5, 0.22, 0.98)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.25, 0.96)),
            QuoteFrom(new Black76Input(OptionRight.Put, 100.0, 110.0, 1.5, 0.28, 0.94))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);

        Assert.True(calibration.AllConverged);
        AssertClose(0.22, calibration.Points[0].Volatility, 1e-8);
        AssertClose(0.25, calibration.Points[1].Volatility, 1e-8);
        AssertClose(0.28, calibration.Points[2].Volatility, 1e-8);
    }

    [Fact]
    public void CalibrationPreservesFailureDiagnostics()
    {
        var quotes = new[]
        {
            new Black76VolatilityQuote(OptionRight.Call, 110.0, 100.0, 1.0, 0.95, MarketPrice: 1.0)
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);

        Assert.False(calibration.AllConverged);
        Assert.False(calibration.Points[0].Converged);
        Assert.Equal(ImpliedVolatilityStatus.BelowIntrinsic, calibration.Points[0].ImpliedVolatility.Status);
    }

    [Fact]
    public void CalibrationPreservesInvalidQuoteDiagnosticsWithoutThrowing()
    {
        var quotes = new[]
        {
            new Black76VolatilityQuote(OptionRight.Call, 100.0, 100.0, 1.0, 0.95, MarketPrice: 10.0),
            new Black76VolatilityQuote((OptionRight)999, double.NaN, 100.0, 1.0, 0.95, MarketPrice: 10.0)
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);

        Assert.False(calibration.AllConverged);
        Assert.True(calibration.Points[0].Converged);
        Assert.False(calibration.Points[1].Converged);
        Assert.Equal(ImpliedVolatilityStatus.NonFiniteInput, calibration.Points[1].ImpliedVolatility.Status);
    }

    [Fact]
    public void Black76CalibrationSnapshotsQuotesBeforeSolving()
    {
        var quotes = new IndexMutatingReadOnlyList<Black76VolatilityQuote>(
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 0.5, 0.20, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.30, 0.98)));

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);

        Assert.True(calibration.AllConverged);
        Assert.Equal(2, calibration.Points.Count);
        AssertClose(0.20, calibration.Points[0].Volatility, 1e-8);
        AssertClose(0.30, calibration.Points[1].Volatility, 1e-8);
    }

    [Fact]
    public void CalibrationRejectsNullAndEmptyQuoteSets()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ImpliedVolatilityCalibration.CalibrateBlack76(null!));
        Assert.Throws<ArgumentException>(() =>
            ImpliedVolatilityCalibration.CalibrateBlack76([]));
        Assert.Throws<ArgumentNullException>(() =>
            ImpliedVolatilityCalibration.CalibrateBachelier(null!));
        Assert.Throws<ArgumentException>(() =>
            ImpliedVolatilityCalibration.CalibrateBachelier([]));
    }

    [Fact]
    public void CalibrationBuildsVolatilityCurveWhenAllPointsConverge()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 0.5, 0.20, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.30, 0.98))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);
        var curve = calibration.ToBlackVolatilityCurve(extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(0.20, curve.Volatility(0.5), 1e-8);
        AssertClose(0.25, curve.Volatility(0.75), 1e-8);
        AssertClose(0.30, curve.Volatility(1.5), 1e-8);
    }

    [Fact]
    public void CalibrationBuildsVolatilityCurveFromUnorderedQuotesByExpiry()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 2.0, 0.30, 0.97)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 0.5, 0.20, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.25, 0.98))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);
        var curve = calibration.ToBlackVolatilityCurve(extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(0.20, curve.Volatility(0.5), 1e-8);
        AssertClose(0.25, curve.Volatility(1.0), 1e-8);
        AssertClose(0.30, curve.Volatility(2.0), 1e-8);
    }

    [Fact]
    public void CalibrationBuildsVarianceCurveFromCalibratedTotalVariance()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 0.5, 0.20, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.25, 0.98)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 2.0, 0.30, 0.97))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);
        var curve = calibration.ToBlackVarianceCurve(extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(0.5 * 0.20 * 0.20, curve.Variance(0.5), 1e-8);
        AssertClose(1.0 * 0.25 * 0.25, curve.Variance(1.0), 1e-8);
        AssertClose(2.0 * 0.30 * 0.30, curve.Variance(2.0), 1e-8);
        AssertClose(0.30, curve.Volatility(3.0), 1e-8);
    }

    [Fact]
    public void CalibrationVarianceCurveRejectsDecreasingTotalVarianceByDefault()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.40, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 2.0, 0.20, 0.98))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);

        Assert.Throws<ArgumentOutOfRangeException>(() => calibration.ToBlackVarianceCurve());

        var relaxed = calibration.ToBlackVarianceCurve(requireNondecreasingVariance: false);
        AssertClose(1.0 * 0.40 * 0.40, relaxed.Variance(1.0), 1e-8);
        AssertClose(2.0 * 0.20 * 0.20, relaxed.Variance(2.0), 1e-8);
    }

    [Fact]
    public void CalibrationBuildsVolatilitySurfaceFromCompleteGrid()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 1.0, 0.20, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 110.0, 1.0, 0.24, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 2.0, 0.30, 0.98)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 110.0, 2.0, 0.34, 0.98))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);
        var surface = calibration.ToBlackVolatilitySurface(extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(0.20, surface.Volatility(1.0, 90.0), 1e-8);
        AssertClose(0.27, surface.Volatility(1.5, 100.0), 1e-8);
        AssertClose(0.34, surface.Volatility(3.0, 120.0), 1e-8);
    }

    [Fact]
    public void CalibrationBuildsVarianceSurfaceFromCompleteGrid()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 1.0, 0.20, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 110.0, 1.0, 0.24, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 2.0, 0.30, 0.98)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 110.0, 2.0, 0.34, 0.98))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);
        var surface = calibration.ToBlackVarianceSurface(extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(1.0 * 0.20 * 0.20, surface.Variance(1.0, 90.0), 1e-8);
        AssertClose(2.0 * 0.34 * 0.34, surface.Variance(2.0, 110.0), 1e-8);
        AssertClose(0.34, surface.Volatility(3.0, 110.0), 1e-8);
    }

    [Fact]
    public void CalibrationCannotBuildSurfaceFromIncompleteGrid()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 1.0, 0.20, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 110.0, 1.0, 0.24, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 2.0, 0.30, 0.98))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);

        Assert.Throws<InvalidOperationException>(() => calibration.ToBlackVolatilitySurface());
        Assert.Throws<InvalidOperationException>(() => calibration.ToBlackVarianceSurface());
    }

    [Fact]
    public void CalibrationCannotBuildSurfaceFromDuplicateGridPoint()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 1.0, 0.20, 0.99)),
            QuoteFrom(new Black76Input(OptionRight.Put, 100.0, 90.0, 1.0, 0.22, 0.99))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);

        Assert.Throws<InvalidOperationException>(() => calibration.ToBlackVolatilitySurface());
        Assert.Throws<InvalidOperationException>(() => calibration.ToBlackVarianceSurface());
    }

    [Fact]
    public void CalibrationCannotBuildTermCurveFromDuplicateExpiries()
    {
        var quotes = new[]
        {
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 95.0, 1.0, 0.20, 0.98)),
            QuoteFrom(new Black76Input(OptionRight.Put, 100.0, 105.0, 1.0, 0.25, 0.98))
        };

        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(quotes);

        Assert.Throws<InvalidOperationException>(() => calibration.ToBlackVolatilityCurve());
        Assert.Throws<InvalidOperationException>(() => calibration.ToBlackVarianceCurve());
    }

    [Fact]
    public void CalibrationResultSnapshotsInputPoints()
    {
        var root = new Solvers.RootResult(true, 0.0, 0.0, 0, 0, 0.0, 0.0, Solvers.RootStatus.Converged);
        var points = new List<VolatilityCalibrationPoint>
        {
            new(
                TimeToExpiry: 1.0,
                Strike: 100.0,
                MarketPrice: 10.0,
                new ImpliedVolatilityResult(true, 0.25, 0.0, 0, ImpliedVolatilityStatus.Converged, root))
        };

        var calibration = new VolatilityCalibrationResult(points);
        points.Clear();

        Assert.Single(calibration.Points);
        Assert.True(calibration.AllConverged);
        AssertClose(0.25, calibration.Points[0].Volatility, 0.0);
    }

    [Fact]
    public void CalibrationResultRejectsMalformedPoints()
    {
        Assert.Throws<ArgumentException>(() => new VolatilityCalibrationResult([]));
        Assert.Throws<ArgumentException>(() => new VolatilityCalibrationResult([default]));
    }

    [Fact]
    public void CalibrationPointRejectsInvalidConvergedState()
    {
        var root = new Solvers.RootResult(true, 0.25, 0.0, 1, 3, 0.0, 1.0, Solvers.RootStatus.Converged);
        var impliedVolatility = new ImpliedVolatilityResult(
            true,
            0.25,
            0.0,
            1,
            ImpliedVolatilityStatus.Converged,
            root);

        Assert.Throws<ArgumentOutOfRangeException>(() => new VolatilityCalibrationPoint(
            double.NaN,
            Strike: 100.0,
            MarketPrice: 10.0,
            impliedVolatility));

        Assert.Throws<ArgumentOutOfRangeException>(() => new VolatilityCalibrationPoint(
            TimeToExpiry: 1.0,
            Strike: double.NaN,
            MarketPrice: 10.0,
            impliedVolatility));

        Assert.Throws<ArgumentOutOfRangeException>(() => new VolatilityCalibrationPoint(
            TimeToExpiry: 1.0,
            Strike: 100.0,
            MarketPrice: double.PositiveInfinity,
            impliedVolatility));

        Assert.Throws<ArgumentOutOfRangeException>(() => new VolatilityCalibrationPoint(
            TimeToExpiry: 1.0,
            Strike: 100.0,
            MarketPrice: 10.0,
            default));
    }

    [Fact]
    public void CalibrationPointPreservesFailedDiagnostics()
    {
        var failedRoot = new Solvers.RootResult(
            false,
            double.NaN,
            double.NaN,
            0,
            0,
            double.NaN,
            double.NaN,
            Solvers.RootStatus.NonFiniteInput);
        var failedVolatility = new ImpliedVolatilityResult(
            false,
            double.NaN,
            double.NaN,
            0,
            ImpliedVolatilityStatus.NonFiniteInput,
            failedRoot);

        var point = new VolatilityCalibrationPoint(
            TimeToExpiry: double.NaN,
            Strike: double.NaN,
            MarketPrice: double.NaN,
            failedVolatility);

        Assert.False(point.Converged);
        Assert.Equal(ImpliedVolatilityStatus.NonFiniteInput, point.ImpliedVolatility.Status);
    }

    [Fact]
    public void FailedCalibrationCannotBuildCurve()
    {
        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(new[]
        {
            new Black76VolatilityQuote(OptionRight.Call, 100.0, 100.0, 1.0, 0.95, MarketPrice: 200.0)
        });

        Assert.Throws<InvalidOperationException>(() => calibration.ToBlackVolatilityCurve());
        Assert.Throws<InvalidOperationException>(() => calibration.ToBlackVarianceCurve());
    }

    [Fact]
    public void BachelierCalibrationRecoversNormalVolatility()
    {
        var input = new BachelierInput(OptionRight.Put, -0.25, 0.10, 0.75, 11.0, 0.99);
        var quote = new BachelierVolatilityQuote(
            input.Right,
            input.Forward,
            input.Strike,
            input.TimeToExpiry,
            input.DiscountFactor,
            Bachelier.Price(input));

        var calibration = ImpliedVolatilityCalibration.CalibrateBachelier(
            new[] { quote },
            new ImpliedVolatilityOptions(UpperVolatility: 100.0));

        Assert.True(calibration.AllConverged);
        AssertClose(input.NormalVolatility, calibration.Points[0].Volatility, 1e-8);
    }

    [Fact]
    public void BachelierCalibrationSnapshotsQuotesBeforeSolving()
    {
        var first = new BachelierInput(OptionRight.Put, -0.25, 0.10, 0.75, 11.0, 0.99);
        var second = new BachelierInput(OptionRight.Call, 0.20, 0.10, 1.25, 9.0, 0.97);
        var quotes = new IndexMutatingReadOnlyList<BachelierVolatilityQuote>(
            QuoteFrom(first),
            QuoteFrom(second));

        var calibration = ImpliedVolatilityCalibration.CalibrateBachelier(
            quotes,
            new ImpliedVolatilityOptions(UpperVolatility: 100.0));

        Assert.True(calibration.AllConverged);
        Assert.Equal(2, calibration.Points.Count);
        AssertClose(first.NormalVolatility, calibration.Points[0].Volatility, 1e-8);
        AssertClose(second.NormalVolatility, calibration.Points[1].Volatility, 1e-8);
    }

    private static Black76VolatilityQuote QuoteFrom(Black76Input input) =>
        new(
            input.Right,
            input.Forward,
            input.Strike,
            input.TimeToExpiry,
            input.DiscountFactor,
            Black76.Price(input));

    private static BachelierVolatilityQuote QuoteFrom(BachelierInput input) =>
        new(
            input.Right,
            input.Forward,
            input.Strike,
            input.TimeToExpiry,
            input.DiscountFactor,
            Bachelier.Price(input));

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");

    private sealed class IndexMutatingReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly List<T> items;
        private bool mutated;

        public IndexMutatingReadOnlyList(params T[] items) => this.items = [.. items];

        public int Count => items.Count;

        public T this[int index]
        {
            get
            {
                if (!mutated && items.Count > 1)
                {
                    items.RemoveAt(items.Count - 1);
                    mutated = true;
                }

                return items[index];
            }
        }

        public IEnumerator<T> GetEnumerator() => items.ToArray().AsEnumerable().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
