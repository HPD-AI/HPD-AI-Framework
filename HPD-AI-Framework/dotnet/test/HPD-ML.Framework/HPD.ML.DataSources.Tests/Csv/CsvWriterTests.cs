namespace HPD.ML.DataSources.Tests;

using HPD.ML.Core;

public class CsvWriterTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string TempPath(string ext = ".csv")
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpdml_test_{Guid.NewGuid():N}{ext}");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void Write_SimpleData_RoundTrips()
    {
        var source = InMemoryDataHandle.FromColumns(
            ("Id", new int[] { 1, 2, 3 }),
            ("Name", new string[] { "Alice", "Bob", "Carol" }));

        var path = TempPath();
        CsvWriter.Write(source, path);
        var loaded = CsvDataHandle.Create(path);

        var ids = TestFileHelper.CollectIntColumn(loaded, "Id");
        var names = TestFileHelper.CollectStringColumn(loaded, "Name");
        Assert.Equal([1, 2, 3], ids);
        Assert.Equal(["Alice", "Bob", "Carol"], names);
    }

    [Fact]
    public void Write_EscapesCommasInFields()
    {
        var source = InMemoryDataHandle.FromColumns(
            ("V", new string[] { "hello, world" }));

        var path = TempPath();
        CsvWriter.Write(source, path);
        var content = File.ReadAllText(path);
        Assert.Contains("\"hello, world\"", content);
    }

    [Fact]
    public void Write_EscapesQuotesInFields()
    {
        var source = InMemoryDataHandle.FromColumns(
            ("V", new string[] { "she said \"hi\"" }));

        var path = TempPath();
        CsvWriter.Write(source, path);
        var content = File.ReadAllText(path);
        Assert.Contains("\"she said \"\"hi\"\"\"", content);
    }

    [Fact]
    public void Write_NoHeader()
    {
        var source = InMemoryDataHandle.FromColumns(
            ("V", new int[] { 1, 2 }));

        var path = TempPath();
        CsvWriter.Write(source, path, new CsvOptions { HasHeader = false });
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal("1", lines[0]);
    }

    [Fact]
    public async Task WriteAsync_MatchesSync()
    {
        var source = InMemoryDataHandle.FromColumns(
            ("Id", new int[] { 1, 2, 3 }),
            ("Score", new double[] { 1.5, 2.5, 3.5 }));

        var syncPath = TempPath();
        var asyncPath = TempPath();
        CsvWriter.Write(source, syncPath);
        await CsvWriter.WriteAsync(source, asyncPath);

        Assert.Equal(File.ReadAllText(syncPath), File.ReadAllText(asyncPath));
    }
}
