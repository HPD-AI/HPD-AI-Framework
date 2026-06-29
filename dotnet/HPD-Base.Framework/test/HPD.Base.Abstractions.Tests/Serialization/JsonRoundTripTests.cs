using System.Text.Json;
using HPD.Base.Descriptors;
using HPD.Base.Query;
using HPD.Base.Serialization;

namespace HPD.Base.Abstractions.Tests.Serialization;

public sealed class JsonRoundTripTests
{
    [Fact]
    public void EnumsSerializeAsLowerCamelStrings()
    {
        var json = JsonSerializer.Serialize(
            new ManifestLinkDescriptor
            {
                Rel = ManifestLinkKind.Capabilities,
                Href = "/base/capabilities",
                ResponseDtoId = BaseDtoIds.CapabilityDescriptor
            },
            HPDBaseJsonSerializerContext.Default.ManifestLinkDescriptor);

        Assert.Contains("\"rel\":\"capabilities\"", json);
        Assert.DoesNotContain("\"rel\":1", json);
    }

    [Fact]
    public void QueryValueDecimalRoundTripsAsString()
    {
        var value = new QueryValue { Kind = QueryValueKind.Decimal, Decimal = "12.3400" };
        var json = JsonSerializer.Serialize(value, HPDBaseJsonSerializerContext.Default.QueryValue);
        var roundTrip = JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.QueryValue);

        Assert.Equal("12.3400", roundTrip!.Decimal);
    }
}
