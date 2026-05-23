namespace HPD.ML.Backends.Abstractions.Training;


public static class TrainableLosses
{
    public static TVariable MeanSquaredError<TTensor, TVariable, TTape>(
        ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
        TTape tape,
        TVariable predicted,
        TVariable target)
        where TTensor : class, IDisposable
        where TTape : IDisposable
    {
        ArgumentNullException.ThrowIfNull(backend);

        var diff = backend.Subtract(tape, predicted, target);
        return backend.Mean(tape, backend.Multiply(tape, diff, diff));
    }
}
