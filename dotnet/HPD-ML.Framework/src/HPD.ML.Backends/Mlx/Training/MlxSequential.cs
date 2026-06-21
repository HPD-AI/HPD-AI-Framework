namespace HPD.ML.Backends.Mlx.Training;

public sealed class MlxSequential : MlxModule
{
    private readonly MlxModule[] _layers;

    public MlxSequential(params MlxModule[] layers)
    {
        if (layers.Length == 0)
            throw new ArgumentException("Sequential model requires at least one layer.", nameof(layers));

        _layers = layers;
    }

    public override IEnumerable<MlxParameter> Parameters => _layers.SelectMany(layer => layer.Parameters);

    public override MlxFloatTensorVar Forward(MlxTensorTape tape, MlxFloatTensorVar input)
    {
        var value = input;
        foreach (var layer in _layers)
            value = layer.Forward(tape, value);
        return value;
    }

    public override void Dispose()
    {
        for (var i = _layers.Length - 1; i >= 0; i--)
            _layers[i].Dispose();
    }
}
