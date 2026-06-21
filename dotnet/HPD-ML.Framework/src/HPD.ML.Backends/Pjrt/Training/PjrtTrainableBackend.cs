using HPD.ML.Backends.Abstractions.Training;

namespace HPD.ML.Backends.Pjrt.Training;

public sealed class PjrtTrainableBackend :
    ITrainableTensorBackend<PjrtFloatTensor, PjrtFloatTensorVar, PjrtTensorTape>,
    ITrainableActivationBackend<PjrtFloatTensor, PjrtFloatTensorVar, PjrtTensorTape>
{
    private readonly PjrtFloatBackend _backend;

    public PjrtTrainableBackend(PjrtFloatBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public PjrtFloatTensor CreateMatrix(int rows, int cols, ReadOnlySpan<float> data = default) => _backend.CreateMatrix(rows, cols, data);
    public PjrtTensorTape CreateTape() => new(_backend);
    public PjrtFloatTensorVar Watch(PjrtTensorTape tape, PjrtFloatTensor value) => tape.Watch(value);
    public PjrtFloatTensorVar MatMul(PjrtTensorTape tape, PjrtFloatTensorVar left, PjrtFloatTensorVar right) => tape.MatMul(left, right);
    public PjrtFloatTensorVar Add(PjrtTensorTape tape, PjrtFloatTensorVar left, PjrtFloatTensorVar right) => tape.Add(left, right);
    public PjrtFloatTensorVar Subtract(PjrtTensorTape tape, PjrtFloatTensorVar left, PjrtFloatTensorVar right) => tape.Subtract(left, right);
    public PjrtFloatTensorVar Multiply(PjrtTensorTape tape, PjrtFloatTensorVar left, PjrtFloatTensorVar right) => tape.Multiply(left, right);
    public PjrtFloatTensorVar Mean(PjrtTensorTape tape, PjrtFloatTensorVar value) => tape.Mean(value);
    public PjrtFloatTensorVar Scale(PjrtTensorTape tape, PjrtFloatTensorVar value, float scalar) => tape.Scale(value, scalar);
    public PjrtFloatTensorVar BroadcastTo(PjrtTensorTape tape, PjrtFloatTensorVar value, int rows, int cols) => tape.BroadcastTo(value, rows, cols);
    public PjrtFloatTensorVar ReLU(PjrtTensorTape tape, PjrtFloatTensorVar value) => tape.ReLU(value);
    public PjrtFloatTensor Gradient(PjrtTensorTape tape, PjrtFloatTensorVar output, PjrtFloatTensorVar input) => tape.Gradient(output, input);
    public PjrtFloatTensor Value(PjrtFloatTensorVar variable) => variable.Value;
    public PjrtFloatTensor Scale(PjrtFloatTensor value, float scalar) => _backend.Scale(value, scalar);
    public PjrtFloatTensor Subtract(PjrtFloatTensor left, PjrtFloatTensor right) => _backend.Subtract(left, right);
    public float[] ToArray(PjrtFloatTensor value) => value.ToArray();
    public float ReadScalar(PjrtFloatTensor value) => value.ToArray()[0];
    public int Rows(PjrtFloatTensor value) => value.Rows;
    public int Cols(PjrtFloatTensor value) => value.Cols;
    public int Rows(PjrtFloatTensorVar value) => value.Value.Rows;
    public int Cols(PjrtFloatTensorVar value) => value.Value.Cols;
    public void Update(PjrtFloatTensor value, ReadOnlySpan<float> data) => value.UpdateFromSpan(data);
}
