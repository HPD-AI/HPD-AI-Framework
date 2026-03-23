using System.Text.Json;
using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Serialization;
using Xunit;

namespace HPD.RAG.Core.Tests;

/// <summary>T-017 through T-029: Filter AST tests.</summary>
public class FilterAstTests
{
    // T-017
    [Fact]
    public void MragFilter_Eq_String_ProducesCorrectOp()
    {
        var node = MragFilter.Eq("category", "Technical");

        Assert.Equal("eq", node.Op);
        Assert.Equal("category", node.Property);
        Assert.NotNull(node.Value);
        Assert.Equal("Technical", node.Value.Value.GetString());
    }

    // T-018
    [Fact]
    public void MragFilter_Eq_Int_RoundtripsValue()
    {
        var node = MragFilter.Eq("count", 42);

        Assert.NotNull(node.Value);
        Assert.Equal(42, node.Value.Value.GetInt32());
    }

    // T-019
    [Fact]
    public void MragFilter_Eq_Double_RoundtripsValue()
    {
        var node = MragFilter.Eq("score", 0.95);

        Assert.NotNull(node.Value);
        Assert.Equal(0.95, node.Value.Value.GetDouble());
    }

    // T-020
    [Fact]
    public void MragFilter_Eq_Bool_RoundtripsValue()
    {
        var node = MragFilter.Eq("active", true);

        Assert.NotNull(node.Value);
        Assert.True(node.Value.Value.GetBoolean());
    }

    // T-021
    [Fact]
    public void MragFilter_Eq_DateTimeOffset_SerializesAsIso8601()
    {
        var date = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var node = MragFilter.Eq("date", date);

        Assert.NotNull(node.Value);
        var str = node.Value.Value.GetString();
        Assert.NotNull(str);
        Assert.StartsWith("2025-01-01T00:00:00", str);
    }

    // T-022
    [Fact]
    public void MragFilter_Tag_ProducesTagPrefixedProperty()
    {
        var node = MragFilter.Tag("userId", "user-123");

        Assert.Equal("tag:userId", node.Property);
        Assert.Equal("eq", node.Op);
        Assert.NotNull(node.Value);
        Assert.Equal("user-123", node.Value.Value.GetString());
    }

    // T-023
    [Fact]
    public void MragFilter_And_ProducesChildrenArray()
    {
        var node = MragFilter.And(MragFilter.Eq("a", "1"), MragFilter.Eq("b", "2"));

        Assert.Equal("and", node.Op);
        Assert.NotNull(node.Children);
        Assert.Equal(2, node.Children.Length);
        Assert.Null(node.Property);
    }

    // T-024
    [Fact]
    public void MragFilter_Or_ProducesChildrenArray()
    {
        var node = MragFilter.Or(MragFilter.Eq("x", "1"), MragFilter.Eq("y", "2"));

        Assert.Equal("or", node.Op);
        Assert.NotNull(node.Children);
        Assert.Equal(2, node.Children.Length);
    }

    // T-025
    [Fact]
    public void MragFilter_Not_ProducesSingleChild()
    {
        var node = MragFilter.Not(MragFilter.Eq("x", "y"));

        Assert.Equal("not", node.Op);
        Assert.NotNull(node.Children);
        Assert.Single(node.Children);
    }

    // T-026
    [Fact]
    public void MragFilter_CompoundNested_RoundtripsThroughJson()
    {
        var filter = MragFilter.And(
            MragFilter.Tag("userId", "u"),
            MragFilter.Gt("score", 0.8),
            MragFilter.Or(
                MragFilter.Eq("type", "a"),
                MragFilter.Eq("type", "b")));

        var json = JsonSerializer.Serialize(filter, MragJsonSerializerContext.Shared.MragFilterNode);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragFilterNode);

        Assert.NotNull(deserialized);
        Assert.Equal("and", deserialized.Op);
        Assert.NotNull(deserialized.Children);
        Assert.Equal(3, deserialized.Children.Length);

        var orChild = deserialized.Children[2];
        Assert.Equal("or", orChild.Op);
        Assert.NotNull(orChild.Children);
        Assert.Equal(2, orChild.Children.Length);
    }

    // T-027
    [Fact]
    public void MragFilterNode_Roundtrips_ThroughMragJsonSerializerContext()
    {
        var filter = MragFilter.And(
            MragFilter.Eq("category", "tech"),
            MragFilter.Not(MragFilter.Eq("deleted", true)));

        var json = JsonSerializer.Serialize(filter, MragJsonSerializerContext.Shared.MragFilterNode);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragFilterNode);

        Assert.NotNull(deserialized);
        Assert.Equal("and", deserialized.Op);
        Assert.NotNull(deserialized.Children);
        Assert.Equal(2, deserialized.Children.Length);
        Assert.Equal("eq", deserialized.Children[0].Op);
        Assert.Equal("category", deserialized.Children[0].Property);
        Assert.Equal("not", deserialized.Children[1].Op);
        Assert.NotNull(deserialized.Children[1].Children);
        Assert.Single(deserialized.Children[1].Children!);
    }

    // T-028
    public static IEnumerable<object[]> AllLeafOpsData =>
    [
        ["eq", (Func<MragFilterNode>)(() => MragFilter.Eq("p", "v"))],
        ["neq", (Func<MragFilterNode>)(() => MragFilter.Neq("p", "v"))],
        ["gt", (Func<MragFilterNode>)(() => MragFilter.Gt("p", 1.0))],
        ["gte", (Func<MragFilterNode>)(() => MragFilter.Gte("p", 1.0))],
        ["lt", (Func<MragFilterNode>)(() => MragFilter.Lt("p", 1.0))],
        ["lte", (Func<MragFilterNode>)(() => MragFilter.Lte("p", 1.0))],
        ["contains", (Func<MragFilterNode>)(() => MragFilter.Contains("p", "v"))],
        ["startswith", (Func<MragFilterNode>)(() => MragFilter.StartsWith("p", "v"))],
    ];

    [Theory]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("gt")]
    [InlineData("gte")]
    [InlineData("lt")]
    [InlineData("lte")]
    [InlineData("contains")]
    [InlineData("startswith")]
    public void MragFilter_AllLeafOps_ProduceCorrectOpStrings(string expectedOp)
    {
        MragFilterNode node = expectedOp switch
        {
            "eq" => MragFilter.Eq("p", "v"),
            "neq" => MragFilter.Neq("p", "v"),
            "gt" => MragFilter.Gt("p", 1.0),
            "gte" => MragFilter.Gte("p", 1.0),
            "lt" => MragFilter.Lt("p", 1.0),
            "lte" => MragFilter.Lte("p", 1.0),
            "contains" => MragFilter.Contains("p", "v"),
            "startswith" => MragFilter.StartsWith("p", "v"),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedOp))
        };

        Assert.Equal(expectedOp, node.Op);
    }

    // T-029
    [Fact]
    public void MragFilter_Eq_CanonicalJson_IsStableAcrossTwoCalls()
    {
        var filter = MragFilter.Eq("x", "v");

        var json1 = JsonSerializer.Serialize(filter, MragJsonSerializerContext.Shared.MragFilterNode);
        var json2 = JsonSerializer.Serialize(filter, MragJsonSerializerContext.Shared.MragFilterNode);

        Assert.Equal(json1, json2);
    }
}
