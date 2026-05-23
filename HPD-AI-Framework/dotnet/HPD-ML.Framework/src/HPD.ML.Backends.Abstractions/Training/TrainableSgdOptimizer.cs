namespace HPD.ML.Backends.Abstractions.Training;


public sealed class TrainableSgdOptimizer<TTensor, TVariable, TTape> : ITrainableOptimizer<TTensor>
    where TTensor : class, IDisposable
    where TTape : IDisposable
{
    private readonly ITrainableTensorBackend<TTensor, TVariable, TTape> _backend;

    public TrainableSgdOptimizer(ITrainableTensorBackend<TTensor, TVariable, TTape> backend, float learningRate)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (!float.IsFinite(learningRate) || learningRate <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(learningRate), "Learning rate must be finite and positive.");

        LearningRate = learningRate;
    }

    public float LearningRate { get; }

    public void Step(TTensor parameter, TTensor gradient)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(gradient);

        using var scaledGradient = _backend.Scale(gradient, LearningRate);
        using var updated = _backend.Subtract(parameter, scaledGradient);
        _backend.Update(parameter, _backend.ToArray(updated));
    }
}
