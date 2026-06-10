namespace Rhodium.Tensor;

public readonly ref struct WindowedDouble
{
    private readonly RollingTensorHistory _history;

    public WindowedDouble(double value, RollingTensorHistory history)
    {
        Value = value;
        _history = history;
    }

    public double Value { get; }

    public TensorWindow Window(int length) => _history.Window(length);

    public static implicit operator double(WindowedDouble value) => value.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
