namespace HPD.ML.DeepLearning.Backends;

using HPD.ML.Abstractions;
using HPD.ML.DeepLearning;
using Helium.Train;

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
    {
        var layerSizes = definition.Layers.Select(l => l.OutputSize).ToArray();
        using var backend = new ManagedTensorBackend();
        var trainer = new Trainer(backend);
        var (weights, biases) = trainer.Train(
            layerSizes, features, labels,
            options.LearningRate, options.Epochs, options.BatchSize, seed);
        return new NeuralNetworkParameters(definition, weights, biases);
    }
}
