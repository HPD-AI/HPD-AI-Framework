namespace HPD.ML.DeepLearning.Backends;

public static class DeepLearningBackendCompatibility
{
    public static void Validate(
        NeuralNetworkDefinition definition,
        DeepLearningBackendCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (!capabilities.SupportsTraining)
            throw new NotSupportedException($"Backend '{capabilities.Name}' does not support training.");
        if (!capabilities.SupportsFloat32)
            throw new NotSupportedException($"Backend '{capabilities.Name}' does not support float32 neural network training.");

        foreach (var activation in definition.Layers.Select(layer => layer.Activation).Distinct())
        {
            if (!capabilities.SupportsActivation(activation))
            {
                throw new NotSupportedException(
                    $"Backend '{capabilities.Name}' does not support activation '{activation}'.");
            }
        }
    }
}
