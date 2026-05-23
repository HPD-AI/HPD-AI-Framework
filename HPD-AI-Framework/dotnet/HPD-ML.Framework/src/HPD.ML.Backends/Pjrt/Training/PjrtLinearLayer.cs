namespace HPD.ML.Backends.Pjrt.Training;

public sealed class PjrtLinearLayer : PjrtModule
{
    private readonly PjrtParameter[] _parameters;

    public PjrtLinearLayer(
        PjrtFloatBackend backend,
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

        Weight = new PjrtParameter($"{name}.weight", backend.CreateMatrix(inputSize, outputSize, weights));
        Bias = new PjrtParameter($"{name}.bias", backend.CreateMatrix(1, outputSize, bias));
        _parameters = [Weight, Bias];
    }

    public PjrtParameter Weight { get; }
    public PjrtParameter Bias { get; }

    public override IEnumerable<PjrtParameter> Parameters => _parameters;

    public override PjrtFloatTensorVar Forward(PjrtTensorTape tape, PjrtFloatTensorVar input)
    {
        ArgumentNullException.ThrowIfNull(tape);
        var weight = tape.Watch(Weight.Value);
        var bias = tape.Watch(Bias.Value);
        return Forward(tape, input, weight, bias);
    }

    public PjrtFloatTensorVar Forward(
        PjrtTensorTape tape,
        PjrtFloatTensorVar input,
        IReadOnlyDictionary<PjrtParameter, PjrtFloatTensorVar> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return Forward(tape, input, parameters[Weight], parameters[Bias]);
    }

    private PjrtFloatTensorVar Forward(
        PjrtTensorTape tape,
        PjrtFloatTensorVar input,
        PjrtFloatTensorVar weight,
        PjrtFloatTensorVar bias)
    {
        return tape.Add(tape.MatMul(input, weight), tape.BroadcastTo(bias, input.Value.Rows, Bias.Value.Cols));
    }
}
