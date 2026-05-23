using HPD.ML.Backends.Abstractions.Training;

namespace HPD.ML.Backends.Mlx.Training;

public sealed class MlxTrainableBackend :
    ITrainableTensorBackend<MlxFloatTensor, MlxFloatTensorVar, MlxTensorTape>,
    ITrainableActivationBackend<MlxFloatTensor, MlxFloatTensorVar, MlxTensorTape>
{
    private readonly MlxFloatBackend _backend;

    public MlxTrainableBackend(MlxFloatBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public MlxFloatTensor CreateMatrix(int rows, int cols, ReadOnlySpan<float> data = default) => _backend.CreateMatrix(rows, cols, data);
    public MlxTensorTape CreateTape() => new(_backend);
    public MlxFloatTensorVar Watch(MlxTensorTape tape, MlxFloatTensor value) => tape.Watch(value);
    public MlxFloatTensorVar MatMul(MlxTensorTape tape, MlxFloatTensorVar left, MlxFloatTensorVar right) => tape.MatMul(left, right);
    public MlxFloatTensorVar Add(MlxTensorTape tape, MlxFloatTensorVar left, MlxFloatTensorVar right) => tape.Add(left, right);
    public MlxFloatTensorVar Subtract(MlxTensorTape tape, MlxFloatTensorVar left, MlxFloatTensorVar right) => tape.Subtract(left, right);
    public MlxFloatTensorVar Multiply(MlxTensorTape tape, MlxFloatTensorVar left, MlxFloatTensorVar right) => tape.Multiply(left, right);
    public MlxFloatTensorVar Mean(MlxTensorTape tape, MlxFloatTensorVar value) => tape.Mean(value);
    public MlxFloatTensorVar Scale(MlxTensorTape tape, MlxFloatTensorVar value, float scalar) => tape.Scale(value, scalar);
    public MlxFloatTensorVar BroadcastTo(MlxTensorTape tape, MlxFloatTensorVar value, int rows, int cols) => tape.BroadcastTo(value, rows, cols);
    public MlxFloatTensorVar ReLU(MlxTensorTape tape, MlxFloatTensorVar value) => tape.ReLU(value);
    public MlxFloatTensor Gradient(MlxTensorTape tape, MlxFloatTensorVar output, MlxFloatTensorVar input) => tape.Gradient(output, input);
    public MlxFloatTensor Value(MlxFloatTensorVar variable) => variable.Value;
    public MlxFloatTensor Scale(MlxFloatTensor value, float scalar) => _backend.Scale(value, scalar);
    public MlxFloatTensor Subtract(MlxFloatTensor left, MlxFloatTensor right) => _backend.Subtract(left, right);
    public float[] ToArray(MlxFloatTensor value) => value.ToArray();
    public float ReadScalar(MlxFloatTensor value) => value.ToArray()[0];
    public int Rows(MlxFloatTensor value) => value.Rows;
    public int Cols(MlxFloatTensor value) => value.Cols;
    public int Rows(MlxFloatTensorVar value) => value.Value.Rows;
    public int Cols(MlxFloatTensorVar value) => value.Value.Cols;
    public void Update(MlxFloatTensor value, ReadOnlySpan<float> data) => value.UpdateFromSpan(data);
}
