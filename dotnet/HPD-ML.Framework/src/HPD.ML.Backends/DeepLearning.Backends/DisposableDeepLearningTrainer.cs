namespace HPD.ML.DeepLearning.Backends;

using HPD.ML.DeepLearning;

internal sealed class DisposableDeepLearningTrainer : IDeepLearningTrainer, IDisposable
{
    private readonly IDeepLearningTrainer _inner;
    private readonly IDisposable _backend;
    private bool _disposed;

    public DisposableDeepLearningTrainer(IDeepLearningTrainer inner, IDisposable backend)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public NeuralNetworkParameters Train(
        NeuralNetworkDefinition definition,
        float[][] features,
        float[][] labels,
        TrainingOptions options,
        int seed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _inner.Train(definition, features, labels, options, seed);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _backend.Dispose();
    }
}
