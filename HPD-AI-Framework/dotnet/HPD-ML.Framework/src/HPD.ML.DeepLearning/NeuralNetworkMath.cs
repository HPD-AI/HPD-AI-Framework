namespace HPD.ML.DeepLearning;

internal static class NeuralNetworkMath
{
    public static float[] Forward(
        NeuralNetworkDefinition definition,
        IReadOnlyList<float[]> weights,
        IReadOnlyList<float[]> biases,
        ReadOnlySpan<float> input)
    {
        var current = input.ToArray();
        for (var layerIndex = 0; layerIndex < definition.Layers.Count; layerIndex++)
        {
            var layer = definition.Layers[layerIndex];
            var next = new float[layer.OutputSize];
            var w = weights[layerIndex];
            var b = biases[layerIndex];

            for (var output = 0; output < layer.OutputSize; output++)
            {
                var sum = b[output];
                for (var i = 0; i < layer.InputSize; i++)
                    sum += current[i] * w[i * layer.OutputSize + output];
                next[output] = ApplyActivation(sum, layer.Activation);
            }

            current = next;
        }

        return current;
    }

    public static float ApplyActivation(float value, ActivationKind activation)
        => activation switch
        {
            ActivationKind.Identity => value,
            ActivationKind.ReLU => MathF.Max(0.0f, value),
            _ => throw new NotSupportedException($"Unsupported activation: {activation}")
        };

    public static float ActivationDerivative(float activatedValue, ActivationKind activation)
        => activation switch
        {
            ActivationKind.Identity => 1.0f,
            ActivationKind.ReLU => activatedValue > 0.0f ? 1.0f : 0.0f,
            _ => throw new NotSupportedException($"Unsupported activation: {activation}")
        };
}
