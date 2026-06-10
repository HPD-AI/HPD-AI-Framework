using HPD.TUI.Layout;

namespace HPD.TUI.Models;

public sealed class TableModel<T>
{
    private readonly List<TableColumn<T>> _columns = [];
    private readonly List<T> _rows = [];

    public string? Title { get; set; }

    public string? Caption { get; set; }

    public string EmptyText { get; set; } = "No rows";

    public bool ShowHeader { get; set; } = true;

    public bool ShowRowSeparators { get; set; }

    public BorderSpec Border { get; set; } = BorderSpec.None;

    public IReadOnlyList<TableColumn<T>> Columns => _columns;

    public IReadOnlyList<T> Rows => _rows;

    public TableModel<T> AddColumn(TableColumn<T> column)
    {
        _columns.Add(column ?? throw new ArgumentNullException(nameof(column)));
        return this;
    }

    public TableModel<T> AddColumn(string header, Func<T, string> value, int priority = 0)
    {
        return AddColumn(new TableColumn<T>(header, value) { Priority = priority });
    }

    public TableModel<T> AddRow(T row)
    {
        _rows.Add(row);
        return this;
    }

    public static TableModel<T> From(IEnumerable<T> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var model = new TableModel<T>();
        foreach (var row in rows)
        {
            model.AddRow(row);
        }

        return model;
    }
}

public sealed class TableColumn<T>
{
    public TableColumn(string header, Func<T, string> value)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Header { get; }

    public Func<T, string> Value { get; }

    public SizePolicy Width { get; init; } = SizePolicy.Content();

    public Alignment Alignment { get; init; } = Alignment.Start;

    public OverflowPolicy Overflow { get; init; } = OverflowPolicy.Ellipsis;

    public int Priority { get; init; }
}
