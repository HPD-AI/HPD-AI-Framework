namespace HPD.ML.Abstractions;

/// <summary>A single row with typed column access.</summary>
public interface IRow
{
    ISchema Schema { get; }

    /// <summary>Supports ref struct T (Span, TensorSpan) via allows ref struct.</summary>
    T GetValue<T>(string columnName) where T : allows ref struct;

    bool TryGetValue<T>(string columnName, out T value) where T : allows ref struct;
}
