namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// IRowCursor that applies a row mapping function to each row from an inner cursor.
/// </summary>
public sealed class MappedCursor : IRowCursor
{
    private readonly IRowCursor _inner;
    private readonly Func<IRow, IRow> _map;
    private IRow? _current;

    public MappedCursor(IRowCursor inner, Func<IRow, IRow> map)
    {
        _inner = inner;
        _map = map;
    }

    public IRow Current => _current ?? throw new InvalidOperationException("Call MoveNext first.");

    public bool MoveNext()
    {
        if (!_inner.MoveNext())
        {
            _current = null;
            return false;
        }
        _current = _map(_inner.Current);
        return true;
    }

    public void Dispose() => _inner.Dispose();
}
