namespace HPD.ML.Backends.Abstractions.Training;

public sealed class TrainableParameter<TTensor> : IDisposable
    where TTensor : class, IDisposable
{
    private bool _disposed;

    public TrainableParameter(string name, TTensor value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parameter name is required.", nameof(name));

        Name = name;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Name { get; }
    public TTensor Value { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Value.Dispose();
    }
}
