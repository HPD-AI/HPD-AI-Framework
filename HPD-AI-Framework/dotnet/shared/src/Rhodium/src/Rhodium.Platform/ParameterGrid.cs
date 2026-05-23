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

    public static ParameterGrid FromParameterSets(IEnumerable<ParameterSet> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var rows = parameters
            .Select(static parameter => new ParameterSet(
                parameter.All.ToDictionary(
                    static item => item.Name,
                    static item => item.Value,
                    StringComparer.Ordinal)))
            .ToArray();

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
        ? _rows
            .SelectMany(static row => row.All.Select(static item => item.Name))
            .Distinct(StringComparer.Ordinal)
            .ToArray()
        : _axes.Select(static axis => axis.Name).ToArray();

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
}
