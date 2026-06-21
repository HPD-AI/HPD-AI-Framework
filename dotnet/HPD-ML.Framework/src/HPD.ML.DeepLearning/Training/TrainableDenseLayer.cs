namespace HPD.ML.DeepLearning.Training;

using HPD.ML.Backends.Abstractions.Training;

public sealed class TrainableDenseLayer<TTensor, TVariable, TTape> : TrainableModule<TTensor, TVariable, TTape>
    where TTensor : class, IDisposable
    where TTape : IDisposable
{
    private readonly HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor>[] _parameters;

    public TrainableDenseLayer(
        ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
        int inputSize,
        int outputSize,
        ReadOnlySpan<float> weights,
        ReadOnlySpan<float> bias,
        string name = "dense")
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

        Weight = new HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor>($"{name}.weight", backend.CreateMatrix(inputSize, outputSize, weights));
        Bias = new HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor>($"{name}.bias", backend.CreateMatrix(1, outputSize, bias));
        _parameters = [Weight, Bias];
    }

    public HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor> Weight { get; }
    public HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor> Bias { get; }

    public override IEnumerable<HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor>> Parameters => _parameters;

    public override TVariable Forward(
        ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
        TTape tape,
        TVariable input,
        IReadOnlyDictionary<HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor>, TVariable> parameters)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(parameters);

        var product = backend.MatMul(tape, input, parameters[Weight]);
        var bias = backend.BroadcastTo(tape, parameters[Bias], backend.Rows(product), backend.Cols(parameters[Bias]));
        return backend.Add(tape, product, bias);
    }
}
