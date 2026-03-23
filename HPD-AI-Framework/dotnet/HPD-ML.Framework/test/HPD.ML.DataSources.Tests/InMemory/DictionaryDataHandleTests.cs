namespace HPD.ML.DataSources.Tests;

using HPD.ML.Core;

public class DictionaryDataHandleTests
{
    [Fact]
    public void Create_InfersSchema_FromAllRows()
    {
        var rows = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["A"] = 1 },
            new Dictionary<string, object> { ["A"] = 2, ["B"] = "x" }
        };

        var handle = DictionaryDataHandle.Create(rows);

        Assert.NotNull(handle.Schema.FindByName("A"));
        Assert.NotNull(handle.Schema.FindByName("B"));
    }

    [Fact]
    public void Create_InfersSchema_WidensTypes()
    {
        var rows = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["v"] = 1 },
            new Dictionary<string, object> { ["v"] = 1.5 }
        };

        var handle = DictionaryDataHandle.Create(rows);

        Assert.Equal(typeof(double), handle.Schema.FindByName("v")!.Type.ClrType);
    }

    [Fact]
    public void Create_WithExplicitSchema()
    {
        var schema = new SchemaBuilder()
            .AddColumn<float>("V")
            .Build();

        var rows = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["V"] = 1.0f },
            new Dictionary<string, object> { ["V"] = 2.0f }
        };

        var handle = DictionaryDataHandle.Create(rows, schema);

        Assert.Equal(typeof(float), handle.Schema.Columns[0].Type.ClrType);
        Assert.Equal(2L, handle.RowCount);
    }

    [Fact]
    public void Create_MissingKeysInLaterRows_DefaultFilled()
    {
        var rows = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["A"] = 1, ["B"] = 2 },
            new Dictionary<string, object> { ["A"] = 3 } // B missing
        };

        var handle = DictionaryDataHandle.Create(rows);

        var bValues = TestFileHelper.CollectIntColumn(handle, "B");
        Assert.Equal(2, bValues[0]);
        Assert.Equal(0, bValues[1]); // default(int)
    }

    [Fact]
    public void Create_EmptyRows_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DictionaryDataHandle.Create(Array.Empty<IReadOnlyDictionary<string, object>>()));
    }

    [Fact]
    public void Create_SingleRow_Works()
    {
        var rows = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["X"] = 42 }
        };

        var handle = DictionaryDataHandle.Create(rows);

        Assert.Equal(1L, handle.RowCount);
        var xs = TestFileHelper.CollectIntColumn(handle, "X");
        Assert.Equal([42], xs);
    }

    [Fact]
    public void Create_CorrectData_Cursor()
    {
        var rows = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["Id"] = 1, ["Name"] = "Alice" },
            new Dictionary<string, object> { ["Id"] = 2, ["Name"] = "Bob" },
            new Dictionary<string, object> { ["Id"] = 3, ["Name"] = "Carol" }
        };

        var handle = DictionaryDataHandle.Create(rows);

        var ids = TestFileHelper.CollectIntColumn(handle, "Id");
        var names = TestFileHelper.CollectStringColumn(handle, "Name");
        Assert.Equal([1, 2, 3], ids);
        Assert.Equal(["Alice", "Bob", "Carol"], names);
    }
}
