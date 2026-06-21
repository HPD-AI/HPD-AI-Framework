namespace HPD.ML.Backends.Pjrt.Training;

public sealed class PjrtSequential : PjrtModule
{
    private readonly PjrtModule[] _layers;

    public PjrtSequential(params PjrtModule[] layers)
    {
        if (layers.Length == 0)
            throw new ArgumentException("At least one layer is required.", nameof(layers));

        _layers = layers;
    }

    public override IEnumerable<PjrtParameter> Parameters
    {
        get
        {
            foreach (var layer in _layers)
            {
                foreach (var parameter in layer.Parameters)
                    yield return parameter;
            }
        }
    }

    public override PjrtFloatTensorVar Forward(PjrtTensorTape tape, PjrtFloatTensorVar input)
    {
        var current = input;
        foreach (var layer in _layers)
            current = layer.Forward(tape, current);
        return current;
    }

    public PjrtFloatTensorVar Forward(
        PjrtTensorTape tape,
        PjrtFloatTensorVar input,
        IReadOnlyDictionary<PjrtParameter, PjrtFloatTensorVar> parameters)
    {
        var current = input;
        foreach (var layer in _layers)
        {
            current = layer is PjrtLinearLayer linear
                ? linear.Forward(tape, current, parameters)
                : layer.Forward(tape, current);
        }

        return current;
    }

    public override void Dispose()
    {
        for (var i = _layers.Length - 1; i >= 0; i--)
            _layers[i].Dispose();
    }
}
