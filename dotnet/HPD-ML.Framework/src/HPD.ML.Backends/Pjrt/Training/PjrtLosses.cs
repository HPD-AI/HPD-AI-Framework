namespace HPD.ML.Backends.Pjrt.Training;

public static class PjrtLosses
{
    public static PjrtFloatTensorVar MeanSquaredError(
        PjrtTensorTape tape,
        PjrtFloatTensorVar predicted,
        PjrtFloatTensorVar target)
    {
        ArgumentNullException.ThrowIfNull(tape);
        var diff = tape.Subtract(predicted, target);
        return tape.Mean(tape.Multiply(diff, diff));
    }
}
