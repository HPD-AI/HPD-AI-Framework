namespace HPD.ML.Backends.Mlx.Training;

public static class MlxLosses
{
    public static MlxFloatTensorVar MeanSquaredError(
        MlxTensorTape tape,
        MlxFloatTensorVar prediction,
        MlxFloatTensorVar target)
    {
        ArgumentNullException.ThrowIfNull(tape);

        var error = tape.Subtract(prediction, target);
        return tape.Mean(tape.Multiply(error, error));
    }

    public static MlxFloatTensorVar SoftmaxCrossEntropy(
        MlxTensorTape tape,
        MlxFloatTensorVar logits,
        MlxFloatTensorVar oneHotTargets,
        int classAxis = 1)
    {
        ArgumentNullException.ThrowIfNull(tape);
        if (classAxis is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(classAxis), "Class axis must be 0 or 1.");
        if (logits.Value.Rows != oneHotTargets.Value.Rows || logits.Value.Cols != oneHotTargets.Value.Cols)
            throw new ArgumentException("Logits and targets must have the same shape.", nameof(oneHotTargets));

        var probabilities = tape.Softmax(logits, classAxis);
        var logProbabilities = tape.Log(probabilities);
        var negativeLogLikelihood = tape.Negate(tape.Sum(tape.Multiply(oneHotTargets, logProbabilities)));
        var batchSize = classAxis == 1 ? logits.Value.Rows : logits.Value.Cols;
        return tape.Scale(negativeLogLikelihood, 1.0f / batchSize);
    }

    public static MlxFloatTensorVar BinaryCrossEntropy(
        MlxTensorTape tape,
        MlxFloatTensorVar probabilities,
        MlxFloatTensorVar targets)
    {
        ArgumentNullException.ThrowIfNull(tape);
        if (probabilities.Value.Rows != targets.Value.Rows || probabilities.Value.Cols != targets.Value.Cols)
            throw new ArgumentException("Probabilities and targets must have the same shape.", nameof(targets));

        var one = tape.ConstantLike(probabilities, 1.0f);
        var positive = tape.Multiply(targets, tape.Log(probabilities));
        var negative = tape.Multiply(tape.Subtract(one, targets), tape.Log(tape.Subtract(one, probabilities)));
        return tape.Negate(tape.Mean(tape.Add(positive, negative)));
    }

    public static MlxFloatTensorVar L2Penalty(
        MlxTensorTape tape,
        MlxFloatTensorVar value,
        float coefficient)
    {
        ArgumentNullException.ThrowIfNull(tape);
        if (!float.IsFinite(coefficient) || coefficient < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(coefficient), "Coefficient must be finite and non-negative.");

        return tape.Scale(tape.Sum(tape.Multiply(value, value)), coefficient);
    }
}
