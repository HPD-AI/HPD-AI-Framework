namespace HPD.ML.Backends.Mlx.Training;

public sealed class MlxAdamOptimizer : IMlxOptimizer, IDisposable
{
    private readonly MlxFloatBackend _backend;
    private readonly Dictionary<MlxFloatTensor, State> _states = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public MlxAdamOptimizer(
        MlxFloatBackend backend,
        float learningRate = 0.001f,
        float beta1 = 0.9f,
        float beta2 = 0.999f,
        float epsilon = 1e-8f,
        float weightDecay = 0.0f)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        ValidatePositiveFinite(learningRate, nameof(learningRate));
        ValidateUnitInterval(beta1, nameof(beta1));
        ValidateUnitInterval(beta2, nameof(beta2));
        ValidatePositiveFinite(epsilon, nameof(epsilon));
        if (!float.IsFinite(weightDecay) || weightDecay < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(weightDecay), "Weight decay must be finite and non-negative.");

        LearningRate = learningRate;
        Beta1 = beta1;
        Beta2 = beta2;
        Epsilon = epsilon;
        WeightDecay = weightDecay;
    }

    public float LearningRate { get; }
    public float Beta1 { get; }
    public float Beta2 { get; }
    public float Epsilon { get; }
    public float WeightDecay { get; }

    public void Step(MlxFloatTensor parameter, MlxFloatTensor gradient)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(gradient);
        if (parameter.Rows != gradient.Rows || parameter.Cols != gradient.Cols)
            throw new ArgumentException("Gradient shape must match parameter shape.", nameof(gradient));

        var state = GetState(parameter);
        state.Step++;

        MlxFloatTensor? decay = null;
        MlxFloatTensor? decayedGradient = null;
        var effectiveGradient = gradient;
        if (WeightDecay != 0.0f)
        {
            decay = _backend.Scale(parameter, WeightDecay);
            decayedGradient = _backend.Add(gradient, decay);
            effectiveGradient = decayedGradient;
        }

        try
        {
            using var beta1M = _backend.Scale(state.M, Beta1);
            using var oneMinusBeta1Gradient = _backend.Scale(effectiveGradient, 1.0f - Beta1);
            using var newM = _backend.Add(beta1M, oneMinusBeta1Gradient);

            using var beta2V = _backend.Scale(state.V, Beta2);
            using var gradSquared = _backend.Square(effectiveGradient);
            using var oneMinusBeta2GradSquared = _backend.Scale(gradSquared, 1.0f - Beta2);
            using var newV = _backend.Add(beta2V, oneMinusBeta2GradSquared);

            state.M.UpdateFromSpan(newM.ToArray());
            state.V.UpdateFromSpan(newV.ToArray());

            var biasCorrection1 = 1.0f - MathF.Pow(Beta1, state.Step);
            var biasCorrection2 = 1.0f - MathF.Pow(Beta2, state.Step);
            using var mHat = _backend.DivideByScalar(state.M, biasCorrection1);
            using var vHat = _backend.DivideByScalar(state.V, biasCorrection2);
            using var sqrtVHat = _backend.Sqrt(vHat);
            using var denominator = _backend.AddScalar(sqrtVHat, Epsilon);
            using var normalizedStep = _backend.Divide(mHat, denominator);
            using var step = _backend.Scale(normalizedStep, LearningRate);
            using var updated = _backend.Subtract(parameter, step);
            parameter.UpdateFromSpan(updated.ToArray());
        }
        finally
        {
            decayedGradient?.Dispose();
            decay?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var state in _states.Values)
        {
            state.M.Dispose();
            state.V.Dispose();
        }
        _states.Clear();
    }

    private State GetState(MlxFloatTensor parameter)
    {
        if (_states.TryGetValue(parameter, out var state))
            return state;

        state = new State(
            _backend.CreateMatrix(parameter.Rows, parameter.Cols),
            _backend.CreateMatrix(parameter.Rows, parameter.Cols));
        _states.Add(parameter, state);
        return state;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MlxAdamOptimizer));
    }

    private static void ValidatePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and positive.");
    }

    private static void ValidateUnitInterval(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0.0f || value >= 1.0f)
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and in [0, 1).");
    }

    private sealed class State
    {
        public State(MlxFloatTensor m, MlxFloatTensor v)
        {
            M = m;
            V = v;
        }

        public MlxFloatTensor M { get; }
        public MlxFloatTensor V { get; }
        public int Step { get; set; }
    }
}
