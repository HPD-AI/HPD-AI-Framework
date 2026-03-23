namespace HPD.ML.Core;

using HPD.ML.Abstractions;

public sealed record Column(
    string Name,
    IFieldType Type,
    IAnnotationSet Annotations,
    bool IsHidden = false) : IColumn
{
    public Column(string name, IFieldType type)
        : this(name, type, AnnotationSet.Empty) { }
}
