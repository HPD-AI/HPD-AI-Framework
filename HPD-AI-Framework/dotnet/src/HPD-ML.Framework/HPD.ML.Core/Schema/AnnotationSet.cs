namespace HPD.ML.Core;

using HPD.ML.Abstractions;

public sealed class AnnotationSet : IAnnotationSet
{
    public static readonly AnnotationSet Empty = new([]);

    private readonly Dictionary<string, object> _values;

    public AnnotationSet(Dictionary<string, object> values) => _values = values;

    public IEnumerable<string> Keys => _values.Keys;

    public bool TryGetValue<T>(string key, out T value)
    {
        if (_values.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Returns a new AnnotationSet with the added key-value pair.</summary>
    public AnnotationSet With<T>(string key, T value)
    {
        var copy = new Dictionary<string, object>(_values) { [key] = value! };
        return new AnnotationSet(copy);
    }
}
