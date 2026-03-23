namespace HPD.ML.Core;

using System.Numerics;
using System.Numerics.Tensors;

/// <summary>
/// Bridges between columnar arrays and ReadOnlyTensorSpan&lt;T&gt;.
/// Used by InMemoryDataHandle and external engine adapters.
/// </summary>
public static class TensorHelpers
{
    /// <summary>
    /// Create a 1D tensor view over a typed array slice. Zero-copy.
    /// </summary>
    public static ReadOnlyTensorSpan<T> AsScalarBatch<T>(
        T[] array, int start, int count) where T : unmanaged, INumber<T>
    {
        var slice = array.AsSpan(start, Math.Min(count, array.Length - start));
        return new ReadOnlyTensorSpan<T>(slice, [slice.Length]);
    }

    /// <summary>
    /// Create a 2D tensor view over a typed array where each row is a fixed-size vector.
    /// </summary>
    public static ReadOnlyTensorSpan<T> AsVectorBatch<T>(
        T[] array, int start, int rowCount, int vectorLength) where T : unmanaged, INumber<T>
    {
        int elementStart = start * vectorLength;
        int elementCount = Math.Min(rowCount, (array.Length / vectorLength) - start) * vectorLength;
        var slice = array.AsSpan(elementStart, elementCount);
        return new ReadOnlyTensorSpan<T>(slice, [elementCount / vectorLength, vectorLength]);
    }
}
