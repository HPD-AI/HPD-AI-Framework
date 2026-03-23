namespace HPD.ML.Serialization.Zip.Tests;

using System.Text.Json;

public class TestParameterWriterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void WriteAndRead_RoundTrips()
    {
        var writer = new TestParameterWriter();
        var original = new TestParameters { Weights = [1.5, -2.3, 0.0], Bias = 7.7 };

        using var weightsStream = new MemoryStream();
        using var metadataStream = new MemoryStream();

        writer.WriteWeights(original, weightsStream);
        writer.WriteMetadata(original, metadataStream, JsonOptions);

        weightsStream.Position = 0;
        metadataStream.Position = 0;

        var loaded = writer.ReadModel(weightsStream, metadataStream, JsonOptions);
        Assert.Equal(original.Weights, loaded.Weights);
        Assert.Equal(original.Bias, loaded.Bias);
    }

    [Fact]
    public void TypeName_IsCorrect()
    {
        var writer = new TestParameterWriter();
        Assert.Equal(nameof(TestParameters), writer.TypeName);
    }
}
