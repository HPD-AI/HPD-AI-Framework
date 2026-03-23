namespace HPD.ML.Core;

using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;

/// <summary>
/// Row backed by columnar arrays at a specific row index.
/// </summary>
public sealed class Row : IRow
{
    private readonly Dictionary<string, Array> _columns;
    private readonly long _rowIndex;

    public Row(ISchema schema, Dictionary<string, Array> columns, long rowIndex)
    {
        Schema = schema;
        _columns = columns;
        _rowIndex = rowIndex;
    }

    public ISchema Schema { get; }

    public T GetValue<T>(string columnName) where T : allows ref struct
    {
        if (_columns.TryGetValue(columnName, out var array))
            return ReadFromBox<T>(array.GetValue(_rowIndex)!);
        throw new KeyNotFoundException($"Column '{columnName}' not found.");
    }

    public bool TryGetValue<T>(string columnName, out T value) where T : allows ref struct
    {
        if (_columns.TryGetValue(columnName, out var array))
        {
            value = ReadFromBox<T>(array.GetValue(_rowIndex)!);
            return true;
        }
        value = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ReadFromBox<T>(object boxed) where T : allows ref struct
    {
        if (typeof(T).IsValueType)
        {
            // For boxed value types: the object's first field IS the value data.
            // We use Unsafe.As to reinterpret the object as a RawBox<byte>,
            // which gives us a ref to the first byte of data (after the MethodTable).
            // Then we read T from that location.
            ref byte data = ref Unsafe.As<RawBox>(boxed).Data;
            return Unsafe.ReadUnaligned<T>(ref data);
        }

        return Unsafe.As<object, T>(ref boxed);
    }

    /// <summary>
    /// Layout-compatible helper class. When we Unsafe.As an object to this,
    /// the Data field aligns with the first byte of the object's payload
    /// (right after the MethodTable pointer).
    /// </summary>
    private sealed class RawBox
    {
        public byte Data;
    }
}
