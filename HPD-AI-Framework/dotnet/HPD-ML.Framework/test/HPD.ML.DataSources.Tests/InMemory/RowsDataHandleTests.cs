namespace HPD.ML.DataSources.Tests;

using HPD.ML.Core;

public class RowsDataHandleTests
{
    [Fact]
    public void Create_CorrectSchema()
    {
        var schema = new SchemaBuilder()
            .AddColumn<float>("X")
            .AddColumn<float>("Y")
            .Build();

        var handle = RowsDataHandle.Create(schema,
            new Dictionary<string, object> { ["X"] = 1.0f, ["Y"] = 2.0f });

        Assert.Equal(2, handle.Schema.Columns.Count);
        Assert.Equal("X", handle.Schema.Columns[0].Name);
        Assert.Equal("Y", handle.Schema.Columns[1].Name);
    }

    [Fact]
    public void Create_CorrectRowCount()
    {
        var schema = new SchemaBuilder().AddColumn<int>("V").Build();

        var handle = RowsDataHandle.Create(schema,
            new Dictionary<string, object> { ["V"] = 1 },
            new Dictionary<string, object> { ["V"] = 2 },
            new Dictionary<string, object> { ["V"] = 3 });

        Assert.Equal(3L, handle.RowCount);
    }

    [Fact]
    public void Create_CorrectData()
    {
        var schema = new SchemaBuilder()
            .AddColumn<int>("Id")
            .AddColumn("Name", new FieldType(typeof(string)))
            .Build();

        var handle = RowsDataHandle.Create(schema,
            new Dictionary<string, object> { ["Id"] = 1, ["Name"] = "Alice" },
            new Dictionary<string, object> { ["Id"] = 2, ["Name"] = "Bob" });

        var ids = TestFileHelper.CollectIntColumn(handle, "Id");
        var names = TestFileHelper.CollectStringColumn(handle, "Name");
        Assert.Equal([1, 2], ids);
        Assert.Equal(["Alice", "Bob"], names);
    }

    [Fact]
    public void Create_EmptySpan_ZeroRows()
    {
        var schema = new SchemaBuilder().AddColumn<int>("V").Build();
        var handle = RowsDataHandle.Create(schema);

        Assert.Equal(0L, handle.RowCount);
    }
}
