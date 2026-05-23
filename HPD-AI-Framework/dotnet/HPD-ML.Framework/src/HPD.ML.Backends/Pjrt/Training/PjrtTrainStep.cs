namespace HPD.ML.Backends.Pjrt.Training;

public static class PjrtTrainStep
{
    public static float Run(
        PjrtFloatBackend backend,
        IReadOnlyList<PjrtParameter> parameters,
        IPjrtOptimizer optimizer,
        Func<PjrtTensorTape, IReadOnlyDictionary<PjrtParameter, PjrtFloatTensorVar>, PjrtFloatTensorVar> lossFactory)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(optimizer);
        ArgumentNullException.ThrowIfNull(lossFactory);

        using var tape = new PjrtTensorTape(backend);
        var watched = new Dictionary<PjrtParameter, PjrtFloatTensorVar>(ReferenceEqualityComparer.Instance);
        foreach (var parameter in parameters)
            watched.Add(parameter, tape.Watch(parameter.Value));

        var loss = lossFactory(tape, watched);
        var lossValue = loss.Value.ToArray()[0];

        foreach (var parameter in parameters)
        {
            using var gradient = tape.Gradient(loss, watched[parameter]);
            optimizer.Step(parameter.Value, gradient);
        }

        return lossValue;
    }
}
