namespace HPD.ML.DataSources.Tests;

using HPD.ML.Core;

public class CsvRowTests : IDisposable
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
    public void GetValue_Int_Correct()
    {
        var path = CreateCsv("V\n42");
        var handle = CsvDataHandle.Create(path);
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        Assert.Equal(42, cursor.Current.GetValue<int>("V"));
    }

    [Fact]
    public void GetValue_Float_Correct()
    {
        var path = CreateCsv("V\n3.14");
        var schema = new SchemaBuilder().AddColumn<float>("V").Build();
        var handle = CsvDataHandle.Create(path, schema);
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        Assert.Equal(3.14f, cursor.Current.GetValue<float>("V"), 0.01f);
    }

    [Fact]
    public void GetValue_String_Correct()
    {
        var path = CreateCsv("V\nhello");
        var handle = CsvDataHandle.Create(path);
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        Assert.Equal("hello", cursor.Current.GetValue<string>("V"));
    }

    [Fact]
    public void GetValue_Bool_Correct()
    {
        var path = CreateCsv("V\ntrue");
        var handle = CsvDataHandle.Create(path);
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        Assert.True(cursor.Current.GetValue<bool>("V"));
    }

    [Fact]
    public void GetValue_MissingColumn_Throws()
    {
        var path = CreateCsv("V\n1");
        var handle = CsvDataHandle.Create(path);
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        Assert.Throws<KeyNotFoundException>(() => cursor.Current.GetValue<int>("NonExistent"));
    }

    [Fact]
    public void GetValue_Object_UsesSchemaType()
    {
        var path = CreateCsv("V\n42");
        var handle = CsvDataHandle.Create(path);
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        var obj = cursor.Current.GetValue<object>("V");
        Assert.IsType<int>(obj);
        Assert.Equal(42, (int)obj);
    }
}
