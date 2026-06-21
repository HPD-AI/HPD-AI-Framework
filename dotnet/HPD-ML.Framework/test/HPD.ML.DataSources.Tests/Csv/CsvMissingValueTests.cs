namespace HPD.ML.DataSources.Tests;

using HPD.ML.Core;

public class CsvMissingValueTests : IDisposable
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
    public void MissingValue_DefaultPolicy_ReturnsDefault()
    {
        var path = CreateCsv("V\n ");
        var schema = new SchemaBuilder().AddColumn<int>("V").Build();
        var handle = CsvDataHandle.Create(path, schema, new CsvOptions
        {
            MissingValuePolicy = MissingValuePolicy.DefaultValue
        });
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        Assert.Equal(0, cursor.Current.GetValue<int>("V"));
    }

    [Fact]
    public void MissingValue_NaN_ReturnsNaN_Float()
    {
        var path = CreateCsv("V\n ");
        var schema = new SchemaBuilder().AddColumn<float>("V").Build();
        var handle = CsvDataHandle.Create(path, schema, new CsvOptions
        {
            MissingValuePolicy = MissingValuePolicy.NaN
        });
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        Assert.True(float.IsNaN(cursor.Current.GetValue<float>("V")));
    }

    [Fact]
    public void MissingValue_NaN_ReturnsNaN_Double()
    {
        var path = CreateCsv("V\n ");
        var schema = new SchemaBuilder().AddColumn<double>("V").Build();
        var handle = CsvDataHandle.Create(path, schema, new CsvOptions
        {
            MissingValuePolicy = MissingValuePolicy.NaN
        });
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        Assert.True(double.IsNaN(cursor.Current.GetValue<double>("V")));
    }

    [Fact]
    public void MissingValue_Error_Throws()
    {
        var path = CreateCsv("V\n ");
        var schema = new SchemaBuilder().AddColumn<int>("V").Build();
        var handle = CsvDataHandle.Create(path, schema, new CsvOptions
        {
            MissingValuePolicy = MissingValuePolicy.Error
        });
        using var cursor = handle.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        Assert.Throws<InvalidOperationException>(() => cursor.Current.GetValue<int>("V"));
    }
}
