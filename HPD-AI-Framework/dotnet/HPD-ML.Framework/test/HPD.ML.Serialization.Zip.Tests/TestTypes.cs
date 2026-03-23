namespace HPD.ML.Serialization.Zip.Tests;

using System.Text.Json;
using HPD.ML.Abstractions;

/// <summary>Simple test parameters for round-trip testing.</summary>
internal sealed class TestParameters : ILearnedParameters
{
    public double[] Weights { get; init; } = [];
    public double Bias { get; init; }
}

/// <summary>Test parameter writer that uses BinaryWriter/BinaryReader.</summary>
internal sealed class TestParameterWriter : IParameterWriter<TestParameters>
{
    public string TypeName => nameof(TestParameters);

    public void WriteWeights(TestParameters parameters, Stream destination)
    {
        using var writer = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(parameters.Weights.Length);
        foreach (var w in parameters.Weights)
            writer.Write(w);
        writer.Write(parameters.Bias);
    }

    public void WriteMetadata(TestParameters parameters, Stream destination, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(destination, new { parameters.Weights.Length }, options);
    }

    public TestParameters ReadModel(Stream weights, Stream metadata, JsonSerializerOptions options)
    {
        using var reader = new BinaryReader(weights, System.Text.Encoding.UTF8, leaveOpen: true);
        int count = reader.ReadInt32();
        var w = new double[count];
        for (int i = 0; i < count; i++)
            w[i] = reader.ReadDouble();
        var bias = reader.ReadDouble();
        return new TestParameters { Weights = w, Bias = bias };
    }
}

/// <summary>Simple passthrough transform for testing topology serialization.</summary>
internal sealed class TestTransform : ITransform
{
    public TransformProperties Properties => new() { PreservesRowCount = true };
    public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;
    public IDataHandle Apply(IDataHandle input) => input;
}
