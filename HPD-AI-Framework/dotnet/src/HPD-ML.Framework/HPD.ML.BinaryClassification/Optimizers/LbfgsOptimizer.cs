namespace HPD.ML.BinaryClassification;

using Helium.Algebra;
using Helium.Algorithms;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

/// <summary>
/// L-BFGS optimizer using Helium's reverse-mode autodiff.
/// Approximates the inverse Hessian using a history of gradient differences.
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

    /// <summary>
    /// Minimize a loss function. Returns optimized parameters.
    /// </summary>
    public Vector<Double> Minimize(
        Func<Vector<Var<Double>>, Var<Double>> loss,
        Vector<Double> initial,
        ProgressSubject? progress = null)
    {
        int n = initial.Length;
        var parameters = initial;

        // L-BFGS history buffers
        var sHistory = new Queue<Vector<Double>>(_memorySize);
        var yHistory = new Queue<Vector<Double>>(_memorySize);
        var rhoHistory = new Queue<Double>(_memorySize);

        Vector<Double>? prevGrad = null;
        Vector<Double>? prevParams = null;

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            // Add L2 regularization to loss
            Func<Vector<Var<Double>>, Var<Double>> regularizedLoss = p =>
            {
                var baseLoss = loss(p);

                if (_l2Regularization > 0)
                {
                    var l2 = Var<Double>.Constant(new Double(0));
                    for (int i = 0; i < p.Length; i++)
                        l2 = l2 + p[i] * p[i];
                    baseLoss = baseLoss + Var<Double>.Constant(new Double(_l2Regularization / 2)) * l2;
                }

                return baseLoss;
            };

            // Compute loss and gradient via Helium autodiff
            var (lossValue, gradient) = Grad.ValueAndGrad(regularizedLoss, parameters);

            // Check convergence
            double gradNorm = 0;
            for (int i = 0; i < n; i++)
                gradNorm += (double)gradient[i] * (double)gradient[i];
            gradNorm = Math.Sqrt(gradNorm);

            progress?.OnNext(new ProgressEvent
            {
                Epoch = iter,
                MetricValue = (double)lossValue,
                MetricName = "Loss"
            });

            if (gradNorm < _tolerance)
                break;

            // L-BFGS two-loop recursion to compute search direction
            var direction = ComputeDirection(gradient, sHistory, yHistory, rhoHistory);

            // Line search (backtracking Armijo)
            double step = LineSearch(regularizedLoss, parameters, direction, lossValue, gradient);

            // Update parameters
            var newParams = new Double[n];
            for (int i = 0; i < n; i++)
                newParams[i] = parameters[i] + new Double(step) * direction[i];
            var newParameters = Vector<Double>.FromArray(newParams);

            // Update L-BFGS history
            if (prevGrad.HasValue && prevParams.HasValue)
            {
                var s = newParameters - prevParams.Value;
                var y = gradient - prevGrad.Value;
                var rho = Double.Invert(Dot(y, s));

                if ((double)rho > 0) // Skip update if curvature condition violated
                {
                    if (sHistory.Count >= _memorySize)
                    {
                        sHistory.Dequeue();
                        yHistory.Dequeue();
                        rhoHistory.Dequeue();
                    }
                    sHistory.Enqueue(s);
                    yHistory.Enqueue(y);
                    rhoHistory.Enqueue(rho);
                }
            }

            prevGrad = gradient;
            prevParams = parameters;
            parameters = newParameters;
        }

        // Apply L1 proximal operator if needed
        if (_l1Regularization > 0)
        {
            var pruned = new Double[n];
            for (int i = 0; i < n; i++)
            {
                double w = (double)parameters[i];
                pruned[i] = new Double(
                    Math.Sign(w) * Math.Max(0, Math.Abs(w) - _l1Regularization));
            }
            parameters = Vector<Double>.FromArray(pruned);
        }

        return parameters;
    }

    private Vector<Double> ComputeDirection(
        Vector<Double> gradient,
        Queue<Vector<Double>> sHistory,
        Queue<Vector<Double>> yHistory,
        Queue<Double> rhoHistory)
    {
        int n = gradient.Length;
        int m = sHistory.Count;

        if (m == 0)
        {
            // Steepest descent
            return -gradient;
        }

        var s = sHistory.ToArray();
        var y = yHistory.ToArray();
        var rho = rhoHistory.ToArray();
        var alpha = new Double[m];

        // q = gradient (copy)
        var q = new Double[n];
        for (int i = 0; i < n; i++) q[i] = gradient[i];

        // First loop (reverse)
        for (int i = m - 1; i >= 0; i--)
        {
            alpha[i] = rho[i] * DotRaw(s[i], q, n);
            for (int j = 0; j < n; j++)
                q[j] = q[j] - alpha[i] * y[i][j];
        }

        // Initial Hessian approximation: H0 = (s_k·y_k)/(y_k·y_k) * I
        var lastS = s[m - 1];
        var lastY = y[m - 1];
        Double gamma = Dot(lastS, lastY) / Dot(lastY, lastY);
        var r = new Double[n];
        for (int i = 0; i < n; i++) r[i] = gamma * q[i];

        // Second loop (forward)
        for (int i = 0; i < m; i++)
        {
            Double beta = rho[i] * DotRaw(y[i], r, n);
            for (int j = 0; j < n; j++)
                r[j] = r[j] + (alpha[i] - beta) * s[i][j];
        }

        // Negate for descent direction
        for (int i = 0; i < n; i++) r[i] = -r[i];
        return Vector<Double>.FromArray(r);
    }

    private double LineSearch(
        Func<Vector<Var<Double>>, Var<Double>> loss,
        Vector<Double> parameters,
        Vector<Double> direction,
        Double currentLoss,
        Vector<Double> gradient)
    {
        double c = 1e-4; // Armijo condition parameter
        double step = 1.0;
        double dirGrad = (double)Dot(gradient, direction);
        int n = parameters.Length;

        for (int i = 0; i < 20; i++) // max 20 backtracking steps
        {
            var trial = new Double[n];
            for (int j = 0; j < n; j++)
                trial[j] = parameters[j] + new Double(step) * direction[j];

            // Evaluate loss at trial point — suspend outer tape to avoid nesting
            var trialParams = Vector<Double>.FromArray(trial);
            var saved = Tape<Double>.Current;
            Tape<Double>.Current = null;
            Double trialLoss;
            try
            {
                using var session = Tape<Double>.Begin();
                var vars = new Var<Double>[n];
                for (int j = 0; j < n; j++)
                    vars[j] = Var<Double>.Constant(trial[j]);
                var result = loss(Vector<Var<Double>>.FromArray(vars));
                trialLoss = result.Value;
            }
            finally
            {
                Tape<Double>.Current = saved;
            }

            if ((double)trialLoss <= (double)currentLoss + c * step * dirGrad)
                return step;

            step *= 0.5;
        }

        return step;
    }

    // Vector helpers operating on Helium types
    private static Double Dot(Vector<Double> a, Vector<Double> b) => Vector<Double>.Dot(a, b);
    private static Double DotRaw(Vector<Double> a, Double[] b, int n)
    {
        Double sum = new Double(0);
        for (int i = 0; i < n; i++) sum = sum + a[i] * b[i];
        return sum;
    }
}
