using HPD.TUI.Core;
using HPD.TUI.Controllers;
using HPD.TUI.Layout;
using HPD.TUI.Models;
using HPD.TUI.Utilities;

namespace HPD.TUI.Views;

public sealed class TableView<T> : Component, IFocusable
{
    private readonly TableModel<T> _model;
    private readonly List<TableColumn<T>> _gridColumns = [];
    private readonly List<TableColumn<T>> _stackedColumns = [];
    private bool _isFocused;
    private bool _enableCellNavigation;

    public TableView(TableModel<T> model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public TableModel<T> Model => _model;

    public GridNavigationController Navigation { get; } = new(rowCount: 0, columnCount: 0);

    /// <inheritdoc />
    public bool IsFocused { get => _isFocused; set => SetPaint(ref _isFocused, value); }

    /// <summary>Gets or sets whether directional input selects individual cells.</summary>
    public bool EnableCellNavigation { get => _enableCellNavigation; set => SetPaint(ref _enableCellNavigation, value); }

    public int StackedBreakpoint { get; init; } = 40;

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        if (_model.Columns.Count == 0)
        {
            var width = Math.Min(maxWidth, UnicodeWidth.GetWidth(_model.EmptyText));
            return new Measurement(width, width);
        }

        var natural = 0;
        foreach (var column in _model.Columns)
        {
            natural += MeasureColumn(column);
        }

        natural += Math.Max(0, (_model.Columns.Count - 1) * 2);
        natural = Math.Min(maxWidth, natural);
        return new Measurement(Math.Min(natural, maxWidth), natural);
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        if (maxWidth <= 0)
        {
            return;
        }

        if (_model.Rows.Count == 0 || _model.Columns.Count == 0)
        {
            WriteTitleAndCaption(in context, maxWidth, ref output, includeCaption: false);
            output.Write(_model.EmptyText.AsSpan(), context.Theme.Border);
            WriteCaption(in context, maxWidth, ref output);
            return;
        }

        WriteTitleAndCaption(in context, maxWidth, ref output, includeCaption: false);

        if (maxWidth < StackedBreakpoint)
        {
            Navigation.Resize(_model.Rows.Count, 1);
            RenderStacked(in context, maxWidth, ref output);
            WriteCaption(in context, maxWidth, ref output);
            return;
        }

        RenderGrid(in context, maxWidth, ref output);
        WriteCaption(in context, maxWidth, ref output);
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        if (!EnableCellNavigation)
        {
            return false;
        }

        switch (key.Key)
        {
            case KeyCode.UpArrow:
                Navigation.MoveRows(-1);
                break;
            case KeyCode.DownArrow:
                Navigation.MoveRows(1);
                break;
            case KeyCode.LeftArrow:
                Navigation.MoveColumns(-1);
                break;
            case KeyCode.RightArrow:
                Navigation.MoveColumns(1);
                break;
            case KeyCode.Home:
                Navigation.MoveToRowStart();
                break;
            case KeyCode.End:
                Navigation.MoveToRowEnd();
                break;
            case KeyCode.PageUp:
                Navigation.PageRows(-1, 10);
                break;
            case KeyCode.PageDown:
                Navigation.PageRows(1, 10);
                break;
            default:
                return false;
        }

        InvalidatePaint();
        return true;
    }

    public static TableView<T> Create(IEnumerable<T> rows, Action<TableModel<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var model = TableModel<T>.From(rows);
        configure(model);
        return new TableView<T>(model);
    }

    private void RenderGrid(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        var columns = GetVisibleGridColumns(maxWidth);
        if (columns.Count == 0)
        {
            Navigation.Resize(_model.Rows.Count, 1);
            RenderStacked(in context, maxWidth, ref output);
            return;
        }

        Navigation.Resize(_model.Rows.Count, columns.Count);

        var widths = new int[columns.Count];
        var borderWidth = _model.Border.IsVisible ? 2 : 0;
        var innerWidth = Math.Max(0, maxWidth - borderWidth);
        CalculateWidths(columns, innerWidth, widths);
        var lineWidth = GetLineWidth(widths);
        var borderStyle = _model.Border.ResolveStyle(in context);

        if (_model.Border.IsVisible)
        {
            WriteBorderLine(_model.Border.Glyphs.TopLeft, _model.Border.Glyphs.Top, _model.Border.Glyphs.TopRight, lineWidth, borderStyle, ref output);
            output.WriteLineBreak();
        }

        if (_model.ShowHeader)
        {
            WriteGridRow(columns, widths, row: default, isHeader: true, lineWidth, rowIndex: -1, in context, ref output);
            output.WriteLineBreak();

            if (_model.Border.IsVisible)
            {
                WriteBorderLine(_model.Border.Glyphs.Left, _model.Border.Glyphs.Top, _model.Border.Glyphs.Right, lineWidth, borderStyle, ref output);
                output.WriteLineBreak();
            }
        }

        for (var rowIndex = 0; rowIndex < _model.Rows.Count; rowIndex++)
        {
            WriteGridRow(columns, widths, _model.Rows[rowIndex], isHeader: false, lineWidth, rowIndex, in context, ref output);

            if (rowIndex < _model.Rows.Count - 1)
            {
                output.WriteLineBreak();
                if (_model.ShowRowSeparators && _model.Border.IsVisible)
                {
                    WriteBorderLine(_model.Border.Glyphs.Left, _model.Border.Glyphs.Top, _model.Border.Glyphs.Right, lineWidth, borderStyle, ref output);
                    output.WriteLineBreak();
                }
                else if (_model.ShowRowSeparators)
                {
                    WriteRepeated('-', lineWidth, context.Theme.Border, ref output);
                    output.WriteLineBreak();
                }
            }
        }

        if (_model.Border.IsVisible)
        {
            output.WriteLineBreak();
            WriteBorderLine(_model.Border.Glyphs.BottomLeft, _model.Border.Glyphs.Bottom, _model.Border.Glyphs.BottomRight, lineWidth, borderStyle, ref output);
        }
    }

