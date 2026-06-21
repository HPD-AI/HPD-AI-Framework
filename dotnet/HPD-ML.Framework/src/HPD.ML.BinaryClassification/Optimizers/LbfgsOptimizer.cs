namespace HPD.ML.BinaryClassification;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Limited-memory BFGS optimizer for numeric ML objectives.
/// The objective returns the loss and gradient for the provided parameter vector.
/// </summary>
internal sealed class LbfgsOptimizer
{
    private readonly int _memorySize;
    private readonly double _tolerance;
    private readonly int _maxIterations;
    private readonly double _l1Regularization;
    private readonly double _l2Regularization;

    public LbfgsOptimizer(
        int memorySize = 20,
        double tolerance = 1e-7,
        int maxIterations = 100,
        double l1Regularization = 0,
        double l2Regularization = 1.0)
    {
        _memorySize = memorySize;
        _tolerance = tolerance;
        _maxIterations = maxIterations;
        _l1Regularization = l1Regularization;
        _l2Regularization = l2Regularization;
    }

    public double[] Minimize(
        Func<double[], (double Loss, double[] Gradient)> objective,
        ReadOnlySpan<double> initial,
        ProgressSubject? progress = null)
    {
        int n = initial.Length;
        var parameters = initial.ToArray();

        var sHistory = new Queue<double[]>(_memorySize);
        var yHistory = new Queue<double[]>(_memorySize);
        var rhoHistory = new Queue<double>(_memorySize);

        double[]? previousGradient = null;
        double[]? previousParameters = null;

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            var (loss, gradient) = EvaluateRegularized(objective, parameters);
            var gradientNorm = Norm(gradient);

            progress?.OnNext(new ProgressEvent
            {
                Epoch = iter,
                MetricValue = loss,
                MetricName = "Loss"
            });

            if (gradientNorm < _tolerance)
                break;

            var direction = ComputeDirection(gradient, sHistory, yHistory, rhoHistory);
            var step = LineSearch(objective, parameters, direction, loss, gradient);

            var newParameters = new double[n];
            for (int i = 0; i < n; i++)
                newParameters[i] = parameters[i] + step * direction[i];

            if (previousGradient is not null && previousParameters is not null)
            {
                var s = Subtract(newParameters, previousParameters);
                var y = Subtract(gradient, previousGradient);
                var ys = Dot(y, s);

                if (ys > 1e-20)
                {
                    if (sHistory.Count >= _memorySize)
                    {
                        sHistory.Dequeue();
                        yHistory.Dequeue();
                        rhoHistory.Dequeue();
                    }

                    sHistory.Enqueue(s);
                    yHistory.Enqueue(y);
                    rhoHistory.Enqueue(1.0 / ys);
                }
            }

            previousGradient = gradient;
            previousParameters = parameters;
            parameters = newParameters;
        }

        if (_l1Regularization > 0)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                var value = parameters[i];
                parameters[i] = Math.Sign(value) * Math.Max(0, Math.Abs(value) - _l1Regularization);
            }
        }

        return parameters;
    }

    private (double Loss, double[] Gradient) EvaluateRegularized(
        Func<double[], (double Loss, double[] Gradient)> objective,
        double[] parameters)
    {
        var (loss, gradient) = objective(parameters);

        if (_l2Regularization <= 0)
            return (loss, gradient);

        var regularizedGradient = gradient.ToArray();
        var l2 = 0.0;
        for (int i = 0; i < parameters.Length; i++)
        {
            l2 += parameters[i] * parameters[i];
            regularizedGradient[i] += _l2Regularization * parameters[i];
        }

        return (loss + 0.5 * _l2Regularization * l2, regularizedGradient);
    }

    private double[] ComputeDirection(
        double[] gradient,
        Queue<double[]> sHistory,
        Queue<double[]> yHistory,
        Queue<double> rhoHistory)
    {
        int n = gradient.Length;
        int m = sHistory.Count;

        if (m == 0)
            return Scale(gradient, -1.0);

        var s = sHistory.ToArray();
        var y = yHistory.ToArray();
        var rho = rhoHistory.ToArray();
        var alpha = new double[m];
        var q = gradient.ToArray();

        for (int i = m - 1; i >= 0; i--)
        {
            alpha[i] = rho[i] * Dot(s[i], q);
            for (int j = 0; j < n; j++)
                q[j] -= alpha[i] * y[i][j];
        }

        var gammaDenominator = Dot(y[m - 1], y[m - 1]);
        var gamma = gammaDenominator > 0
            ? Dot(s[m - 1], y[m - 1]) / gammaDenominator
            : 1.0;

        var r = Scale(q, gamma);

        for (int i = 0; i < m; i++)
        {
            var beta = rho[i] * Dot(y[i], r);
            for (int j = 0; j < n; j++)
                r[j] += (alpha[i] - beta) * s[i][j];
        }

        for (int i = 0; i < n; i++)
            r[i] = -r[i];

        return r;
    }

    private double LineSearch(
        Func<double[], (double Loss, double[] Gradient)> objective,
        double[] parameters,
        double[] direction,
        double currentLoss,
        double[] gradient)
    {
        const double c = 1e-4;
        var step = 1.0;
        var dirGrad = Dot(gradient, direction);
        var trial = new double[parameters.Length];

        for (int i = 0; i < 20; i++)
        {
            for (int j = 0; j < parameters.Length; j++)
                trial[j] = parameters[j] + step * direction[j];

            var (trialLoss, _) = EvaluateRegularized(objective, trial);
            if (trialLoss <= currentLoss + c * step * dirGrad)
                return step;

            step *= 0.5;
        }

        return step;
    }

    private static double Dot(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        var sum = 0.0;
        for (int i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    private static double Norm(ReadOnlySpan<double> values) => Math.Sqrt(Dot(values, values));

    private static double[] Scale(ReadOnlySpan<double> values, double scalar)
    {
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = values[i] * scalar;
        return result;
    }

    private static double[] Subtract(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
    {
        var result = new double[left.Length];
        for (int i = 0; i < left.Length; i++)
            result[i] = left[i] - right[i];
        return result;
    }
}
