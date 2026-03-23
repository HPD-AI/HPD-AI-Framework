namespace HPD.ML.DataSources.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class CsvDataHandleTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string CreateCsv(string content)
    {
        var path = TestFileHelper.WriteTempFile(content, ".csv");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void Create_InfersSchema_IntFloatString()
    {
        var path = CreateCsv("Id,Score,Name\n1,3.5,Alice\n2,4.0,Bob");
        var handle = CsvDataHandle.Create(path);

        Assert.Equal(3, handle.Schema.Columns.Count);
        Assert.Equal(typeof(int), handle.Schema.Columns[0].Type.ClrType);
        Assert.Equal(typeof(double), handle.Schema.Columns[1].Type.ClrType);
        Assert.Equal(typeof(string), handle.Schema.Columns[2].Type.ClrType);
    }

    [Fact]
    public void Create_InfersSchema_AllStrings()
    {
        var path = CreateCsv("A,B\nhello,world\nfoo,bar");
        var handle = CsvDataHandle.Create(path);

        Assert.Equal(typeof(string), handle.Schema.Columns[0].Type.ClrType);
        Assert.Equal(typeof(string), handle.Schema.Columns[1].Type.ClrType);
    }

    [Fact]
    public void Create_InfersSchema_BoolColumn()
    {
        var path = CreateCsv("Flag\ntrue\nfalse\ntrue");
        var handle = CsvDataHandle.Create(path);

        Assert.Equal(typeof(bool), handle.Schema.Columns[0].Type.ClrType);
    }

    [Fact]
    public void Create_InfersSchema_WidensIntToLong()
    {
        var path = CreateCsv($"V\n1\n{long.MaxValue}");
        var handle = CsvDataHandle.Create(path);

        Assert.Equal(typeof(long), handle.Schema.Columns[0].Type.ClrType);
    }

    [Fact]
    public void Create_InfersSchema_WidensIntToDouble()
    {
        var path = CreateCsv("V\n1\n3.14");
        var handle = CsvDataHandle.Create(path);

        Assert.Equal(typeof(double), handle.Schema.Columns[0].Type.ClrType);
    }

    [Fact]
    public void Create_WithExplicitSchema_UsesProvided()
    {
        var path = CreateCsv("A,B\n1,2\n3,4");
        var schema = new SchemaBuilder()
            .AddColumn<float>("A")
            .AddColumn<float>("B")
            .Build();
        var handle = CsvDataHandle.Create(path, schema);

        Assert.Equal(typeof(float), handle.Schema.Columns[0].Type.ClrType);
        Assert.Equal(typeof(float), handle.Schema.Columns[1].Type.ClrType);
    }

    [Fact]
    public void Create_WithTypeHints_OverridesInference()
    {
        var path = CreateCsv("Age\n25\n30");
        var handle = CsvDataHandle.Create(path, new CsvOptions
        {
            TypeHints = new Dictionary<string, Type> { ["Age"] = typeof(float) }
        });

        Assert.Equal(typeof(float), handle.Schema.Columns[0].Type.ClrType);
    }

    [Fact]
    public void Create_NoHeader_GeneratesColumnNames()
    {
        var path = CreateCsv("1,hello,true");
        var handle = CsvDataHandle.Create(path, new CsvOptions { HasHeader = false });

        Assert.Equal("Column0", handle.Schema.Columns[0].Name);
        Assert.Equal("Column1", handle.Schema.Columns[1].Name);
        Assert.Equal("Column2", handle.Schema.Columns[2].Name);
    }

    [Fact]
    public void Cursor_ReadsAllRows()
    {
        var path = CreateCsv("Id\n0\n1\n2\n3\n4");
        var handle = CsvDataHandle.Create(path);

        var ids = TestFileHelper.CollectIntColumn(handle, "Id");
        Assert.Equal([0, 1, 2, 3, 4], ids);
    }

    [Fact]
    public void Cursor_SkipRows()
    {
        var path = CreateCsv("Id\n0\n1\n2\n3\n4");
        var handle = CsvDataHandle.Create(path, new CsvOptions { SkipRows = 2 });

        var ids = TestFileHelper.CollectIntColumn(handle, "Id");
        Assert.Equal([2, 3, 4], ids);
    }

    [Fact]
    public void Cursor_MaxRows()
    {
        var path = CreateCsv("Id\n0\n1\n2\n3\n4\n5\n6\n7\n8\n9");
        var handle = CsvDataHandle.Create(path, new CsvOptions { MaxRows = 3 });

        var ids = TestFileHelper.CollectIntColumn(handle, "Id");
        Assert.Equal([0, 1, 2], ids);
    }

    [Fact]
    public void Cursor_CommentsSkipped()
    {
        var path = CreateCsv("Id\n# this is a comment\n0\n1\n# another comment\n2");
        var handle = CsvDataHandle.Create(path, new CsvOptions { CommentPrefix = '#' });

        var ids = TestFileHelper.CollectIntColumn(handle, "Id");
        Assert.Equal([0, 1, 2], ids);
    }

    [Fact]
    public void Cursor_QuotedFields()
    {
        var path = CreateCsv("Name\n\"Alice, Bob\"\n\"He said \"\"hi\"\"\"\n\"line1\"");
        var handle = CsvDataHandle.Create(path);

        var names = TestFileHelper.CollectStringColumn(handle, "Name");
        Assert.Equal("Alice, Bob", names[0]);
        Assert.Equal("He said \"hi\"", names[1]);
        Assert.Equal("line1", names[2]);
    }

    [Fact]
    public void Cursor_CustomSeparator()
    {
        var path = CreateCsv("A\tB\n1\t2\n3\t4");
        var handle = CsvDataHandle.Create(path, new CsvOptions { Separator = '\t' });

        var a = TestFileHelper.CollectIntColumn(handle, "A");
        Assert.Equal([1, 3], a);
    }

    [Fact]
    public void Materialize_ReturnsInMemoryDataHandle()
    {
        var path = CreateCsv("Id,Name\n1,Alice\n2,Bob\n3,Carol");
        var handle = CsvDataHandle.Create(path);
        var materialized = handle.Materialize();

        Assert.Equal(3L, materialized.RowCount);
        var ids = TestFileHelper.CollectIntColumn(materialized, "Id");
        Assert.Equal([1, 2, 3], ids);
    }

    [Fact]
    public void Properties_CorrectDefaults()
    {
        var path = CreateCsv("Id\n1");
        var handle = CsvDataHandle.Create(path);

        Assert.Equal(OrderingPolicy.StrictlyOrdered, handle.Ordering);
        Assert.Equal(MaterializationCapabilities.CursorOnly, handle.Capabilities);
        Assert.Null(handle.RowCount);
    }
}
