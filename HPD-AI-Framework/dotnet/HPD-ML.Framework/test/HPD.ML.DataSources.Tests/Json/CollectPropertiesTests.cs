namespace HPD.ML.DataSources.Tests;

using System.Text.Json;

public class CollectPropertiesTests
{
    [Fact]
    public void FlatObject_CollectsAll()
    {
        using var doc = JsonDocument.Parse("{\"a\":1,\"b\":\"x\"}");
        var props = new Dictionary<string, Type>();
        JsonDataHandle.CollectProperties(doc.RootElement, props, "", 1, 0);

        Assert.Equal(typeof(int), props["a"]);
        Assert.Equal(typeof(string), props["b"]);
    }

    [Fact]
    public void NestedObject_FlattensWithDot()
    {
        using var doc = JsonDocument.Parse("{\"a\":{\"b\":1}}");
        var props = new Dictionary<string, Type>();
        JsonDataHandle.CollectProperties(doc.RootElement, props, "", 1, 0);

        Assert.True(props.ContainsKey("a.b"));
        Assert.Equal(typeof(int), props["a.b"]);
    }

    [Fact]
    public void NestedObject_StopsAtMaxDepth()
    {
        using var doc = JsonDocument.Parse("{\"a\":{\"b\":{\"c\":1}}}");
        var props = new Dictionary<string, Type>();
        JsonDataHandle.CollectProperties(doc.RootElement, props, "", 1, 0);

        // depth=1: "a" is flattened into "a.b", but "a.b" is an object at depth 1
        // so "a.b" should be treated as string (object beyond max depth)
        Assert.True(props.ContainsKey("a.b"));
        Assert.Equal(typeof(string), props["a.b"]);
    }

    [Fact]
    public void BooleanValues_DetectedCorrectly()
    {
        using var doc = JsonDocument.Parse("{\"flag\":true}");
        var props = new Dictionary<string, Type>();
        JsonDataHandle.CollectProperties(doc.RootElement, props, "", 1, 0);

        Assert.Equal(typeof(bool), props["flag"]);
    }
}
