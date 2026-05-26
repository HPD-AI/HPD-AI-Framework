namespace Rhodium.Platform;

public sealed class ParameterGrid
{
    private readonly List<ParameterAxis> _axes = [];
    private readonly ParameterSet[]? _rows;
    private int _count = 1;

    private ParameterGrid()
    {
    }

    private ParameterGrid(ParameterSet[] rows)
    {
        _rows = rows;
        _count = rows.Length;
    }

    public static ParameterGrid Create() => new();

    public static ParameterGrid FromParameterSets(IReadOnlyList<ParameterSet> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var rows = new ParameterSet[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            var values = new Dictionary<string, object>(parameters[i].Count, StringComparer.Ordinal);
            parameters[i].CopyTo(values);
            rows[i] = values.Count == 0 ? ParameterSet.Empty : new ParameterSet(values);
        }

        return new ParameterGrid(rows);
    }

    public ParameterGrid Add<T>(string name, params T[] values)
        where T : notnull
    {
        if (_rows is not null)
            throw new InvalidOperationException("Exact-row parameter grids cannot be extended with Cartesian axes.");

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
            throw new ArgumentException("Parameter axis must contain at least one value.", nameof(values));
        if (_axes.Any(axis => string.Equals(axis.Name, name, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Parameter '{name}' is already defined.");

        _axes.Add(new ParameterAxis(name, values.Cast<object>().ToArray()));
        checked
        {
            _count *= values.Length;
        }

        return this;
    }

    public int Count => _rows is not null ? _rows.Length : _axes.Count == 0 ? 0 : _count;

    public IReadOnlyList<string> ParameterNames => _rows is not null
        ? GetExactRowParameterNames()
        : GetAxisParameterNames();

    public ParameterSet GetParametersForVariant(int variantIndex)
    {
        if ((uint)variantIndex >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(variantIndex));

        if (_rows is not null)
            return _rows[variantIndex];

        var values = new Dictionary<string, object>(_axes.Count, StringComparer.Ordinal);
        var divisor = 1;
        foreach (var axis in _axes)
        {
            var axisIndex = variantIndex / divisor % axis.Values.Length;
            values[axis.Name] = axis.Values[axisIndex];
            divisor *= axis.Values.Length;
        }

        return new ParameterSet(values);
    }

    private readonly record struct ParameterAxis(string Name, object[] Values);

    private string[] GetExactRowParameterNames()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _rows!.Length; i++)
            _rows[i].AddNamesTo(names, seen);

        return names.ToArray();
    }

    private string[] GetAxisParameterNames()
    {
        var names = new string[_axes.Count];
        for (var i = 0; i < _axes.Count; i++)
            names[i] = _axes[i].Name;

        return names;
    }
}
