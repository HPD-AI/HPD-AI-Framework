using HPD.TUI.Core;
using HPD.TUI.Rendering;

namespace HPD.TUI.Layout;

public sealed class Grid : Component
{
    private readonly List<GridColumn> _columns = [];
    private readonly List<GridRow> _rows = [];
    private Terminal.TerminalGrid? _scratchGrid;

    public int ColumnGap { get; init; } = 1;

    public int RowGap { get; init; }

    public IReadOnlyList<GridColumn> Columns => _columns;

    public IReadOnlyList<GridRow> Rows => _rows;

    public override ComponentDependencies Dependencies => ComponentDependencies.Static;

    public Grid AddColumn(SizePolicy width)
    {
        _columns.Add(new GridColumn(width));
        InvalidateLayout();
        return this;
    }

    public Grid AddColumn(GridColumn column)
    {
        _columns.Add(column);
        InvalidateLayout();
        return this;
    }

    public Grid AddRow(params IComponent[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        return AddRow(new GridRow(cells));
    }

    public Grid AddRow(GridRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_columns.Count == 0)
        {
            throw new InvalidOperationException("Cannot add a row before adding columns.");
        }

        if (row.Cells.Count > _columns.Count)
        {
            throw new InvalidOperationException("Row has more cells than the grid has columns.");
        }

        AdoptChildren(row.Cells);
        _rows.Add(row);
        InvalidateLayout();
        return this;
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        if (_columns.Count == 0)
        {
            return new Measurement(0, 0);
        }

        var widths = new int[_columns.Count];
        CalculateColumnWidths(in context, maxWidth, widths);

        var width = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            width += widths[i];
        }

        width += Math.Max(0, (_columns.Count - 1) * ColumnGap);
        width = Math.Min(width, maxWidth);

        var height = 0;
        Span<int> rowWidths = _columns.Count <= 32 ? stackalloc int[_columns.Count] : new int[_columns.Count];
        CalculateColumnWidths(in context, maxWidth, rowWidths);
        for (var i = 0; i < _rows.Count; i++)
        {
            height += CalculateRowHeight(in context, _rows[i], rowWidths, context.Height);
        }

        height += Math.Max(0, _rows.Count - 1) * RowGap;
        return new Measurement(Math.Min(width, maxWidth), width, height);
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        if (_columns.Count == 0 || _rows.Count == 0 || maxWidth <= 0)
        {
            return;
        }

        Span<int> widths = _columns.Count <= 32 ? stackalloc int[_columns.Count] : new int[_columns.Count];
        CalculateColumnWidths(in context, maxWidth, widths);

        var originX = output.CursorX;
        var rowY = output.CursorY;

        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var row = _rows[rowIndex];
            var rowHeight = CalculateRowHeight(in context, row, widths, Math.Max(1, context.Height - rowY));
            var x = originX;

