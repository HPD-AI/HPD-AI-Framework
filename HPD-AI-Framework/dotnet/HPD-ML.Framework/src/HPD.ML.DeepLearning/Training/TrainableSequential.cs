namespace HPD.ML.DeepLearning.Training;

using HPD.ML.Backends.Abstractions.Training;

public sealed class TrainableSequential<TTensor, TVariable, TTape> : TrainableModule<TTensor, TVariable, TTape>
    where TTensor : class, IDisposable
    where TTape : IDisposable
{
    private readonly TrainableModule<TTensor, TVariable, TTape>[] _layers;

    public TrainableSequential(params TrainableModule<TTensor, TVariable, TTape>[] layers)
    {
        if (layers.Length == 0)
            throw new ArgumentException("At least one layer is required.", nameof(layers));

        _layers = layers;
    }

    public override IEnumerable<HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor>> Parameters
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

    public override TVariable Forward(
        ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
        TTape tape,
        TVariable input,
        IReadOnlyDictionary<HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor>, TVariable> parameters)
    {
        var current = input;
        foreach (var layer in _layers)
            current = layer.Forward(backend, tape, current, parameters);
        return current;
    }

    public override void Dispose()
    {
        for (var i = _layers.Length - 1; i >= 0; i--)
            _layers[i].Dispose();
    }
}
