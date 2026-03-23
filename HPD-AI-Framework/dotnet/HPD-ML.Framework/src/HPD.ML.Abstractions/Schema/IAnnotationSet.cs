namespace HPD.ML.Abstractions;

/// <summary>
/// Named, typed annotations on a column. Roles (Label, Feature, Weight)
/// use the "role:" prefix. External metadata uses "onnx:", "tf:", etc.
/// </summary>
public interface IAnnotationSet
{
    IEnumerable<string> Keys { get; }
    bool TryGetValue<T>(string key, out T value);
}
