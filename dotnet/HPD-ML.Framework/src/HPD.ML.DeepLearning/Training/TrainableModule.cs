namespace HPD.ML.DeepLearning.Training;

using HPD.ML.Backends.Abstractions.Training;

public abstract class TrainableModule<TTensor, TVariable, TTape> : IDisposable
    where TTensor : class, IDisposable
    where TTape : IDisposable
{
    public abstract IEnumerable<HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor>> Parameters { get; }

    public abstract TVariable Forward(
        ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
        TTape tape,
        TVariable input,
        IReadOnlyDictionary<HPD.ML.Backends.Abstractions.Training.TrainableParameter<TTensor>, TVariable> parameters);

    public virtual void Dispose()
    {
        foreach (var parameter in Parameters)
            parameter.Dispose();
    }
}
