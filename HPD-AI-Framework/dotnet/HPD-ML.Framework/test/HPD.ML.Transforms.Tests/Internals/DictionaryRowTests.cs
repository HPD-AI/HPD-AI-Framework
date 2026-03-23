namespace HPD.ML.Transforms.Tests;

using HPD.ML.Core;

public class DictionaryRowTests
{
    private static DictionaryRow MakeRow(Dictionary<string, object> values)
    {
        var builder = new SchemaBuilder();
        foreach (var (k, v) in values)
            builder.AddColumn(k, new FieldType(v.GetType()));
        return new DictionaryRow(builder.Build(), values);
    }

    [Fact]
    public void DictRow_GetValue_Int()
    {
        var row = MakeRow(new() { ["V"] = 42 });
        Assert.Equal(42, row.GetValue<int>("V"));
    }

    [Fact]
    public void DictRow_GetValue_Float()
    {
        var row = MakeRow(new() { ["V"] = 3.14f });
        Assert.Equal(3.14f, row.GetValue<float>("V"), 0.001f);
    }

    [Fact]
    public void DictRow_GetValue_String()
    {
        var row = MakeRow(new() { ["V"] = "hello" });
        Assert.Equal("hello", row.GetValue<string>("V"));
    }

    [Fact]
    public void DictRow_GetValue_NumericWidening()
    {
        var row = MakeRow(new() { ["V"] = 42 });
        Assert.Equal(42f, row.GetValue<float>("V"), 0.001f);
    }

    [Fact]
    public void DictRow_MissingColumn_Throws()
    {
        var row = MakeRow(new() { ["V"] = 1 });
        Assert.Throws<KeyNotFoundException>(() => row.GetValue<int>("X"));
    }
}
