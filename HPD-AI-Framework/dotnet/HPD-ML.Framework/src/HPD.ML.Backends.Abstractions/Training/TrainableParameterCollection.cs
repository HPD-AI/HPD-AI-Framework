namespace HPD.ML.Backends.Abstractions.Training;

public sealed class TrainableParameterCollection<TTensor> : IReadOnlyList<TrainableParameter<TTensor>>, IDisposable
    where TTensor : class, IDisposable
{
    private readonly List<TrainableParameter<TTensor>> _parameters = [];
    private bool _disposed;

    public int Count => _parameters.Count;

    public TrainableParameter<TTensor> this[int index] => _parameters[index];

    public void Add(TrainableParameter<TTensor> parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _parameters.Add(parameter);
    }

    public IEnumerator<TrainableParameter<TTensor>> GetEnumerator() => _parameters.GetEnumerator();

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
