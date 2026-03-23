namespace HPD.ML.DataSources.Tests;

using HPD.ML.Core;

public class EnumerableDataHandleTests
{
    private record Person(string Name, int Age);

    [Fact]
    public void Create_FromRecords_CorrectSchema()
    {
        var schema = new SchemaBuilder()
            .AddColumn("Name", new FieldType(typeof(string)))
            .AddColumn<int>("Age")
            .Build();

        var items = new[] { new Person("Alice", 30), new Person("Bob", 25) };
        var handle = EnumerableDataHandle.Create(items, schema,
            p => new Dictionary<string, object> { ["Name"] = p.Name, ["Age"] = p.Age });

        Assert.Equal(2, handle.Schema.Columns.Count);
        Assert.Equal("Name", handle.Schema.Columns[0].Name);
        Assert.Equal("Age", handle.Schema.Columns[1].Name);
    }

    [Fact]
    public void Create_FromRecords_CorrectData()
    {
        var schema = new SchemaBuilder()
            .AddColumn("Name", new FieldType(typeof(string)))
            .AddColumn<int>("Age")
            .Build();

        var items = new[] { new Person("Alice", 30), new Person("Bob", 25), new Person("Carol", 35) };
        var handle = EnumerableDataHandle.Create(items, schema,
            p => new Dictionary<string, object> { ["Name"] = p.Name, ["Age"] = p.Age });

        var names = TestFileHelper.CollectStringColumn(handle, "Name");
        var ages = TestFileHelper.CollectIntColumn(handle, "Age");
        Assert.Equal(["Alice", "Bob", "Carol"], names);
        Assert.Equal([30, 25, 35], ages);
    }

    [Fact]
    public void Create_EmptyEnumerable_ZeroRows()
    {
        var schema = new SchemaBuilder()
            .AddColumn<int>("Id")
            .Build();

        var handle = EnumerableDataHandle.Create(
            Array.Empty<int>(), schema,
            x => new Dictionary<string, object> { ["Id"] = x });

        Assert.Equal(0L, handle.RowCount);
        Assert.Single(handle.Schema.Columns);
    }
}
