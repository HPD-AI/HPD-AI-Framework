namespace HPD.ML.DeepLearning;

internal static class ManagedNeuralNetworkTrainer
{
    public static NeuralNetworkParameters Train(
        NeuralNetworkDefinition definition,
        float[][] features,
        float[][] labels,
        TrainingOptions options,
        int seed)
    {
        options.Validate();
        if (features.Length != labels.Length)
            throw new ArgumentException("Feature and label row counts must match.");
        if (features.Length == 0)
            throw new ArgumentException("Training data must contain at least one row.");

        var random = new Random(seed);
        var weights = InitializeWeights(definition, random);
        var biases = definition.Layers.Select(layer => new float[layer.OutputSize]).ToArray();

        for (var epoch = 0; epoch < options.Epochs; epoch++)
        {
            for (var sample = 0; sample < features.Length; sample++)
                TrainSample(definition, weights, biases, features[sample], labels[sample], options.LearningRate);
        }

        return new NeuralNetworkParameters(definition, weights, biases);
    }

    private static float[][] InitializeWeights(NeuralNetworkDefinition definition, Random random)
    {
        var weights = new float[definition.Layers.Count][];
        for (var layerIndex = 0; layerIndex < definition.Layers.Count; layerIndex++)
        {
            var layer = definition.Layers[layerIndex];
            var scale = MathF.Sqrt(2.0f / layer.InputSize);
            weights[layerIndex] = new float[layer.InputSize * layer.OutputSize];
            for (var i = 0; i < weights[layerIndex].Length; i++)
                weights[layerIndex][i] = ((float)random.NextDouble() * 2.0f - 1.0f) * scale;
        }

        return weights;
    }

    private static void TrainSample(
        NeuralNetworkDefinition definition,
        float[][] weights,
        float[][] biases,
        float[] input,
        float[] expected,
        float learningRate)
    {
        var activations = new float[definition.Layers.Count + 1][];
        activations[0] = input;

        for (var layerIndex = 0; layerIndex < definition.Layers.Count; layerIndex++)
        {
            var layer = definition.Layers[layerIndex];
            var previous = activations[layerIndex];
            var current = new float[layer.OutputSize];
            for (var output = 0; output < layer.OutputSize; output++)
            {
                var sum = biases[layerIndex][output];
                for (var i = 0; i < layer.InputSize; i++)
                    sum += previous[i] * weights[layerIndex][i * layer.OutputSize + output];
                current[output] = NeuralNetworkMath.ApplyActivation(sum, layer.Activation);
            }

            activations[layerIndex + 1] = current;
        }

        var deltas = new float[definition.Layers.Count][];
        var lastLayerIndex = definition.Layers.Count - 1;
        deltas[lastLayerIndex] = new float[definition.Layers[lastLayerIndex].OutputSize];
        for (var output = 0; output < deltas[lastLayerIndex].Length; output++)
        {
            var actual = activations[^1][output];
            deltas[lastLayerIndex][output] =
                (actual - expected[output]) *
                NeuralNetworkMath.ActivationDerivative(actual, definition.Layers[lastLayerIndex].Activation);
        }

        for (var layerIndex = lastLayerIndex - 1; layerIndex >= 0; layerIndex--)
        {
            var layer = definition.Layers[layerIndex];
            var nextLayer = definition.Layers[layerIndex + 1];
            deltas[layerIndex] = new float[layer.OutputSize];
            for (var output = 0; output < layer.OutputSize; output++)
            {
                var sum = 0.0f;
                for (var next = 0; next < nextLayer.OutputSize; next++)
                    sum += deltas[layerIndex + 1][next] * weights[layerIndex + 1][output * nextLayer.OutputSize + next];

                deltas[layerIndex][output] =
                    sum * NeuralNetworkMath.ActivationDerivative(activations[layerIndex + 1][output], layer.Activation);
            }
        }

        for (var layerIndex = 0; layerIndex < definition.Layers.Count; layerIndex++)
        {
            var layer = definition.Layers[layerIndex];
            var previous = activations[layerIndex];
            for (var output = 0; output < layer.OutputSize; output++)
            {
                for (var i = 0; i < layer.InputSize; i++)
                    weights[layerIndex][i * layer.OutputSize + output] -= learningRate * deltas[layerIndex][output] * previous[i];
                biases[layerIndex][output] -= learningRate * deltas[layerIndex][output];
            }
        }
    }
}
