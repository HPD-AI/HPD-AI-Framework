namespace HPD.ML.Backends.Mlx.Training;

public static class MlxActivations
{
    public static MlxFloatTensorVar Sigmoid(MlxTensorTape tape, MlxFloatTensorVar value)
        => tape.Sigmoid(value);

    public static MlxFloatTensorVar Tanh(MlxTensorTape tape, MlxFloatTensorVar value)
        => tape.Tanh(value);

    public static MlxFloatTensorVar SiLU(MlxTensorTape tape, MlxFloatTensorVar value)
        => tape.Multiply(value, tape.Sigmoid(value));

    public static MlxFloatTensorVar GeluApprox(MlxTensorTape tape, MlxFloatTensorVar value)
    {
        ArgumentNullException.ThrowIfNull(tape);

        var x2 = tape.Multiply(value, value);
        var x3 = tape.Multiply(x2, value);
        var inner = tape.Scale(tape.Add(value, tape.Scale(x3, 0.044715f)), 0.7978845608f);
        var one = tape.ConstantLike(value, 1.0f);
        return tape.Scale(tape.Multiply(value, tape.Add(one, tape.Tanh(inner))), 0.5f);
    }

    public static MlxFloatTensorVar ReLU(MlxTensorTape tape, MlxFloatTensorVar value)
        => tape.ReLU(value);

    public static MlxFloatTensorVar LeakyReLU(MlxTensorTape tape, MlxFloatTensorVar value, float negativeSlope = 0.01f)
        => tape.LeakyReLU(value, negativeSlope);
}
