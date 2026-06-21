namespace HPD.ML.Backends.Mlx.Training;

public sealed class MlxLinearLayer : MlxModule
{
    private readonly MlxParameter[] _parameters;

    public MlxLinearLayer(
        MlxFloatBackend backend,
        int inputSize,
        int outputSize,
        ReadOnlySpan<float> weights,
        ReadOnlySpan<float> bias,
        string name = "linear")
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (inputSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputSize), "Input size must be positive.");
        if (outputSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputSize), "Output size must be positive.");
        if (weights.Length != inputSize * outputSize)
            throw new ArgumentException($"Weight length must be {inputSize * outputSize}.", nameof(weights));
        if (bias.Length != outputSize)
            throw new ArgumentException($"Bias length must be {outputSize}.", nameof(bias));

        Weight = new MlxParameter($"{name}.weight", backend.CreateMatrix(inputSize, outputSize, weights));
        Bias = new MlxParameter($"{name}.bias", backend.CreateMatrix(1, outputSize, bias));
        _parameters = [Weight, Bias];
    }

    public MlxParameter Weight { get; }
    public MlxParameter Bias { get; }

    public override IEnumerable<MlxParameter> Parameters => _parameters;

    public override MlxFloatTensorVar Forward(MlxTensorTape tape, MlxFloatTensorVar input)
    {
        ArgumentNullException.ThrowIfNull(tape);
        var weight = tape.Watch(Weight.Value);
        var bias = tape.Watch(Bias.Value);
        return Forward(tape, input, weight, bias);
    }

    public MlxFloatTensorVar Forward(
        MlxTensorTape tape,
        MlxFloatTensorVar input,
        IReadOnlyDictionary<MlxParameter, MlxFloatTensorVar> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return Forward(tape, input, parameters[Weight], parameters[Bias]);
    }

    private MlxFloatTensorVar Forward(
        MlxTensorTape tape,
        MlxFloatTensorVar input,
        MlxFloatTensorVar weight,
        MlxFloatTensorVar bias)
    {
        return tape.Add(tape.MatMul(input, weight), tape.BroadcastTo(bias, input.Value.Rows, Bias.Value.Cols));
    }
}
