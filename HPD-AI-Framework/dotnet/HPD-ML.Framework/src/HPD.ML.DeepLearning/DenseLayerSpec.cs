namespace HPD.ML.DeepLearning;

public sealed record DenseLayerSpec
{
    public DenseLayerSpec(int inputSize, int outputSize, ActivationKind activation = ActivationKind.Identity)
    {
        if (inputSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputSize), "Input size must be positive.");
        if (outputSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputSize), "Output size must be positive.");

        InputSize = inputSize;
        OutputSize = outputSize;
        Activation = activation;
    }

    public int InputSize { get; }
    public int OutputSize { get; }
    public ActivationKind Activation { get; }
}
