namespace HPD.ML.Serialization.Zip;

using HPD.ML.DeepLearning;

internal sealed class NeuralNetworkParameterMetadata
{
    public string FeatureColumn { get; set; } = string.Empty;
    public string LabelColumn { get; set; } = string.Empty;
    public NeuralNetworkLayerMetadata[] Layers { get; set; } = [];
}

internal sealed class NeuralNetworkLayerMetadata
{
    public int InputSize { get; set; }
    public int OutputSize { get; set; }
    public ActivationKind Activation { get; set; }
    public int WeightCount { get; set; }
    public int BiasCount { get; set; }
}
