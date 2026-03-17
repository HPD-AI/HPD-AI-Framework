namespace HPD.ML.Evaluation;

using HPD.ML.Abstractions;

/// <summary>
/// Shared helpers for safe type coercion in metric transforms.
/// </summary>
internal static class MetricHelpers
{
    /// <summary>Convert a row value to bool, handling bool/float/double/int/uint.</summary>
    internal static bool ToBool(IRow row, string column)
    {
        // Get raw object to avoid DictionaryRow's Convert.ToBoolean coercion
        // which treats any non-zero numeric as true
        var raw = row.GetValue<object>(column);
        return raw switch
        {
            bool b => b,
            float f => f > 0.5f,
            double d => d > 0.5,
            int i => i != 0,
            uint u => u != 0,
            _ => Convert.ToBoolean(raw)
        };
    }

    /// <summary>Convert a row value to double, handling float/double/int/uint.</summary>
    internal static double ToDouble(IRow row, string column)
    {
        if (row.TryGetValue<double>(column, out var d)) return d;
        if (row.TryGetValue<float>(column, out var f)) return f;
        if (row.TryGetValue<int>(column, out var i)) return i;
        if (row.TryGetValue<uint>(column, out var u)) return u;
        return Convert.ToDouble(row.GetValue<object>(column));
    }

    /// <summary>Convert a row value to int.</summary>
    internal static int ToInt(IRow row, string column)
    {
        if (row.TryGetValue<int>(column, out var i)) return i;
        if (row.TryGetValue<uint>(column, out var u)) return (int)u;
        if (row.TryGetValue<float>(column, out var f)) return (int)f;
        if (row.TryGetValue<double>(column, out var d)) return (int)d;
        return Convert.ToInt32(row.GetValue<object>(column));
    }

    /// <summary>Read a double metric value from a single-row metrics DataHandle.</summary>
    internal static double ReadMetric(IDataHandle metrics, string metricName)
    {
        using var cursor = metrics.GetCursor([metricName]);
        if (cursor.MoveNext() && cursor.Current.TryGetValue<double>(metricName, out var val))
            return val;
        return 0;
    }
}
