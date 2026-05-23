namespace HPD.ML.Backends.Abstractions.Training;

public interface ITrainableTensorBackend<TTensor, TVariable, TTape>
    where TTensor : class, IDisposable
    where TTape : IDisposable
{
    TTensor CreateMatrix(int rows, int cols, ReadOnlySpan<float> data = default);
    TTape CreateTape();
    TVariable Watch(TTape tape, TTensor value);
    TVariable MatMul(TTape tape, TVariable left, TVariable right);
    TVariable Add(TTape tape, TVariable left, TVariable right);
    TVariable Subtract(TTape tape, TVariable left, TVariable right);
    TVariable Multiply(TTape tape, TVariable left, TVariable right);
    TVariable Mean(TTape tape, TVariable value);
    TVariable Scale(TTape tape, TVariable value, float scalar);
    TVariable BroadcastTo(TTape tape, TVariable value, int rows, int cols);
    TTensor Gradient(TTape tape, TVariable output, TVariable input);
    TTensor Value(TVariable variable);
    TTensor Scale(TTensor value, float scalar);
    TTensor Subtract(TTensor left, TTensor right);
    float[] ToArray(TTensor value);
    float ReadScalar(TTensor value);
    int Rows(TTensor value);
    int Cols(TTensor value);
    int Rows(TVariable value);
    int Cols(TVariable value);
    void Update(TTensor value, ReadOnlySpan<float> data);
}
