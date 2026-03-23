using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class InMemoryDataHandleTests
{
    [Fact]
    public void Properties_ReflectColumnarStorage()
    {
        var handle = TestHelpers.CreateSimpleHandle(3);

        Assert.Equal(3L, handle.RowCount);
        Assert.Equal(OrderingPolicy.StrictlyOrdered, handle.Ordering);
        Assert.True(handle.Capabilities.HasFlag(MaterializationCapabilities.ColumnarAccess));
        Assert.True(handle.Capabilities.HasFlag(MaterializationCapabilities.BatchAccess));
        Assert.True(handle.Capabilities.HasFlag(MaterializationCapabilities.KnownDensity));
    }

    [Fact]
    public void Materialize_ReturnsSelf()
    {
        var handle = TestHelpers.CreateSimpleHandle();
        Assert.Same(handle, handle.Materialize());
    }

    [Fact]
    public void GetCursor_IteratesAllRows()
    {
        var ids = TestHelpers.CollectIntColumn(TestHelpers.CreateSimpleHandle(5), "Id");
        Assert.Equal([0, 1, 2, 3, 4], ids);
    }

    [Fact]
    public async Task StreamRows_IteratesAllRows()
    {
        var handle = TestHelpers.CreateSimpleHandle(5);
        var count = 0;
        await foreach (var row in handle.StreamRows())
            count++;
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task StreamRows_CancellationToken_Respected()
    {
        var handle = TestHelpers.CreateSimpleHandle(100);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in handle.StreamRows(cts.Token)) { }
        });
    }

    [Fact]
    public void TryGetColumnBatch_TypedColumn_ReturnsTensor()
    {
        var handle = InMemoryDataHandle.FromColumns(
            ("Values", new float[] { 1f, 2f, 3f, 4f, 5f }));

        Assert.True(handle.TryGetColumnBatch<float>("Values", 1, 3, out var batch));
        Assert.Equal(3, batch.FlattenedLength);
    }

    [Fact]
    public void TryGetColumnBatch_MissingColumn_ReturnsFalse()
    {
        var handle = TestHelpers.CreateSimpleHandle();
        Assert.False(handle.TryGetColumnBatch<float>("Bogus", 0, 1, out _));
    }

    [Fact]
    public void TryGetColumnBatch_PartialRange_ClampedToLength()
    {
        var handle = InMemoryDataHandle.FromColumns(
            ("V", new float[] { 1f, 2f, 3f, 4f, 5f }));

        Assert.True(handle.TryGetColumnBatch<float>("V", 3, 10, out var batch));
        Assert.Equal(2, batch.FlattenedLength);
    }

    [Fact]
    public void FromColumns_InfersSchemaFromArrayTypes()
    {
        var handle = InMemoryDataHandle.FromColumns(
            ("Age", new int[] { 25, 30 }),
            ("Name", new string[] { "Alice", "Bob" }));

        Assert.Equal(typeof(int), handle.Schema.FindByName("Age")!.Type.ClrType);
        Assert.Equal(typeof(string), handle.Schema.FindByName("Name")!.Type.ClrType);
    }

    [Fact]
    public void FromColumns_RoundTrips_ThroughCursor()
    {
        var handle = InMemoryDataHandle.FromColumns(
            ("Id", new int[] { 1, 2, 3 }),
            ("Score", new float[] { 10f, 20f, 30f }));

        var ids = TestHelpers.CollectIntColumn(handle, "Id");
        var scores = TestHelpers.CollectFloatColumn(handle, "Score");

        Assert.Equal([1, 2, 3], ids);
        Assert.Equal([10f, 20f, 30f], scores);
    }

    [Fact]
    public void FromEnumerable_BuildsCorrectData()
    {
        var items = new[] { (Id: 1, Score: 1.5f), (Id: 2, Score: 2.5f) };
        var schema = new SchemaBuilder()
            .AddColumn<int>("Id")
            .AddColumn<float>("Score")
            .Build();

        var handle = InMemoryDataHandle.FromEnumerable(
            items,
            item => new Dictionary<string, object> { ["Id"] = item.Id, ["Score"] = item.Score },
            schema);

        Assert.Equal(2L, handle.RowCount);
        var ids = TestHelpers.CollectIntColumn(handle, "Id");
        Assert.Equal([1, 2], ids);
    }
}
