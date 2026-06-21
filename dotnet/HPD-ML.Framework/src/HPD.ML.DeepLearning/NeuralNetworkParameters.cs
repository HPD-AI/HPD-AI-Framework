namespace HPD.ML.DeepLearning;

using HPD.ML.Abstractions;

public sealed class NeuralNetworkParameters : ILearnedParameters
{
    public NeuralNetworkParameters(
        NeuralNetworkDefinition definition,
        IReadOnlyList<float[]> weights,
        IReadOnlyList<float[]> biases)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (weights.Count != definition.Layers.Count)
            throw new ArgumentException("Weight array count must match layer count.", nameof(weights));
        if (biases.Count != definition.Layers.Count)
            throw new ArgumentException("Bias array count must match layer count.", nameof(biases));

        var copiedWeights = new float[weights.Count][];
        var copiedBiases = new float[biases.Count][];
        for (var i = 0; i < definition.Layers.Count; i++)
        {
            var layer = definition.Layers[i];
            if (weights[i].Length != layer.InputSize * layer.OutputSize)
                throw new ArgumentException($"Layer {i} weight length is invalid.", nameof(weights));
            if (biases[i].Length != layer.OutputSize)
                throw new ArgumentException($"Layer {i} bias length is invalid.", nameof(biases));

            copiedWeights[i] = [.. weights[i]];
            copiedBiases[i] = [.. biases[i]];
        }

        Weights = copiedWeights;
        Biases = copiedBiases;
    }

    public NeuralNetworkDefinition Definition { get; }
    public IReadOnlyList<float[]> Weights { get; }
    public IReadOnlyList<float[]> Biases { get; }
}
