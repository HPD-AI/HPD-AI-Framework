namespace HPD.ML.DataSources.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class PipelineTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string CreateFile(string content, string ext)
    {
        var path = TestFileHelper.WriteTempFile(content, ext);
        _tempFiles.Add(path);
        return path;
    }

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
    public void CsvRoundTrip_Write_Read_DataMatches()
    {
        var source = InMemoryDataHandle.FromColumns(
            ("Id", new int[] { 10, 20, 30 }),
            ("Score", new double[] { 1.1, 2.2, 3.3 }));

        var path = TempPath();
        CsvWriter.Write(source, path);
        var loaded = CsvDataHandle.Create(path);
        var materialized = loaded.Materialize();

        var ids = TestFileHelper.CollectIntColumn(materialized, "Id");
        var scores = TestFileHelper.CollectDoubleColumn(materialized, "Score");
        Assert.Equal([10, 20, 30], ids);
        Assert.Equal(1.1, scores[0], 0.01);
        Assert.Equal(2.2, scores[1], 0.01);
        Assert.Equal(3.3, scores[2], 0.01);
    }

    [Fact]
    public void Csv_Load_Transform_Materialize()
    {
        var path = CreateFile("Id,Name,Score\n1,Alice,10\n2,Bob,20\n3,Carol,30", ".csv");
        var handle = CsvDataHandle.Create(path);

        var transform = ColumnSelectTransform.Keep("Id", "Score");
        var result = transform.Apply(handle);
        var materialized = result.Materialize();

        Assert.Equal(2, materialized.Schema.Columns.Count);
        Assert.NotNull(materialized.Schema.FindByName("Id"));
        Assert.NotNull(materialized.Schema.FindByName("Score"));
        Assert.Null(materialized.Schema.FindByName("Name"));
    }

    [Fact]
    public void Csv_Load_TrainTestSplit()
    {
        // Build a CSV with 100 rows
        var lines = new List<string> { "Id" };
        for (int i = 0; i < 100; i++) lines.Add(i.ToString());
        var path = CreateFile(string.Join('\n', lines), ".csv");

        var handle = CsvDataHandle.Create(path);
        var materialized = handle.Materialize();
        var (train, test) = DataHandleSplitter.TrainTestSplit(materialized, testFraction: 0.2, seed: 42);

        Assert.Equal(80L, train.RowCount);
        Assert.Equal(20L, test.RowCount);

        // All IDs present
        var trainIds = TestFileHelper.CollectIntColumn(train, "Id");
        var testIds = TestFileHelper.CollectIntColumn(test, "Id");
        var allIds = trainIds.Concat(testIds).ToHashSet();
        Assert.Equal(Enumerable.Range(0, 100).ToHashSet(), allIds);
    }

    [Fact]
    public void Json_Load_Filter_Materialize()
    {
        var path = CreateFile(
            "{\"id\":1,\"val\":10}\n{\"id\":2,\"val\":20}\n{\"id\":3,\"val\":30}\n{\"id\":4,\"val\":40}\n{\"id\":5,\"val\":50}",
            ".jsonl");
        var handle = JsonDataHandle.Create(path, new JsonOptions { IsJsonLines = true });
        var materialized = handle.Materialize();

        var filtered = new FilteredDataHandle(materialized, row => row.GetValue<int>("id") <= 3);
        var result = filtered.Materialize();

        Assert.Equal(3L, result.RowCount);
        var ids = TestFileHelper.CollectIntColumn(result, "id");
        Assert.Equal([1, 2, 3], ids);
    }

    [Fact]
    public void DictionaryData_Shuffle_Cursor()
    {
        var rows = Enumerable.Range(0, 10).Select(i =>
            (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["Id"] = i }
        ).ToList();

        var handle = DictionaryDataHandle.Create(rows);
        var shuffled = new ShuffledDataHandle(handle, seed: 42);

        var ids = TestFileHelper.CollectIntColumn(shuffled, "Id");
        Assert.Equal(10, ids.Count);
        Assert.Equal(Enumerable.Range(0, 10).ToHashSet(), ids.ToHashSet());
        // Should be in a different order than 0-9
        Assert.NotEqual(Enumerable.Range(0, 10).ToList(), ids);
    }

    [Fact]
    public void Csv_MissingValues_NaN_Pipeline()
    {
        var path = CreateFile("Score\n1.0\n\n3.0", ".csv");
        var schema = new SchemaBuilder().AddColumn<float>("Score").Build();
        var handle = CsvDataHandle.Create(path, schema, new CsvOptions
        {
            MissingValuePolicy = MissingValuePolicy.NaN
        });

        var materialized = handle.Materialize();
        var scores = TestFileHelper.CollectFloatColumn(materialized, "Score");

        Assert.Equal(1.0f, scores[0], 0.01f);
        Assert.True(float.IsNaN(scores[1]));
        Assert.Equal(3.0f, scores[2], 0.01f);
    }
}
