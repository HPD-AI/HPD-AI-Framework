namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// Immutable schema with explicit merge semantics.
/// Constructed via SchemaBuilder or directly from a column list.
/// </summary>
public sealed class Schema : ISchema
{
    private readonly IReadOnlyList<IColumn> _columns;
    private readonly Dictionary<string, IColumn> _nameIndex;

    public Schema(IReadOnlyList<IColumn> columns, RefinementLevel level = RefinementLevel.Exact)
    {
        _columns = columns;
        Level = level;
        _nameIndex = new Dictionary<string, IColumn>(columns.Count);
        foreach (var col in columns)
        {
            if (!col.IsHidden)
                _nameIndex[col.Name] = col; // last-writer-wins for duplicates
        }
    }

    public IReadOnlyList<IColumn> Columns => _columns;
    public RefinementLevel Level { get; }

    public IColumn? FindByName(string name)
        => _nameIndex.GetValueOrDefault(name);

    public IColumn? FindByQualifiedName(string qualifiedName)
        => _columns.FirstOrDefault(c => $"{c.Name}:{c.Type.ClrType.Name}" == qualifiedName);

    public ISchema MergeHorizontal(ISchema other, ConflictPolicy policy)
    {
        var merged = new List<IColumn>(_columns);
        foreach (var col in other.Columns)
        {
            var existing = merged.FindIndex(c => c.Name == col.Name);
            if (existing >= 0)
            {
                if (policy == ConflictPolicy.ErrorOnConflict)
                    throw new InvalidOperationException(
                        $"Column '{col.Name}' exists in both schemas. Use LastWriterWins or rename.");

                // LastWriterWins: replace, add audit annotation
                var baseAnnotations = col.Annotations as AnnotationSet ?? AnnotationSet.Empty;
                var audited = new Column(col.Name, col.Type,
                    baseAnnotations.With("schema:shadowed-type", merged[existing].Type.ClrType.Name),
                    col.IsHidden);
                merged[existing] = audited;
            }
            else
            {
                merged.Add(col);
            }
        }

        var mergedLevel = (RefinementLevel)Math.Min((int)Level, (int)other.Level);
        return new Schema(merged, mergedLevel);
    }

    public ISchema MergeVertical(ISchema other)
    {
        if (_columns.Count != other.Columns.Count)
            throw new InvalidOperationException(
                $"Vertical merge requires same column count. Left: {_columns.Count}, Right: {other.Columns.Count}");

        for (int i = 0; i < _columns.Count; i++)
        {
            if (_columns[i].Name != other.Columns[i].Name)
                throw new InvalidOperationException(
                    $"Column name mismatch at index {i}: '{_columns[i].Name}' vs '{other.Columns[i].Name}'");
            if (_columns[i].Type.ClrType != other.Columns[i].Type.ClrType)
                throw new InvalidOperationException(
                    $"Column type mismatch for '{_columns[i].Name}': " +
                    $"{_columns[i].Type.ClrType.Name} vs {other.Columns[i].Type.ClrType.Name}");
        }
        return this; // same schema
    }

    public bool IsRefinementOf(ISchema approximate)
    {
        if (Level <= approximate.Level) return false;
        foreach (var approxCol in approximate.Columns)
        {
            var ours = FindByName(approxCol.Name);
            if (ours is null) return false;
            if (!approxCol.Type.ClrType.IsAssignableFrom(ours.Type.ClrType)) return false;
        }
        return true;
    }
}
