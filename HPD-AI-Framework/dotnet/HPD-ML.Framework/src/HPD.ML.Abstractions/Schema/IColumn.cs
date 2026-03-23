namespace HPD.ML.Abstractions;

public interface IColumn
{
    string Name { get; }
    IFieldType Type { get; }
    IAnnotationSet Annotations { get; }
    bool IsHidden { get; }
}
