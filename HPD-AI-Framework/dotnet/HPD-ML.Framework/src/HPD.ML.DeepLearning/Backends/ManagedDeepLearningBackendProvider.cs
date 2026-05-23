namespace HPD.ML.DeepLearning.Backends;

using HPD.ML.Abstractions;

public sealed class ManagedDeepLearningBackendProvider : IDeepLearningBackendProvider
{
    public bool CanHandle(BackendSpec backend)
        => string.Equals(backend.Kind, "default", StringComparison.OrdinalIgnoreCase)
           || string.Equals(backend.Kind, "cpu", StringComparison.OrdinalIgnoreCase)
           || string.Equals(backend.Kind, "managed", StringComparison.OrdinalIgnoreCase);

    public DeepLearningBackendCapabilities GetCapabilities(BackendSpec backend)
        => DeepLearningBackendCapabilities.ManagedCpu;

    public IDeepLearningTrainer CreateTrainer(DeepLearningBackendContext context)
        => new ManagedDeepLearningTrainer();
}

internal sealed class ManagedDeepLearningTrainer : IDeepLearningTrainer
{
    public NeuralNetworkParameters Train(
        NeuralNetworkDefinition definition,
        float[][] features,
        float[][] labels,
        TrainingOptions options,
        int seed)
        => ManagedNeuralNetworkTrainer.Train(definition, features, labels, options, seed);
}
