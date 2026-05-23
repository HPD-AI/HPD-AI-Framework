namespace HPD.ML.BinaryClassification.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class LbfgsOptimizerTests
{
    [Fact]
    public void Minimize_Quadratic_FindsMinimum()
    {
        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 50);
        var result = optimizer.Minimize(
            p =>
            {
                var diff = p[0] - 3.0;
                return (diff * diff, [2.0 * diff]);
            },
            [0.0]);

        Assert.Equal(3.0, result[0], 0.01);
    }

    [Fact]
    public void Minimize_2D_Quadratic()
    {
        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 50);
        var result = optimizer.Minimize(
            p =>
            {
                var dx = p[0] - 1.0;
                var dy = p[1] - 2.0;
                return (dx * dx + dy * dy, [2.0 * dx, 2.0 * dy]);
            },
            [0.0, 0.0]);

        Assert.Equal(1.0, result[0], 0.05);
        Assert.Equal(2.0, result[1], 0.05);
    }

    [Fact]
    public void Minimize_Rosenbrock_Converges()
    {
        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 200, tolerance: 1e-10);
        var result = optimizer.Minimize(
            p =>
            {
                var x = p[0];
                var y = p[1];
                var a = 1.0 - x;
                var b = y - x * x;
                return (
                    a * a + 100.0 * b * b,
                    [-2.0 * a - 400.0 * x * b, 200.0 * b]);
            },
            [-1.0, -1.0]);

        Assert.Equal(1.0, result[0], 0.1);
        Assert.Equal(1.0, result[1], 0.1);
    }

    [Fact]
    public void Minimize_WithL2_ShrinksWeights()
    {
        var optimizer = new LbfgsOptimizer(l2Regularization: 5.0, maxIterations: 50);
        var result = optimizer.Minimize(
            p =>
            {
                var diff = p[0] - 10.0;
                return (diff * diff, [2.0 * diff]);
            },
            [0.0]);

        Assert.True(result[0] < 10.0);
        Assert.True(result[0] > 0.0);
    }

    [Fact]
    public void Minimize_WithL1_SparsifiesWeights()
    {
        var optimizer = new LbfgsOptimizer(l1Regularization: 0.5, l2Regularization: 0, maxIterations: 100);
        var result = optimizer.Minimize(
            p =>
            {
                var d1 = p[0] - 0.01;
                var d2 = p[1] - 5.0;
                return (d1 * d1 + d2 * d2, [2.0 * d1, 2.0 * d2]);
            },
            [0.0, 0.0]);

        Assert.Equal(0.0, result[0], 0.01);
        Assert.True(Math.Abs(result[1]) > 1.0);
    }

    [Fact]
    public void Minimize_ReportsProgress()
    {
        var progress = new ProgressSubject();
        var events = new List<ProgressEvent>();
        progress.Subscribe(new Observer<ProgressEvent>(events.Add));

        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 10);
        optimizer.Minimize(
            p =>
            {
                var diff = p[0] - 1.0;
                return (diff * diff, [2.0 * diff]);
            },
            [0.0],
            progress);

        Assert.True(events.Count >= 1);
        Assert.All(events, e => Assert.Equal("Loss", e.MetricName));
    }

    [Fact]
    public void Minimize_ConvergesEarly_WhenTight()
    {
        var progress = new ProgressSubject();
        var events = new List<ProgressEvent>();
        progress.Subscribe(new Observer<ProgressEvent>(events.Add));

        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 100, tolerance: 1e-3);
        optimizer.Minimize(
            p =>
            {
                var diff = p[0];
                return (diff * diff, [2.0 * diff]);
            },
            [0.001],
            progress);

        Assert.True(events.Count < 10);
    }

    [Fact]
    public void Minimize_ZeroGradientInitial_StopsImmediately()
    {
        var progress = new ProgressSubject();
        var events = new List<ProgressEvent>();
        progress.Subscribe(new Observer<ProgressEvent>(events.Add));

        var optimizer = new LbfgsOptimizer(l2Regularization: 0, maxIterations: 100, tolerance: 1e-5);
        var result = optimizer.Minimize(
            p => (p[0] * p[0], [2.0 * p[0]]),
            [0.0],
            progress);

        Assert.Equal(0.0, result[0], 0.001);
        Assert.True(events.Count <= 2);
    }
}

internal sealed class Observer<T>(Action<T> onNext) : IObserver<T>
{
    public void OnNext(T value) => onNext(value);
    public void OnError(Exception error) { }
    public void OnCompleted() { }
}
