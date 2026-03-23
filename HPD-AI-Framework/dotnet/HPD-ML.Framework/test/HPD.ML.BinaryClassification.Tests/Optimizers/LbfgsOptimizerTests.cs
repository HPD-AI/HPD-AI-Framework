namespace HPD.ML.BinaryClassification.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public class LbfgsOptimizerTests
{
    [Fact]
    public void Minimize_Quadratic_FindsMinimum()
    {
        // f(x) = (x - 3)²
        Func<Vector<Var<Double>>, Var<Double>> loss = p =>
        {
            var diff = p[0] - Var<Double>.Constant(new Double(3));
            return diff * diff;
        };

        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 50);
        var result = optimizer.Minimize(loss, Vector<Double>.FromArray(new Double(0)));

        Assert.Equal(3.0, (double)result[0], 0.01);
    }

    [Fact]
    public void Minimize_2D_Quadratic()
    {
        // f(x,y) = (x-1)² + (y-2)²
        Func<Vector<Var<Double>>, Var<Double>> loss = p =>
        {
            var dx = p[0] - Var<Double>.Constant(new Double(1));
            var dy = p[1] - Var<Double>.Constant(new Double(2));
            return dx * dx + dy * dy;
        };

        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 50);
        var result = optimizer.Minimize(loss, Vector<Double>.FromArray(new Double(0), new Double(0)));

        Assert.Equal(1.0, (double)result[0], 0.05);
        Assert.Equal(2.0, (double)result[1], 0.05);
    }

    [Fact]
    public void Minimize_Rosenbrock_Converges()
    {
        // f(x,y) = (1-x)² + 100(y-x²)²
        Func<Vector<Var<Double>>, Var<Double>> loss = p =>
        {
            var one = Var<Double>.Constant(new Double(1));
            var hundred = Var<Double>.Constant(new Double(100));
            var a = one - p[0];
            var b = p[1] - p[0] * p[0];
            return a * a + hundred * b * b;
        };

        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 200, tolerance: 1e-10);
        var result = optimizer.Minimize(loss, Vector<Double>.FromArray(new Double(-1), new Double(-1)));

        Assert.Equal(1.0, (double)result[0], 0.1);
        Assert.Equal(1.0, (double)result[1], 0.1);
    }

    [Fact]
    public void Minimize_WithL2_ShrinksWeights()
    {
        // f(x) = (x - 10)² + L2 → minimum pulled toward 0
        Func<Vector<Var<Double>>, Var<Double>> loss = p =>
        {
            var diff = p[0] - Var<Double>.Constant(new Double(10));
            return diff * diff;
        };

        var optimizer = new LbfgsOptimizer(l2Regularization: 5.0, maxIterations: 50);
        var result = optimizer.Minimize(loss, Vector<Double>.FromArray(new Double(0)));

        // With strong L2, result should be between 0 and 10 (pulled toward 0)
        Assert.True((double)result[0] < 10.0);
        Assert.True((double)result[0] > 0.0);
    }

    [Fact]
    public void Minimize_WithL1_SparsifiesWeights()
    {
        // Two params: one at target 0.01 (should go to 0 with L1), one at target 5
        Func<Vector<Var<Double>>, Var<Double>> loss = p =>
        {
            var d1 = p[0] - Var<Double>.Constant(new Double(0.01));
            var d2 = p[1] - Var<Double>.Constant(new Double(5));
            return d1 * d1 + d2 * d2;
        };

        var optimizer = new LbfgsOptimizer(l1Regularization: 0.5, l2Regularization: 0, maxIterations: 100);
        var result = optimizer.Minimize(loss, Vector<Double>.FromArray(new Double(0), new Double(0)));

        // Small target should be zeroed out by L1
        Assert.Equal(0.0, (double)result[0], 0.01);
        // Large target should survive
        Assert.True(Math.Abs((double)result[1]) > 1.0);
    }

    [Fact]
    public void Minimize_ReportsProgress()
    {
        Func<Vector<Var<Double>>, Var<Double>> loss = p =>
        {
            var diff = p[0] - Var<Double>.Constant(new Double(1));
            return diff * diff;
        };

        var progress = new ProgressSubject();
        var events = new List<ProgressEvent>();
        progress.Subscribe(new Observer<ProgressEvent>(events.Add));

        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 10);
        optimizer.Minimize(loss, Vector<Double>.FromArray(new Double(0)), progress);

        Assert.True(events.Count >= 1);
        Assert.All(events, e => Assert.Equal("Loss", e.MetricName));
    }

    [Fact]
    public void Minimize_ConvergesEarly_WhenTight()
    {
        Func<Vector<Var<Double>>, Var<Double>> loss = p =>
        {
            var diff = p[0] - Var<Double>.Constant(new Double(0));
            return diff * diff;
        };

        var progress = new ProgressSubject();
        var events = new List<ProgressEvent>();
        progress.Subscribe(new Observer<ProgressEvent>(events.Add));

        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 100, tolerance: 1e-3);
        optimizer.Minimize(loss, Vector<Double>.FromArray(new Double(0.001)), progress);

        // Should converge in very few iterations since starting near minimum
        Assert.True(events.Count < 10);
    }

    [Fact]
    public void Minimize_ZeroGradientInitial_StopsImmediately()
    {
        // Start exactly at minimum
        Func<Vector<Var<Double>>, Var<Double>> loss = p => p[0] * p[0];

        var progress = new ProgressSubject();
        var events = new List<ProgressEvent>();
        progress.Subscribe(new Observer<ProgressEvent>(events.Add));

        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 100, tolerance: 1e-5);
        var result = optimizer.Minimize(loss, Vector<Double>.FromArray(new Double(0)), progress);

        Assert.Equal(0.0, (double)result[0], 0.001);
        Assert.True(events.Count <= 2); // At most one eval before detecting zero gradient
    }
}

/// <summary>Simple IObserver helper for tests.</summary>
internal sealed class Observer<T>(Action<T> onNext) : IObserver<T>
{
    public void OnNext(T value) => onNext(value);
    public void OnError(Exception error) { }
    public void OnCompleted() { }
}
