namespace HPD.ML.Core;

using HPD.ML.Abstractions;

public sealed record FieldType(
    Type ClrType,
    bool IsVector = false,
    IReadOnlyList<int>? Dimensions = null) : IFieldType
{
    /// <summary>Scalar field type from CLR type.</summary>
    public static FieldType Scalar<T>() => new(typeof(T));

    /// <summary>Vector field type with known dimensions.</summary>
    public static FieldType Vector<T>(params ReadOnlySpan<int> dims)
        => new(typeof(T), true, dims.ToArray());
}
