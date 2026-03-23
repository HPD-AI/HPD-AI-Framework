namespace HPD.ML.DataSources.Tests;

using HPD.ML.Abstractions;

public class JsonDataHandleTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string CreateJson(string content, string ext = ".json")
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
    public void Create_JsonLines_InfersSchema()
    {
        var path = CreateJson("{\"id\":1,\"name\":\"A\"}\n{\"id\":2,\"name\":\"B\"}", ".jsonl");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });

        Assert.NotNull(handle.Schema.FindByName("id"));
        Assert.NotNull(handle.Schema.FindByName("name"));
        Assert.Equal(typeof(int), handle.Schema.FindByName("id")!.Type.ClrType);
        Assert.Equal(typeof(string), handle.Schema.FindByName("name")!.Type.ClrType);
    }

    [Fact]
    public void Create_JsonArray_InfersSchema()
    {
        var path = CreateJson("[{\"x\":1.5},{\"x\":2.5}]");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = false });

        Assert.NotNull(handle.Schema.FindByName("x"));
        // 1.5 is not int, so should be double
        Assert.Equal(typeof(double), handle.Schema.FindByName("x")!.Type.ClrType);
    }

    [Fact]
    public void Create_AutoDetects_JsonLines()
    {
        var path = CreateJson("{\"v\":1}\n{\"v\":2}");
        var handle = JsonDataHandle.Create(path);

        // Should auto-detect as JSONL (starts with {)
        var count = TestFileHelper.CountRows(handle);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Create_AutoDetects_JsonArray()
    {
        var path = CreateJson("[{\"v\":1},{\"v\":2},{\"v\":3}]");
        var handle = JsonDataHandle.Create(path);

        var count = TestFileHelper.CountRows(handle);
        Assert.Equal(3, count);
    }

    [Fact]
    public void Create_PropertyMapping_RenamesColumns()
    {
        var path = CreateJson("{\"instruction\":\"hi\",\"response\":\"hello\"}\n{\"instruction\":\"bye\",\"response\":\"see ya\"}");
        var handle = JsonDataHandle.Create(path, new JsonOptions
        {
            IsJsonLines = true,
            PropertyMapping = new Dictionary<string, string>
            {
                ["instruction"] = "Input",
                ["response"] = "Output"
            }
        });

        Assert.NotNull(handle.Schema.FindByName("Input"));
        Assert.NotNull(handle.Schema.FindByName("Output"));
    }

    [Fact]
    public void Create_TypeHints_OverridesInference()
    {
        var path = CreateJson("{\"score\":1}\n{\"score\":2}");
        var handle = JsonDataHandle.Create(path, new JsonOptions
        {
            IsJsonLines = true,
            TypeHints = new Dictionary<string, Type> { ["score"] = typeof(float) }
        });

        Assert.Equal(typeof(float), handle.Schema.FindByName("score")!.Type.ClrType);
    }

    [Fact]
    public void Create_NestedFlattening_Depth1()
    {
        var path = CreateJson("{\"user\":{\"name\":\"Alice\"}}");
        var handle = JsonDataHandle.Create(path, new JsonOptions
        {
            IsJsonLines = true,
            MaxFlattenDepth = 1
        });

        Assert.NotNull(handle.Schema.FindByName("user.name"));
    }

    [Fact]
    public void Create_NestedFlattening_Depth0()
    {
        var path = CreateJson("{\"user\":{\"name\":\"Alice\"}}");
        var handle = JsonDataHandle.Create(path, new JsonOptions
        {
            IsJsonLines = true,
            MaxFlattenDepth = 0
        });

        // With depth 0, nested object is not flattened — treated as string
        Assert.NotNull(handle.Schema.FindByName("user"));
        Assert.Equal(typeof(string), handle.Schema.FindByName("user")!.Type.ClrType);
    }

    [Fact]
    public void Create_TypeWidening_IntToDouble()
    {
        var path = CreateJson("{\"v\":1}\n{\"v\":1.5}");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });

        Assert.Equal(typeof(double), handle.Schema.FindByName("v")!.Type.ClrType);
    }

    [Fact]
    public void Create_TypeWidening_IntToString()
    {
        var path = CreateJson("{\"v\":1}\n{\"v\":\"text\"}");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });

        Assert.Equal(typeof(string), handle.Schema.FindByName("v")!.Type.ClrType);
    }

    [Fact]
    public void Cursor_ReadsAllRows_JsonLines()
    {
        var path = CreateJson("{\"id\":0}\n{\"id\":1}\n{\"id\":2}\n{\"id\":3}\n{\"id\":4}");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });

        var ids = TestFileHelper.CollectIntColumn(handle, "id");
        Assert.Equal([0, 1, 2, 3, 4], ids);
    }

    [Fact]
    public void Cursor_ReadsAllRows_JsonArray()
    {
        var path = CreateJson("[{\"id\":0},{\"id\":1},{\"id\":2},{\"id\":3},{\"id\":4}]");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = false });

        var ids = TestFileHelper.CollectIntColumn(handle, "id");
        Assert.Equal([0, 1, 2, 3, 4], ids);
    }

    [Fact]
    public void Cursor_SkipsBlankLines()
    {
        var path = CreateJson("{\"v\":1}\n\n{\"v\":2}\n\n{\"v\":3}");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });

        var count = TestFileHelper.CountRows(handle);
        Assert.Equal(3, count);
    }

    [Fact]
    public void Materialize_ReturnsCorrectData()
    {
        var path = CreateJson("{\"id\":1,\"name\":\"A\"}\n{\"id\":2,\"name\":\"B\"}\n{\"id\":3,\"name\":\"C\"}");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });
        var materialized = handle.Materialize();

        Assert.Equal(3L, materialized.RowCount);
        var ids = TestFileHelper.CollectIntColumn(materialized, "id");
        Assert.Equal([1, 2, 3], ids);
    }
}
