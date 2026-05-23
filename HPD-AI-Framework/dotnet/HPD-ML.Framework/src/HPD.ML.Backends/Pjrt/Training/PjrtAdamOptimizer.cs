namespace HPD.ML.Backends.Pjrt.Training;

public sealed class PjrtAdamOptimizer : IPjrtOptimizer, IDisposable
{
    private readonly Dictionary<PjrtFloatTensor, State> _states = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public PjrtAdamOptimizer(
        float learningRate = 0.001f,
        float beta1 = 0.9f,
        float beta2 = 0.999f,
        float epsilon = 1e-8f,
        float weightDecay = 0.0f)
    {
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

    public void Step(PjrtFloatTensor parameter, PjrtFloatTensor gradient)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(gradient);
        if (parameter.Rows != gradient.Rows || parameter.Cols != gradient.Cols)
            throw new ArgumentException("Gradient shape must match parameter shape.", nameof(gradient));

        var parameterData = parameter.ToArray();
        var gradientData = gradient.ToArray();
        var state = GetState(parameter, parameterData.Length);
        state.Step++;
        var biasCorrection1 = 1.0f - MathF.Pow(Beta1, state.Step);
        var biasCorrection2 = 1.0f - MathF.Pow(Beta2, state.Step);

        for (var i = 0; i < parameterData.Length; i++)
        {
            var g = WeightDecay == 0.0f ? gradientData[i] : gradientData[i] + WeightDecay * parameterData[i];
            state.M[i] = Beta1 * state.M[i] + (1.0f - Beta1) * g;
            state.V[i] = Beta2 * state.V[i] + (1.0f - Beta2) * g * g;
            parameterData[i] -= LearningRate * (state.M[i] / biasCorrection1) / (MathF.Sqrt(state.V[i] / biasCorrection2) + Epsilon);
        }

        parameter.UpdateFromSpan(parameterData);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _states.Clear();
    }

    private State GetState(PjrtFloatTensor parameter, int length)
    {
        if (_states.TryGetValue(parameter, out var state))
            return state;

        state = new State(new float[length], new float[length]);
        _states.Add(parameter, state);
        return state;
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

    private sealed class State(float[] m, float[] v)
    {
        public float[] M { get; } = m;
        public float[] V { get; } = v;
        public int Step { get; set; }
    }
}
