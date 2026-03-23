namespace HPD.ML.Core;

using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;

/// <summary>
/// Simple IRow backed by a dictionary. Used by transforms to produce output rows.
/// </summary>
public sealed class DictionaryRow : IRow
{
    private readonly Dictionary<string, object> _values;

    public DictionaryRow(ISchema schema, Dictionary<string, object> values)
    {
        Schema = schema;
        _values = values;
    }

    public ISchema Schema { get; }

    public T GetValue<T>(string columnName) where T : allows ref struct
    {
        if (!_values.TryGetValue(columnName, out var val))
            throw new KeyNotFoundException($"Column '{columnName}' not found.");
        return Coerce<T>(val);
    }

    public bool TryGetValue<T>(string columnName, out T value) where T : allows ref struct
    {
        if (_values.TryGetValue(columnName, out var val))
        {
            value = Coerce<T>(val);
            return true;
        }
        value = default!;
        return false;
    }

    private static T Coerce<T>(object val) where T : allows ref struct
    {
        if (typeof(T) == typeof(object))
            return Unsafe.As<object, T>(ref val);

        if (typeof(T) == typeof(int))
        {
            int v = val is int i ? i : Convert.ToInt32(val);
            return Unsafe.As<int, T>(ref v);
        }
        if (typeof(T) == typeof(float))
        {
            float v = val switch
            {
                float f => f,
                int i => i,
                double d => (float)d,
                long l => l,
                _ => Convert.ToSingle(val)
            };
            return Unsafe.As<float, T>(ref v);
        }
        if (typeof(T) == typeof(double))
        {
            double v = val switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                _ => Convert.ToDouble(val)
            };
            return Unsafe.As<double, T>(ref v);
        }
        if (typeof(T) == typeof(long))
        {
            long v = val is long l ? l : Convert.ToInt64(val);
            return Unsafe.As<long, T>(ref v);
        }
        if (typeof(T) == typeof(uint))
        {
            uint v = val is uint u ? u : Convert.ToUInt32(val);
            return Unsafe.As<uint, T>(ref v);
        }
        if (typeof(T) == typeof(bool))
        {
            bool v = val is bool b ? b : Convert.ToBoolean(val);
            return Unsafe.As<bool, T>(ref v);
        }
        if (typeof(T) == typeof(string))
        {
            var v = val is string s ? s : val?.ToString() ?? "";
            return Unsafe.As<string, T>(ref v);
        }

        // Reference types: float[], byte[], string[], etc.
        if (!typeof(T).IsValueType && val is T)
            return Unsafe.As<object, T>(ref val);

        throw new InvalidCastException($"Cannot convert {val?.GetType().Name} to {typeof(T).Name}.");
    }
}