    private void RenderStacked(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        var visibleColumns = GetStackedColumns();
        for (var rowIndex = 0; rowIndex < _model.Rows.Count; rowIndex++)
        {
            var row = _model.Rows[rowIndex];

            for (var i = 0; i < visibleColumns.Count; i++)
            {
                var column = visibleColumns[i];
                output.Write(column.Header.AsSpan(), context.Theme.Accent);
                output.Write(": ", context.Theme.Border);
                output.Write(column.Value(row).AsSpan(), context.Theme.Text);

                if (i < visibleColumns.Count - 1)
                {
                    output.WriteLineBreak();
                }
            }

            if (rowIndex < _model.Rows.Count - 1)
            {
                output.WriteLineBreak();
                output.WriteLineBreak();
            }
        }
    }

    private List<TableColumn<T>> GetStackedColumns()
    {
        _stackedColumns.Clear();
        foreach (var column in _model.Columns)
        {
            _stackedColumns.Add(column);
        }

        _stackedColumns.Sort(static (left, right) => left.Priority.CompareTo(right.Priority));
        return _stackedColumns;
    }

    private List<TableColumn<T>> GetVisibleGridColumns(int maxWidth)
    {
        _gridColumns.Clear();
        foreach (var column in _model.Columns)
        {
            _gridColumns.Add(column);
        }

        while (_gridColumns.Count > 1 && GetNaturalLineWidth(_gridColumns) > maxWidth)
        {
            var removeIndex = 0;
            var removePriority = _gridColumns[0].Priority;
            for (var i = 1; i < _gridColumns.Count; i++)
            {
                if (_gridColumns[i].Priority > removePriority)
                {
                    removePriority = _gridColumns[i].Priority;
                    removeIndex = i;
                }
            }

            _gridColumns.RemoveAt(removeIndex);
        }

        return _gridColumns;
    }

    private void CalculateWidths(IReadOnlyList<TableColumn<T>> columns, int maxWidth, Span<int> widths)
    {
        var gapWidth = Math.Max(0, (columns.Count - 1) * 2);
        var available = Math.Max(0, maxWidth - gapWidth);
        var natural = 0;
        var fillWeight = 0;

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            widths[i] = column.Width.Kind switch
            {
                SizePolicyKind.Fixed => column.Width.Value,
                _ => MeasureColumn(column)
            };
            if (column.Width.Kind == SizePolicyKind.Fill)
            {
                fillWeight += column.Width.Value;
            }

            natural += widths[i];
        }

