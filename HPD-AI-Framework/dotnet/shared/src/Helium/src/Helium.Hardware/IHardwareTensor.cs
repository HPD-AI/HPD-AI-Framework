namespace Helium.Hardware;

/// <summary>
/// Bulk-transfer-only hardware tensor handle.
/// No scalar indexing is exposed because device tensors may synchronize on scalar access.
/// </summary>
public interface IHardwareTensor<T> : IDisposable where T : unmanaged
{
    int Rows { get; }
    int Cols { get; }
    void CopyToHost(Span<T> hostBuffer);
    void UpdateFromSpan(ReadOnlySpan<T> hostData);
}

/// <summary>
/// Explicit immutable execution backend. Operations return new tensors and do not mutate inputs.
/// </summary>
public interface IExecutionBackend<T> where T : unmanaged
{
    IHardwareTensor<T> CreateMatrix(int rows, int cols, ReadOnlySpan<T> initialData = default);
    IHardwareTensor<T> MatMul(IHardwareTensor<T> left, IHardwareTensor<T> right);
    IHardwareTensor<T> LinearSolve(IHardwareTensor<T> matrix, IHardwareTensor<T> rightHandSide);
    IHardwareTensor<T> MatrixInverse(IHardwareTensor<T> value);
    IHardwareTensor<T> Transpose(IHardwareTensor<T> value);
    IHardwareTensor<T> Add(IHardwareTensor<T> left, IHardwareTensor<T> right);
    IHardwareTensor<T> Subtract(IHardwareTensor<T> left, IHardwareTensor<T> right);
    IHardwareTensor<T> Multiply(IHardwareTensor<T> left, IHardwareTensor<T> right);
    IHardwareTensor<T> Negate(IHardwareTensor<T> value);
    IHardwareTensor<T> Scale(IHardwareTensor<T> value, T scalar);
    T Sum(IHardwareTensor<T> value);
    T Mean(IHardwareTensor<T> value);
    T Dot(IHardwareTensor<T> left, IHardwareTensor<T> right);
    T Norm(IHardwareTensor<T> value);
}
