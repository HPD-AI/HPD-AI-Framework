using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class PipelineTests
{
    [Fact]
    public void Pipeline_FromColumns_Select_Rename_Cursor()
    {
        var source = TestHelpers.CreateThreeColumnHandle(3);

        var pipeline = new ComposedTransform([
            ColumnSelectTransform.Keep("A", "B"),
            new ColumnRenameTransform("A", "X"),
        ]);

        var result = pipeline.Apply(source);

        Assert.Equal(2, result.Schema.Columns.Count);
        Assert.NotNull(result.Schema.FindByName("X"));
        Assert.NotNull(result.Schema.FindByName("B"));

        var xs = TestHelpers.CollectIntColumn(result, "X");
        Assert.Equal([0, 1, 2], xs);
    }

    [Fact]
    public void Pipeline_FromColumns_Filter_Materialize()
    {
        var source = TestHelpers.CreateSimpleHandle(10);
        var filtered = new FilteredDataHandle(source, row => row.GetValue<int>("Id") < 5);
        var materialized = filtered.Materialize();

        Assert.Equal(5L, materialized.RowCount);
        var ids = TestHelpers.CollectIntColumn(materialized, "Id");
        Assert.Equal([0, 1, 2, 3, 4], ids);
    }

    [Fact]
    public void Pipeline_Split_TrainTransform_Score()
    {
        var source = TestHelpers.CreateSimpleHandle(100);
        var (train, test) = DataHandleSplitter.TrainTestSplit(source, testFraction: 0.2, seed: 42);

        // Apply a transform only to the test set
        var transform = ColumnSelectTransform.Keep("Id");
        var scored = transform.Apply(test);

        Assert.Single(scored.Schema.Columns);
        Assert.Equal("Id", scored.Schema.Columns[0].Name);
        Assert.Equal(20L, scored.RowCount);
    }

    [Fact]
    public void Pipeline_Concatenate_Shuffle_Cursor()
    {
        var a = InMemoryDataHandle.FromColumns(("Id", new int[] { 0, 1, 2, 3, 4 }));
        var b = InMemoryDataHandle.FromColumns(("Id", new int[] { 5, 6, 7, 8, 9 }));

        var concat = new ConcatenatedDataHandle(a, b);
        var shuffled = new ShuffledDataHandle(concat, seed: 42);

        var ids = TestHelpers.CollectIntColumn(shuffled, "Id");
        Assert.Equal(10, ids.Count);
        Assert.Equal(Enumerable.Range(0, 10).ToHashSet(), ids.ToHashSet());
    }

    [Fact]
    public void Pipeline_Cached_DoesNotRematerialize()
    {
        int factoryCallCount = 0;
        var inner = TestHelpers.CreateSimpleHandle(5);
        var cursorHandle = new CursorDataHandle(
            inner.Schema,
            columns => { factoryCallCount++; return inner.GetCursor(columns); },
            5);

        var cached = new CachedDataHandle(cursorHandle);

        // First access materializes
        using var c1 = cached.GetCursor(["Id"]);
        var count1 = factoryCallCount;

        // Second access uses cache — no new factory calls
        using var c2 = cached.GetCursor(["Id"]);
        Assert.Equal(count1, factoryCallCount);
    }

    [Fact]
    public void Pipeline_SchemaBuilder_InMemory_ColumnCopy()
    {
        var schema = new SchemaBuilder()
            .AddColumn<int>("Id")
            .AddColumn<float>("Score")
            .Build();

        var handle = new InMemoryDataHandle(schema, new Dictionary<string, Array>
        {
            ["Id"] = new int[] { 1, 2, 3 },
            ["Score"] = new float[] { 10f, 20f, 30f },
        });

        var transform = new ColumnCopyTransform("Score", "Score_backup");
        var result = transform.Apply(handle);

        var scores = TestHelpers.CollectFloatColumn(result, "Score");
        var backups = TestHelpers.CollectFloatColumn(result, "Score_backup");
        Assert.Equal(scores, backups);
    }
}