        if (fillWeight > 0 && natural < available)
        {
            var extra = available - natural;
            var distributed = 0;
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].Width.Kind != SizePolicyKind.Fill)
                {
                    continue;
                }

                var add = extra * columns[i].Width.Value / fillWeight;
                widths[i] += add;
                distributed += add;
            }

            for (var i = widths.Length - 1; distributed < extra && i >= 0; i--)
            {
                if (columns[i].Width.Kind == SizePolicyKind.Fill)
                {
                    widths[i]++;
                    distributed++;
                }
            }

            return;
        }

        if (natural <= available)
        {
            return;
        }

        var remaining = available;
        for (var i = 0; i < widths.Length; i++)
        {
            var width = Math.Max(1, available / widths.Length);
            if (i == widths.Length - 1)
            {
                width = remaining;
            }

            widths[i] = Math.Max(1, width);
            remaining -= widths[i];
        }
    }

    private int GetNaturalLineWidth(IReadOnlyList<TableColumn<T>> columns)
    {
        var width = 0;
        foreach (var column in columns)
        {
            width += column.Width.Kind == SizePolicyKind.Fixed ? column.Width.Value : MeasureColumn(column);
        }

        width += Math.Max(0, (columns.Count - 1) * 2);
        if (_model.Border.IsVisible)
        {
            width += 2;
        }

        return width;
    }

    private static int GetLineWidth(ReadOnlySpan<int> widths)
    {
        var width = Math.Max(0, (widths.Length - 1) * 2);
        foreach (var columnWidth in widths)
        {
            width += columnWidth;
        }

        return width;
    }

    private int MeasureColumn(TableColumn<T> column)
    {
        var width = UnicodeWidth.GetWidth(column.Header);
        foreach (var row in _model.Rows)
        {
            width = Math.Max(width, UnicodeWidth.GetWidth(column.Value(row)));
        }

        return width;
    }

    private void WriteTitleAndCaption(in RenderContext context, int maxWidth, ref DisplayListBuilder output, bool includeCaption)
    {
        if (!string.IsNullOrEmpty(_model.Title))
        {
            WriteAligned(_model.Title, maxWidth, Alignment.Start, OverflowPolicy.Ellipsis, context.Theme.Accent, ref output);
            output.WriteLineBreak();
        }

        if (includeCaption)
        {
            WriteCaption(in context, maxWidth, ref output);
        }
    }

    private void WriteCaption(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        if (string.IsNullOrEmpty(_model.Caption))
        {
            return;
        }

        output.WriteLineBreak();
        WriteAligned(_model.Caption, maxWidth, Alignment.Start, OverflowPolicy.Ellipsis, context.Theme.Border, ref output);
    }

    private void WriteGridRow(
        IReadOnlyList<TableColumn<T>> columns,
        ReadOnlySpan<int> widths,
        T? row,
        bool isHeader,
        int lineWidth,
        int rowIndex,
        in RenderContext context,
        ref DisplayListBuilder output)
    {
        var borderStyle = _model.Border.ResolveStyle(in context);
        if (_model.Border.IsVisible)
        {
            output.Write(_model.Border.Glyphs.Left, borderStyle);
        }

        var written = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var value = isHeader ? column.Header : column.Value(row!);
            var style = isHeader
                ? context.Theme.Accent
                : EnableCellNavigation && rowIndex == Navigation.Row && i == Navigation.Column
                    ? context.Theme.Accent
                    : context.Theme.Text;
            WriteAligned(value, widths[i], column.Alignment, column.Overflow, style, ref output);
            written += widths[i];

            if (i < columns.Count - 1)
            {
                output.Write("  ", context.Theme.Border);
                written += 2;
            }
        }

        WriteSpaces(lineWidth - written, context.Theme.Text, ref output);
        if (_model.Border.IsVisible)
        {
            output.Write(_model.Border.Glyphs.Right, borderStyle);
        }
    }

    private static void WriteAligned(string value, int width, Alignment alignment, OverflowPolicy overflow, Style style, ref DisplayListBuilder output)
    {
        var actualWidth = Math.Min(UnicodeWidth.GetWidth(value), width);
        if (actualWidth > width)
        {
            WriteOverflow(value, width, overflow, style, ref output);
            return;
        }

        var left = alignment switch
        {
            Alignment.Center => Math.Max(0, (width - actualWidth) / 2),
            Alignment.End => Math.Max(0, width - actualWidth),
            _ => 0
        };
        WriteSpaces(left, style, ref output);
        WriteOverflow(value, width - left, overflow, style, ref output);
        WriteSpaces(width - left - actualWidth, style, ref output);
    }

    private static void WriteOverflow(string value, int width, OverflowPolicy overflow, Style style, ref DisplayListBuilder output)
    {
        var actualWidth = UnicodeWidth.GetWidth(value);
        if (actualWidth <= width)
        {
            output.Write(value.AsSpan(), style);
            return;
        }

        if (overflow == OverflowPolicy.Ellipsis && width > 1)
        {
            WriteClipped(value, width - 1, style, ref output);
            output.Write("…", style);
            return;
        }

        WriteClipped(value, width, style, ref output);
    }

    private static void WriteClipped(string value, int width, Style style, ref DisplayListBuilder output)
    {
        if (width <= 0)
        {
            return;
        }

        var used = 0;
        var enumerator = new RuneEnumerator(value.AsSpan());
        Span<char> buffer = stackalloc char[2];
        while (enumerator.MoveNext())
        {
            var runeWidth = UnicodeWidth.GetWidth(enumerator.Current);
            if (used + runeWidth > width)
            {
                break;
            }

            if (enumerator.Current.TryEncodeToUtf16(buffer, out var written))
            {
                output.Write(buffer[..written], style);
            }

            used += runeWidth;
        }
    }

    private static void WriteSpaces(int count, Style style, ref DisplayListBuilder output)
    {
        WriteRepeated(' ', count, style, ref output);
    }

    private static void WriteRepeated(char value, int count, Style style, ref DisplayListBuilder output)
    {
        if (count <= 0)
        {
            return;
        }

        Span<char> spaces = stackalloc char[Math.Min(count, 256)];
        spaces.Fill(value);
        while (count > 0)
        {
            var current = Math.Min(count, spaces.Length);
            output.Write(spaces[..current], style);
            count -= current;
        }
    }

    private static void WriteBorderLine(char left, char fill, char right, int innerWidth, Style style, ref DisplayListBuilder output)
    {
        output.Write(left, style);
        WriteRepeated(fill, innerWidth, style, ref output);
        output.Write(right, style);
    }
}
