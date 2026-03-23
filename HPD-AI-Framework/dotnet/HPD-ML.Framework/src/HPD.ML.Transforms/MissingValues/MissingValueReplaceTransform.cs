namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Replaces missing/NaN/null values with a replacement value.
/// </summary>
public sealed class MissingValueReplaceTransform : ITransform
{
    private readonly string _columnName;
    private readonly ReplacementValue _replacement;

    public MissingValueReplaceTransform(string columnName, ReplacementValue replacement)
    {
        _columnName = columnName;
        _replacement = replacement;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;

    public IDataHandle Apply(IDataHandle input)
    {
        return new CursorDataHandle(
            input.Schema,
            columns => new MappedCursor(
                input.GetCursor(columns),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in input.Schema.Columns)
                    {
                        var val = row.GetValue<object>(col.Name);
                        if (col.Name == _columnName && IsMissing(val))
                            values[col.Name] = _replacement.Value;
                        else
                            values[col.Name] = val;
                    }
                    return new DictionaryRow(input.Schema, values);
                }),
            input.RowCount,
            input.Ordering);
    }

    internal static bool IsMissing(object? value)
        => value is null
            || (value is float f && float.IsNaN(f))
            || (value is double d && double.IsNaN(d))
            || (value is string s && string.IsNullOrWhiteSpace(s));
}

/// <summary>
/// Replacement value for missing data.
/// </summary>
public sealed record ReplacementValue(object Value)
{
    public static ReplacementValue Constant(object value) => new(value);
    public static ReplacementValue Zero => new(0f);
}

/// <summary>
/// Fits a MissingValueReplaceTransform by computing mean/median/mode.
/// </summary>
public sealed class MissingValueReplaceLearner : ILearner
{
    private readonly string _columnName;
    private readonly ReplacementStrategy _strategy;

    public MissingValueReplaceLearner(string columnName, ReplacementStrategy strategy = ReplacementStrategy.Mean)
    {
        _columnName = columnName;
        _strategy = strategy;
    }

    public IObservable<ProgressEvent> Progress => new ProgressSubject();
    public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;

    public IModel Fit(LearnerInput input)
    {
        var values = new List<float>();
        using var cursor = input.TrainData.GetCursor([_columnName]);
        while (cursor.MoveNext())
        {
            var val = cursor.Current.GetValue<object>(_columnName);
            if (val is float f && !float.IsNaN(f)) values.Add(f);
            else if (val is double d && !double.IsNaN(d)) values.Add((float)d);
            else if (val is int i) values.Add(i);
        }

        float replacement = _strategy switch
        {
            ReplacementStrategy.Mean => values.Count > 0 ? values.Average() : 0f,
            ReplacementStrategy.Median => values.Count > 0 ? Median(values) : 0f,
            ReplacementStrategy.Mode => values.Count > 0 ? Mode(values) : 0f,
            _ => 0f
        };

        var transform = new MissingValueReplaceTransform(_columnName, ReplacementValue.Constant(replacement));
        return new Model(transform, new MissingValueParameters(replacement));
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.FromResult(Fit(input));

    private static float Median(List<float> vals)
    {
        vals.Sort();
        int mid = vals.Count / 2;
        return vals.Count % 2 == 0
            ? (vals[mid - 1] + vals[mid]) / 2f
            : vals[mid];
    }

    private static float Mode(List<float> vals)
        => vals.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;
}

public enum ReplacementStrategy { Mean, Median, Mode }
public sealed record MissingValueParameters(float Replacement) : ILearnedParameters;
