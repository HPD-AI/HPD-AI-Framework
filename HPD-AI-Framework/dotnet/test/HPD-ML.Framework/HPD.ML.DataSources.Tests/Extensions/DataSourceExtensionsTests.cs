namespace HPD.ML.DataSources.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class DataSourceExtensionsTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string CreateFile(string content, string ext)
    {
        var path = TestFileHelper.WriteTempFile(content, ext);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void LoadCsv_ReturnsCorrectHandle()
    {
        var path = CreateFile("Id\n1\n2\n3", ".csv");
        IDataHandle handle = IDataHandle.LoadCsv(path);

        Assert.NotNull(handle.Schema.FindByName("Id"));
        Assert.Equal(3, TestFileHelper.CountRows(handle));
    }

    [Fact]
    public void LoadCsv_WithSchema_UsesProvided()
    {
        var path = CreateFile("V\n1\n2", ".csv");
        var schema = new SchemaBuilder().AddColumn<float>("V").Build();
        IDataHandle handle = IDataHandle.LoadCsv(path, schema);

        Assert.Equal(typeof(float), handle.Schema.Columns[0].Type.ClrType);
    }

    [Fact]
    public void LoadJson_ReturnsCorrectHandle()
    {
        var path = CreateFile("{\"v\":1}\n{\"v\":2}", ".jsonl");
        IDataHandle handle = IDataHandle.LoadJson(path, new JsonOptions { IsJsonLines = true });

        Assert.NotNull(handle.Schema.FindByName("v"));
        Assert.Equal(2, TestFileHelper.CountRows(handle));
    }

    [Fact]
    public void FromDictionaries_ReturnsCorrectHandle()
    {
        var rows = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["X"] = 1 },
            new Dictionary<string, object> { ["X"] = 2 }
        };

        IDataHandle handle = IDataHandle.FromDictionaries(rows);

        Assert.Equal(2, TestFileHelper.CountRows(handle));
    }

    [Fact]
    public void FromRows_ReturnsCorrectHandle()
    {
        var schema = new SchemaBuilder().AddColumn<int>("V").Build();
        IDataHandle handle = IDataHandle.FromRows(schema,
            new Dictionary<string, object> { ["V"] = 1 },
            new Dictionary<string, object> { ["V"] = 2 });

        Assert.Equal(2, TestFileHelper.CountRows(handle));
    }
}
