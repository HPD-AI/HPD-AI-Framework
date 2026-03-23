using Rhodium.Tensor.Storage;

namespace Rhodium.Tensor;

/// <summary>
/// Reference implementation of ITensorStore using paged columnar storage.
/// </summary>
public sealed class PagedTensorStore : ITensorStore, IDisposable
{
    private interface IColumnCollection : IDisposable
    {
        void Grow();
    }

    private sealed class ColumnCollection<T> : IColumnCollection where T : unmanaged
    {
        public readonly Dictionary<string, PagedColumn<T>> Columns = new();

        public void Grow()
        {
            foreach (var col in Columns.Values)
                col.Grow();
        }

        public void Dispose()
        {
            foreach (var col in Columns.Values)
                col.Dispose();
            Columns.Clear();
        }
    }

    private readonly Dictionary<Type, IColumnCollection> _columns = new();
    private readonly Dictionary<string, Array> _parameters = new();
    private int _virtualCount = 0;

    public int PageSize => AlignedPage<PriceF64>.Capacity;

    public ref T GetScalar<T>(VectorField<T> field, int virtualIndex) where T : unmanaged =>
        ref GetColumn(field).ValueAt(virtualIndex);

    public Span<T> GetPage<T>(VectorField<T> field, int pageIndex) where T : unmanaged =>
        GetColumn(field).GetPage(pageIndex);

    public void Broadcast<T>(VectorField<T> field, T value, int start, int length) where T : unmanaged
    {
        var col = GetColumn(field);
        int end = start + length;
        int current = start;

        while (current < end)
        {
            int pageIdx = current / PageSize;
            int pageOffset = current % PageSize;
            int pageRemaining = PageSize - pageOffset;
            int writeLen = Math.Min(pageRemaining, end - current);

            var span = col.GetPage(pageIdx);
            span.Slice(pageOffset, writeLen).Fill(value);
            current += writeLen;
        }
    }

    public int Grow()
    {
        var index = _virtualCount++;
        if (index % PageSize == 0)
        {
            foreach (var col in _columns.Values)
                col.Grow();
        }
        return index;
    }

    public ReadOnlySpan<T> GetParameter<T>(string name) => (T[])_parameters[name];

    public void ForEachPage<TKernel>(TKernel kernel) where TKernel : IComputeKernel
    {
        var pages = (_virtualCount + PageSize - 1) / PageSize;
        for (int i = 0; i < pages; i++)
            kernel.Execute(this, i);
    }

    private PagedColumn<T> GetColumn<T>(VectorField<T> field) where T : unmanaged
    {
        if (!_columns.TryGetValue(typeof(T), out var collection))
        {
            collection = new ColumnCollection<T>();
            _columns[typeof(T)] = collection;
        }

        var typed = (ColumnCollection<T>)collection;
        if (typed.Columns.TryGetValue(field.Name, out var col))
            return col;

        var column = new PagedColumn<T>();
        var pages = (_virtualCount + PageSize - 1) / PageSize;
        for (int i = 0; i < pages; i++)
            column.Grow();
        typed.Columns[field.Name] = column;
        return column;
    }

    public void Dispose()
    {
        foreach (var col in _columns.Values)
            col.Dispose();
        _columns.Clear();
    }
}
