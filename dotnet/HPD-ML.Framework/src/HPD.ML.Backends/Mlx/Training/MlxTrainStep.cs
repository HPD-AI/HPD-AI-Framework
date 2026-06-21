namespace HPD.ML.Backends.Mlx.Training;

public static class MlxTrainStep
{
    public static float Run(
        MlxFloatBackend backend,
        IReadOnlyList<MlxParameter> parameters,
        IMlxOptimizer optimizer,
        Func<MlxTensorTape, IReadOnlyDictionary<MlxParameter, MlxFloatTensorVar>, MlxFloatTensorVar> lossFactory)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(optimizer);
        ArgumentNullException.ThrowIfNull(lossFactory);

        using var tape = new MlxTensorTape(backend);
        var watched = new Dictionary<MlxParameter, MlxFloatTensorVar>(ReferenceEqualityComparer.Instance);
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
