using System.Text.Json;
using HPD.Base;
using HPD.Base.Serialization;

namespace HPD.Base.Abstractions.Tests.Serialization;

public sealed class PrimitiveJsonConverterTests
{
    [Fact]
    public void RecordIdSerializesAsString()
    {
        var json = JsonSerializer.Serialize(new RecordId("rec_1"), HPDBaseJsonSerializerContext.Default.RecordId);

        Assert.Equal("\"rec_1\"", json);
    }

    [Fact]
    public void RevisionTokenSerializesAsString()
    {
        var json = JsonSerializer.Serialize(new RevisionToken("rev_1"), HPDBaseJsonSerializerContext.Default.RevisionToken);

        Assert.Equal("\"rev_1\"", json);
    }

    [Fact]
    public void RecordIdRejectsObjectShape()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"value\":\"rec_1\"}", HPDBaseJsonSerializerContext.Default.RecordId));
    }
}
