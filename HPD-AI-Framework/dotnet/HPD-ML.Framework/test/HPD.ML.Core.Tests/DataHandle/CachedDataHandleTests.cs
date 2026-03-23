using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class CachedDataHandleTests
{
    [Fact]
    public void FirstAccess_MaterializesInner()
    {
        int materializeCount = 0;
        var inner = TestHelpers.CreateSimpleHandle(3);
        var tracking = new TrackingDataHandle(inner, () => materializeCount++);
        var cached = new CachedDataHandle(tracking);

        using var cursor = cached.GetCursor(["Id"]);
        Assert.Equal(1, materializeCount);
    }

    [Fact]
    public void SubsequentAccess_UsesCachedCopy()
    {
        int materializeCount = 0;
        var inner = TestHelpers.CreateSimpleHandle(3);
        var tracking = new TrackingDataHandle(inner, () => materializeCount++);
        var cached = new CachedDataHandle(tracking);

        using var c1 = cached.GetCursor(["Id"]);
        using var c2 = cached.GetCursor(["Id"]);
        Assert.Equal(1, materializeCount);
    }

    [Fact]
    public void Schema_DelegatesWithoutMaterializing()
    {
        int materializeCount = 0;
        var inner = TestHelpers.CreateSimpleHandle(3);
        var tracking = new TrackingDataHandle(inner, () => materializeCount++);
        var cached = new CachedDataHandle(tracking);

        _ = cached.Schema;
        Assert.Equal(0, materializeCount);
    }

    /// <summary>Wrapper that tracks Materialize calls.</summary>
    private sealed class TrackingDataHandle(
        HPD.ML.Abstractions.IDataHandle inner,
        Action onMaterialize) : HPD.ML.Abstractions.IDataHandle
    {
        public HPD.ML.Abstractions.ISchema Schema => inner.Schema;
        public long? RowCount => inner.RowCount;
        public HPD.ML.Abstractions.OrderingPolicy Ordering => inner.Ordering;
        public HPD.ML.Abstractions.MaterializationCapabilities Capabilities => inner.Capabilities;

        public HPD.ML.Abstractions.IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
            => inner.GetCursor(columnsNeeded);

        public HPD.ML.Abstractions.IDataHandle Materialize()
        {
            onMaterialize();
            return inner.Materialize();
        }

        public IAsyncEnumerable<HPD.ML.Abstractions.IRow> StreamRows(CancellationToken ct = default)
            => inner.StreamRows(ct);

        public bool TryGetColumnBatch<T>(string columnName, int startRow, int rowCount,
            out System.Numerics.Tensors.ReadOnlyTensorSpan<T> batch)
            where T : unmanaged, System.Numerics.INumber<T>
            => inner.TryGetColumnBatch(columnName, startRow, rowCount, out batch);
    }
}
