using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Serialization;

public sealed class PrimitiveJsonConverterTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"bad token\"")]
    [InlineData("\"bad\\nrevision\"")]
    public void RevisionTokenConverterRejectsNoncanonicalValues(string json)
    {
        ((Action)(() => JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.RevisionToken)))
            .Should().Throw<JsonException>();
    }

    [Fact]
    public void RevisionTokenRejectsNonNfcMalformedAndOversizedValues()
    {
        ((Action)(() => new RevisionToken("e\u0301"))).Should().Throw<ArgumentException>();
        ((Action)(() => new RevisionToken("\ud800"))).Should().Throw<ArgumentException>();
        ((Action)(() => new RevisionToken(new string('x', RevisionToken.MaximumUtf8Bytes + 1)))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordIdSerializesAsString()
    {
        var json = JsonSerializer.Serialize(RecordId.Create("rec_1"), HPDBaseJsonSerializerContext.Default.RecordId);

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
