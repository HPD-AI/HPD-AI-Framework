namespace HPD.ML.Abstractions;

public interface IFieldType
{
    Type ClrType { get; }
    bool IsVector { get; }
    IReadOnlyList<int>? Dimensions { get; }
}
