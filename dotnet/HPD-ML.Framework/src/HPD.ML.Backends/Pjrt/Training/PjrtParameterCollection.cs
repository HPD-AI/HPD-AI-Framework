namespace HPD.ML.Backends.Pjrt.Training;

public sealed class PjrtParameterCollection : IReadOnlyList<PjrtParameter>, IDisposable
{
    private readonly List<PjrtParameter> _parameters = [];
    private bool _disposed;

    public int Count => _parameters.Count;

    public PjrtParameter this[int index] => _parameters[index];

    public void Add(PjrtParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _parameters.Add(parameter);
    }

    public IEnumerator<PjrtParameter> GetEnumerator() => _parameters.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        for (var i = _parameters.Count - 1; i >= 0; i--)
            _parameters[i].Dispose();
        _parameters.Clear();
    }
}
