namespace HPD.ML.Serialization.Zip;

using System.Text;
using System.Text.Json;
using HPD.ML.DeepLearning;

public sealed class NeuralNetworkParameterWriter : IParameterWriter<NeuralNetworkParameters>
{
    public string TypeName => nameof(NeuralNetworkParameters);

    public void WriteWeights(NeuralNetworkParameters parameters, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(destination);

        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
        writer.Write(parameters.Weights.Count);

        for (var i = 0; i < parameters.Weights.Count; i++)
        {
            WriteFloatArray(writer, parameters.Weights[i]);
            WriteFloatArray(writer, parameters.Biases[i]);
        }
    }

    public void WriteMetadata(NeuralNetworkParameters parameters, Stream destination, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        var metadata = new NeuralNetworkParameterMetadata
        {
            FeatureColumn = parameters.Definition.FeatureColumn,
            LabelColumn = parameters.Definition.LabelColumn,
            Layers = parameters.Definition.Layers.Select((layer, index) => new NeuralNetworkLayerMetadata
            {
                InputSize = layer.InputSize,
                OutputSize = layer.OutputSize,
                Activation = layer.Activation,
                WeightCount = parameters.Weights[index].Length,
                BiasCount = parameters.Biases[index].Length
            }).ToArray()
        };

        JsonSerializer.Serialize(destination, metadata, options);
    }

    public NeuralNetworkParameters ReadModel(Stream weights, Stream metadata, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);

        var decoded = JsonSerializer.Deserialize<NeuralNetworkParameterMetadata>(metadata, options)
            ?? throw new InvalidOperationException("Failed to deserialize neural network parameter metadata.");

        if (decoded.Layers.Length == 0)
            throw new InvalidOperationException("Neural network parameter metadata contains no layers.");

        var layers = decoded.Layers
            .Select(layer => new DenseLayerSpec(layer.InputSize, layer.OutputSize, layer.Activation))
            .ToArray();
        var definition = new NeuralNetworkDefinition(decoded.FeatureColumn, decoded.LabelColumn, layers);

        using var reader = new BinaryReader(weights, Encoding.UTF8, leaveOpen: true);
        var layerCount = reader.ReadInt32();
        if (layerCount != decoded.Layers.Length)
        {
            throw new InvalidOperationException(
                $"Neural network weight stream contains {layerCount} layers, but metadata contains {decoded.Layers.Length}.");
        }

        var layerWeights = new float[layerCount][];
        var layerBiases = new float[layerCount][];
        for (var i = 0; i < layerCount; i++)
        {
            layerWeights[i] = ReadFloatArray(reader, decoded.Layers[i].WeightCount);
            layerBiases[i] = ReadFloatArray(reader, decoded.Layers[i].BiasCount);
        }

        return new NeuralNetworkParameters(definition, layerWeights, layerBiases);
    }

    private static void WriteFloatArray(BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        for (var i = 0; i < values.Length; i++)
            writer.Write(values[i]);
    }

    private static float[] ReadFloatArray(BinaryReader reader, int expectedCount)
    {
        var count = reader.ReadInt32();
        if (count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Neural network weight stream array length {count} does not match metadata length {expectedCount}.");
        }

        var values = new float[count];
        for (var i = 0; i < values.Length; i++)
            values[i] = reader.ReadSingle();
        return values;
    }
}
