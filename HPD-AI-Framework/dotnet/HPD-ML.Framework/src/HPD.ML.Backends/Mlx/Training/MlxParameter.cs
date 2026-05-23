namespace HPD.ML.Backends.Mlx.Training;

public sealed class MlxParameter : IDisposable
{
    private bool _disposed;

    public MlxParameter(string name, MlxFloatTensor value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parameter name must not be empty.", nameof(name));

        Name = name;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Name { get; }
    public MlxFloatTensor Value { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Value.Dispose();
    }
}