            for (var columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
            {
                if (columnIndex < row.Cells.Count)
                {
                    RenderCell(row.Cells[columnIndex], _columns[columnIndex], new LayoutRect(x, rowY, widths[columnIndex], rowHeight), in context, ref output);
                }

                x += widths[columnIndex] + ColumnGap;
            }

            rowY += rowHeight + RowGap;
            if (rowIndex < _rows.Count - 1)
            {
                output.MoveTo(originX, rowY);
            }
        }
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        foreach (var row in _rows)
        {
            foreach (var cell in row.Cells)
            {
                if (cell.HandleInput(in key))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal void CalculateColumnWidths(in RenderContext context, int maxWidth, Span<int> destination)
    {
        if (destination.Length < _columns.Count)
        {
            throw new ArgumentException("Destination is smaller than the column count.", nameof(destination));
        }

        var available = Math.Max(0, maxWidth - Math.Max(0, (_columns.Count - 1) * ColumnGap));
        var fillWeight = 0;

        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            switch (column.Width.Kind)
            {
                case SizePolicyKind.Fixed:
                    destination[i] = Math.Min(available, column.Width.Value);
                    available -= destination[i];
                    break;
                case SizePolicyKind.Content:
                    destination[i] = Math.Min(available, MeasureContentColumn(in context, i));
                    available -= destination[i];
                    break;
                case SizePolicyKind.Fill:
                    destination[i] = 0;
                    fillWeight += column.Width.Value;
                    break;
            }
        }

        if (fillWeight <= 0 || available <= 0)
        {
            return;
        }

        var remaining = available;
        for (var i = 0; i < _columns.Count; i++)
        {
            if (_columns[i].Width.Kind != SizePolicyKind.Fill)
            {
                continue;
            }

            var width = available * _columns[i].Width.Value / fillWeight;
            if (width == 0)
            {
                width = 1;
            }

            width = Math.Min(width, remaining);
            destination[i] = width;
            remaining -= width;
        }

        for (var i = 0; remaining > 0 && i < _columns.Count; i++)
        {
            if (_columns[i].Width.Kind != SizePolicyKind.Fill)
            {
                continue;
            }

            destination[i]++;
            remaining--;
        }
    }

    private int MeasureContentColumn(in RenderContext context, int columnIndex)
    {
        var width = 0;
        foreach (var row in _rows)
        {
            if (columnIndex >= row.Cells.Count)
            {
                continue;
            }

            var padding = _columns[columnIndex].Padding.Horizontal;
            width = Math.Max(width, MeasureChild(row.Cells[columnIndex], in context, context.Width).MaxWidth + padding);
        }

        return width;
    }

    private int CalculateRowHeight(in RenderContext context, GridRow row, ReadOnlySpan<int> widths, int maxHeight)
    {
        if (row.Height.Kind == SizePolicyKind.Fixed)
        {
            return Math.Max(1, Math.Min(row.Height.Value, maxHeight));
        }

        var height = row.Height.Kind == SizePolicyKind.Fixed ? row.Height.Value : 1;
        for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
        {
            var column = _columns[columnIndex];
            var cellWidth = Math.Max(0, widths[columnIndex] - column.Padding.Horizontal);
            if (cellWidth <= 0)
            {
                continue;
            }

            var measurement = MeasureChild(row.Cells[columnIndex], in context, cellWidth);
            height = Math.Max(height, measurement.Height + column.Padding.Vertical);
        }

        return Math.Max(1, Math.Min(height, maxHeight));
    }

    private void RenderCell(IComponent cell, GridColumn column, LayoutRect rect, in RenderContext context, ref DisplayListBuilder output)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        var contentRect = rect.Inset(column.Padding);
        if (contentRect.IsEmpty)
        {
            return;
        }

        var grid = RentScratchGrid(contentRect.Width, contentRect.Height);
        TuiCapture.RenderToGrid(cell, grid, context.Theme, context.ColorSystem, context.Elapsed);
        var usedLines = Math.Min(contentRect.Height, TuiCapture.GetUsedLineCount(grid));

        for (var y = 0; y < usedLines; y++)
        {
            var lineWidth = MeasureCapturedLine(grid, y);
            var offset = column.Alignment switch
            {
                Alignment.Center => Math.Max(0, (contentRect.Width - lineWidth) / 2),
                Alignment.End => Math.Max(0, contentRect.Width - lineWidth),
                _ => 0
            };

            output.MoveTo(contentRect.X + offset, contentRect.Y + y);
            WriteCapturedLineTo(grid, y, Math.Max(0, contentRect.Width - offset), ref output);
        }
    }

    private Terminal.TerminalGrid RentScratchGrid(int width, int height)
    {
        if (_scratchGrid is { } grid &&
            grid.Width == width &&
            grid.Height == height)
        {
            return grid;
        }

        _scratchGrid?.Dispose();
        _scratchGrid = new Terminal.TerminalGrid(width, height);
        return _scratchGrid;
    }

    private static int MeasureCapturedLine(Terminal.TerminalGrid grid, int y)
    {
        var width = 0;
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = grid.GetCell(x, y);
            if (cell.IsContinuation)
            {
                continue;
            }

            if (!grid.GetGrapheme(cell).SequenceEqual(" "))
            {
                width = x + 1;
            }
        }

        return width;
    }

    private static void WriteCapturedLineTo(Terminal.TerminalGrid grid, int y, int maxWidth, ref DisplayListBuilder output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var writtenWidth = 0;
        for (var x = 0; x < grid.Width && writtenWidth < maxWidth; x++)
        {
            var cell = grid.GetCell(x, y);
            if (cell.IsContinuation)
            {
                continue;
            }

            output.Write(
                grid.GetGrapheme(cell),
                cell.Style,
                new TerminalRunMetadata(grid.GetHyperlink(cell)));
            writtenWidth += cell.DisplayWidth;
        }
    }
}

public readonly record struct GridColumn(SizePolicy Width)
{
    public Thickness Padding { get; init; }

    public Alignment Alignment { get; init; } = Alignment.Start;
}

public sealed class GridRow
{
    private readonly IComponent[] _cells;

    public GridRow(IComponent[] cells)
    {
        _cells = cells;
    }

    public IReadOnlyList<IComponent> Cells => _cells;

    public SizePolicy Height { get; init; } = SizePolicy.Content();
}
