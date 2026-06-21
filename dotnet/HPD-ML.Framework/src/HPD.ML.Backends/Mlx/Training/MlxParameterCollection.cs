using System.Collections;

namespace HPD.ML.Backends.Mlx.Training;

public sealed class MlxParameterCollection : IReadOnlyList<MlxParameter>, IDisposable
{
    private readonly List<MlxParameter> _parameters = [];
    private bool _disposed;

    public MlxParameterCollection()
    {
    }

    public MlxParameterCollection(IEnumerable<MlxParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _parameters.AddRange(parameters);
    }

    public int Count => _parameters.Count;

    public MlxParameter this[int index] => _parameters[index];

    public void Add(MlxParameter parameter)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MlxParameterCollection));
        _parameters.Add(parameter ?? throw new ArgumentNullException(nameof(parameter)));
    }

    public IEnumerator<MlxParameter> GetEnumerator() => _parameters.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
