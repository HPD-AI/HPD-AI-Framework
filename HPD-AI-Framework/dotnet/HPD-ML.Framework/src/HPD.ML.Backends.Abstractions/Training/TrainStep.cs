namespace HPD.ML.Backends.Abstractions.Training;


public static class TrainStep
{
    public static float Run<TTensor, TVariable, TTape>(
        ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
        IReadOnlyList<TrainableParameter<TTensor>> parameters,
        ITrainableOptimizer<TTensor> optimizer,
        Func<TTape, IReadOnlyDictionary<TrainableParameter<TTensor>, TVariable>, TVariable> lossFactory)
        where TTensor : class, IDisposable
        where TTape : IDisposable
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(optimizer);
        ArgumentNullException.ThrowIfNull(lossFactory);

        using var tape = backend.CreateTape();
        var watched = new Dictionary<TrainableParameter<TTensor>, TVariable>(ReferenceEqualityComparer.Instance);
        foreach (var parameter in parameters)
            watched.Add(parameter, backend.Watch(tape, parameter.Value));

        var loss = lossFactory(tape, watched);
        var lossValue = backend.ReadScalar(backend.Value(loss));

        foreach (var parameter in parameters)
        {
            using var gradient = backend.Gradient(tape, loss, watched[parameter]);
            optimizer.Step(parameter.Value, gradient);
        }

        return lossValue;
    }
}
