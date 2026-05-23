namespace HPD.ML.Backends.Pjrt.Training;

public sealed class PjrtParameter : IDisposable
{
    private bool _disposed;

    public PjrtParameter(string name, PjrtFloatTensor value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parameter name is required.", nameof(name));

        Name = name;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Name { get; }
    public PjrtFloatTensor Value { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Value.Dispose();
    }
}
