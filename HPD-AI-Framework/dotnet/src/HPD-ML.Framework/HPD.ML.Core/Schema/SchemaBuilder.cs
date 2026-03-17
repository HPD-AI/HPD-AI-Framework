namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// Fluent builder for constructing schemas.
/// </summary>
public sealed class SchemaBuilder
{
    private readonly List<IColumn> _columns = [];
    private RefinementLevel _level = RefinementLevel.Exact;

    public SchemaBuilder AddColumn(string name, IFieldType type, IAnnotationSet? annotations = null)
    {
        _columns.Add(new Column(name, type, annotations ?? AnnotationSet.Empty));
        return this;
    }

    public SchemaBuilder AddColumn<T>(string name) where T : unmanaged
    {
        _columns.Add(new Column(name, FieldType.Scalar<T>()));
        return this;
    }

    public SchemaBuilder AddVectorColumn<T>(string name, params ReadOnlySpan<int> dims) where T : unmanaged
    {
        _columns.Add(new Column(name, FieldType.Vector<T>(dims)));
        return this;
    }

    /// <summary>Add a role annotation (Label, Feature, Weight, GroupId).</summary>
    public SchemaBuilder AddColumn<T>(string name, string role) where T : unmanaged
    {
        var annotations = AnnotationSet.Empty.With($"role:{role}", true);
        _columns.Add(new Column(name, FieldType.Scalar<T>(), annotations));
        return this;
    }

    public SchemaBuilder WithLevel(RefinementLevel level)
    {
        _level = level;
        return this;
    }

    public Schema Build() => new(_columns, _level);
}
