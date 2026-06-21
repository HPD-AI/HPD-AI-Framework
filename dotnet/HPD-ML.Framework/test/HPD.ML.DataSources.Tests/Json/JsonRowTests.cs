namespace HPD.ML.DataSources.Tests;

public class JsonRowTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string CreateJsonl(string content)
    {
        var path = TestFileHelper.WriteTempFile(content, ".jsonl");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void GetValue_Int()
    {
        var path = CreateJsonl("{\"v\":42}");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });
        using var cursor = handle.GetCursor(["v"]);
        Assert.True(cursor.MoveNext());
        Assert.Equal(42, cursor.Current.GetValue<int>("v"));
    }

    [Fact]
    public void GetValue_String()
    {
        var path = CreateJsonl("{\"v\":\"hello\"}");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });
        using var cursor = handle.GetCursor(["v"]);
        Assert.True(cursor.MoveNext());
        Assert.Equal("hello", cursor.Current.GetValue<string>("v"));
    }

    [Fact]
    public void GetValue_Bool()
    {
        var path = CreateJsonl("{\"v\":true}");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });
        using var cursor = handle.GetCursor(["v"]);
        Assert.True(cursor.MoveNext());
        Assert.True(cursor.Current.GetValue<bool>("v"));
    }

    [Fact]
    public void GetValue_NestedPath()
    {
        var path = CreateJsonl("{\"a\":{\"b\":99}}");
        var handle = JsonDataHandle.Create(path, new JsonOptions
        {
            IsJsonLines = true,
            MaxFlattenDepth = 1
        });
        using var cursor = handle.GetCursor(["a.b"]);
        Assert.True(cursor.MoveNext());
        Assert.Equal(99, cursor.Current.GetValue<int>("a.b"));
    }

    [Fact]
    public void GetValue_MissingProperty_Throws()
    {
        var path = CreateJsonl("{\"v\":1}");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });
        using var cursor = handle.GetCursor(["v"]);
        Assert.True(cursor.MoveNext());
        Assert.Throws<KeyNotFoundException>(() => cursor.Current.GetValue<int>("nonexistent"));
    }
}
