namespace HPD.ML.Backends.Pjrt.Training;

public sealed class PjrtSgdOptimizer : IPjrtOptimizer
{
    private readonly PjrtFloatBackend _backend;

    public PjrtSgdOptimizer(PjrtFloatBackend backend, float learningRate)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (!float.IsFinite(learningRate) || learningRate <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(learningRate), "Learning rate must be finite and positive.");

        LearningRate = learningRate;
    }

    public float LearningRate { get; }

    public void Step(PjrtFloatTensor parameter, PjrtFloatTensor gradient)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(gradient);
        if (parameter.Rows != gradient.Rows || parameter.Cols != gradient.Cols)
            throw new ArgumentException("Gradient shape must match parameter shape.", nameof(gradient));

        using var scaledGradient = _backend.Scale(gradient, LearningRate);
        using var updated = _backend.Subtract(parameter, scaledGradient);
        parameter.UpdateFromSpan(updated.ToArray());
    }
}
