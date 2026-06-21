namespace HPD.ML.DeepLearning;

public sealed record NeuralNetworkDefinition
{
    public NeuralNetworkDefinition(
        string featureColumn,
        string labelColumn,
        IReadOnlyList<DenseLayerSpec> layers)
    {
        if (string.IsNullOrWhiteSpace(featureColumn))
            throw new ArgumentException("Feature column is required.", nameof(featureColumn));
        if (string.IsNullOrWhiteSpace(labelColumn))
            throw new ArgumentException("Label column is required.", nameof(labelColumn));
        if (layers.Count == 0)
            throw new ArgumentException("At least one layer is required.", nameof(layers));

        for (var i = 1; i < layers.Count; i++)
        {
            if (layers[i - 1].OutputSize != layers[i].InputSize)
            {
                throw new ArgumentException(
                    $"Layer {i} input size must equal prior layer output size.",
                    nameof(layers));
            }
        }

        FeatureColumn = featureColumn;
        LabelColumn = labelColumn;
        Layers = [.. layers];
    }

    public string FeatureColumn { get; }
    public string LabelColumn { get; }
    public IReadOnlyList<DenseLayerSpec> Layers { get; }
}
