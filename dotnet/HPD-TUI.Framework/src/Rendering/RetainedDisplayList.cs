using HPD.TUI.Core;

namespace HPD.TUI.Rendering;

internal sealed class RetainedDisplayList : ISegmentSink, IRetainedDisplayListSink
{
    private List<DisplayOperation> _operations = [];
    private List<DisplayOperation> _building = [];
    private Dictionary<ComponentId, ComponentSlice> _slices = [];
    private Dictionary<ComponentId, ComponentSlice> _buildingSlices = [];
    private readonly Stack<PendingSlice> _pendingSlices = [];
    private readonly Dictionary<ComponentId, ComponentRevisions> _committedRevisions = [];
    private bool[] _damagedRows = [];
    private int _commandsBuilt;
    private int _commandsReused;
    private int _componentsPainted;
    private DisplayListKey _key;
    private int _cursorX;
    private int _cursorY;

    public int CursorX => _cursorX;
    public int CursorY => _cursorY;
    public int Count => _operations.Count;
    public ReadOnlySpan<bool> DamagedRows => _damagedRows;
    public int DamagedRowCount { get; private set; }
    public bool RequiresFullRaster { get; private set; } = true;
    public int CommandsBuilt => _commandsBuilt;
    public int CommandsReused => _commandsReused;
    public int ComponentsPainted => _componentsPainted;

    public bool Prepare(IComponent root, in RenderContext context, int maxWidth)
    {
        var dependencies = RenderContextFields.None;
        var key = new DisplayListKey(
            ComputeTreeRevision(root, ref dependencies),
            context.Width,
            context.Height,
            context.Theme.Key,
            context.ColorSystem,
            (dependencies & RenderContextFields.Elapsed) != 0 ? context.Elapsed : default);
        if (_operations.Count > 0 && key == _key && TreeMatches(root))
        {
            _commandsBuilt = 0;
            _commandsReused = _operations.Count;
            _componentsPainted = 0;
            RequiresFullRaster = false;
            DamagedRowCount = 0;
            EnsureDamageRows(context.Height);
            Array.Clear(_damagedRows);
            return true;
        }

        _building.Clear();
        _buildingSlices.Clear();
        _pendingSlices.Clear();
        _cursorX = 0;
        _cursorY = 0;
        _commandsBuilt = _commandsReused = _componentsPainted = 0;
        var writer = new DisplayListBuilder(this, maxWidth);
        Begin(root, in context, maxWidth);
        try
        {
            root.Render(in context, ref writer);
            End(root);
        }
        catch
        {
            _building.Clear();
            _buildingSlices.Clear();
            _pendingSlices.Clear();
            throw;
        }
        (_operations, _building) = (_building, _operations);
        (_slices, _buildingSlices) = (_buildingSlices, _slices);
        _committedRevisions.Clear();
        RecordTreeRevisions(root);
        _key = key;
        ComputeDamage(context.Height);
        return false;
    }

    public void Replay(ISegmentSink destination)
    {
        foreach (var operation in _operations)
        {
            switch (operation.Kind)
            {
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.TextRun:
                    destination.Write(operation.Command.Payload.Text.AsSpan(), operation.Command.Style, operation.Command.Metadata);
                    break;
                case DisplayOperationKind.LineBreak:
                    destination.WriteLineBreak();
                    break;
                case DisplayOperationKind.Move:
                    destination.MoveTo(operation.X, operation.Y);
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.SetCursor:
                    destination.SetTerminalCursor(operation.Command.Bounds.X, operation.Command.Bounds.Y);
                    break;
            }
        }
    }

    public void ReplayDamaged(ISegmentSink destination)
    {
        if (destination is HPD.TUI.Terminal.TerminalGrid grid) grid.ClearTerminalCursor();
        foreach (var operation in _operations)
        {
            if (operation.Kind != DisplayOperationKind.Command) continue;
            var command = operation.Command;
            if (command.Kind == DisplayCommandKind.SetCursor)
            {
                destination.SetTerminalCursor(command.Bounds.X, command.Bounds.Y);
                continue;
            }
            if (command.Kind != DisplayCommandKind.TextRun || !IntersectsDamage(command.Bounds)) continue;
            destination.MoveTo(command.Bounds.X, command.Bounds.Y);
            destination.Write(command.Payload.Text.AsSpan(), command.Style, command.Metadata);
        }
    }

    public bool Write(scoped ReadOnlySpan<char> text, Style style, TerminalRunMetadata metadata = default)
    {
        var ownedText = text.ToString();
        var width = Utilities.UnicodeWidth.GetWidth(text);
        var command = new DisplayCommand(
            DisplayCommandKind.TextRun,
            new Layout.LayoutRect(_cursorX, _cursorY, width, 1),
            style,
            metadata,
            DisplayPayload.FromText(ownedText));
        _building.Add(new DisplayOperation(DisplayOperationKind.Command, command, 0, 0));
        _commandsBuilt++;
        _cursorX += width;
        return true;
    }

    public bool WriteLineBreak()
    {
        _building.Add(new DisplayOperation(DisplayOperationKind.LineBreak, default, 0, 0));
        _commandsBuilt++;
        _cursorX = 0;
        _cursorY++;
        return true;
    }

    public void MoveTo(int x, int y)
    {
        _building.Add(new DisplayOperation(DisplayOperationKind.Move, default, x, y));
        _commandsBuilt++;
        _cursorX = x;
        _cursorY = y;
    }

    public void SetTerminalCursor(int x, int y)
    {
        _building.Add(new DisplayOperation(DisplayOperationKind.Command,
            new DisplayCommand(DisplayCommandKind.SetCursor, new Layout.LayoutRect(x, y, 0, 0), default, default, default), x, y));
        _commandsBuilt++;
    }

    public bool TryReuse(IComponent component, in RenderContext context, int maxWidth, out int commandCount)
    {
        var key = CreateSliceKey(component, in context, maxWidth);
        if (!_slices.TryGetValue(component.Lifecycle.Id, out var slice) ||
            slice.Key != key || slice.StartX != _cursorX || slice.StartY != _cursorY || !TreeMatches(component))
        {
            commandCount = 0;
            return false;
        }

        var start = _building.Count;
        for (var index = 0; index < slice.Count; index++)
            _building.Add(_operations[slice.Start + index]);
        _cursorX = slice.EndX;
        _cursorY = slice.EndY;
        _buildingSlices[component.Lifecycle.Id] = slice with { Start = start };
        _commandsReused += slice.Count;
        commandCount = slice.Count;
        return true;
    }

    public void Begin(IComponent component, in RenderContext context, int maxWidth)
    {
        _componentsPainted++;
        _pendingSlices.Push(new PendingSlice(component.Lifecycle.Id, CreateSliceKey(component, in context, maxWidth),
            _building.Count, _cursorX, _cursorY));
    }

    public void End(IComponent component)
    {
        if (_pendingSlices.Count == 0 || _pendingSlices.Peek().Component != component.Lifecycle.Id)
            throw new InvalidOperationException("Display-list component slices must close in ownership order.");
        var pending = _pendingSlices.Pop();
        _buildingSlices[pending.Component] = new ComponentSlice(
            pending.Key, pending.Start, _building.Count - pending.Start,
            pending.StartX, pending.StartY, _cursorX, _cursorY);
    }

    private static ComponentSliceKey CreateSliceKey(IComponent component, in RenderContext context, int maxWidth)
    {
        var dependencies = RenderContextFields.None;
        var treeRevision = ComputeTreeRevision(component, ref dependencies);
        return new ComponentSliceKey(treeRevision, maxWidth, context.Height,
            (dependencies & RenderContextFields.Theme) != 0 ? context.Theme.Key : default,
            (dependencies & RenderContextFields.ColorSystem) != 0 ? context.ColorSystem : default,
            (dependencies & RenderContextFields.Elapsed) != 0 ? context.Elapsed : default);
    }

    private bool TreeMatches(IComponent component)
    {
        if (!_committedRevisions.TryGetValue(component.Lifecycle.Id, out var revisions) ||
            revisions.Layout != component.LayoutRevision || revisions.Paint != component.PaintRevision)
            return false;
        if (component is not Component owner) return true;
        foreach (var child in owner.OwnedChildren)
            if (!TreeMatches(child)) return false;
        return true;
    }

    private void RecordTreeRevisions(IComponent component)
    {
        _committedRevisions[component.Lifecycle.Id] = new(component.LayoutRevision, component.PaintRevision);
        if (component is not Component owner) return;
        foreach (var child in owner.OwnedChildren) RecordTreeRevisions(child);
    }

    private static ulong ComputeTreeRevision(IComponent component, ref RenderContextFields dependencies)
    {
        var hash = new HashCode();
        Add(component, ref hash, ref dependencies);
        return unchecked((ulong)hash.ToHashCode());

        static void Add(IComponent current, ref HashCode hash, ref RenderContextFields dependencies)
        {
            hash.Add(current.Lifecycle.Id.Value);
            hash.Add(current.LayoutRevision.Value);
            hash.Add(current.PaintRevision.Value);
            dependencies |= current.Dependencies.Layout | current.Dependencies.Paint;
            if (current is not Component owner) return;
            foreach (var child in owner.OwnedChildren) Add(child, ref hash, ref dependencies);
        }
    }

    private void ComputeDamage(int height)
    {
        EnsureDamageRows(height);
        Array.Clear(_damagedRows);
        DamagedRowCount = 0;
        RequiresFullRaster = _building.Count == 0;
        if (RequiresFullRaster) { MarkAll(); return; }
        var common = Math.Min(_operations.Count, _building.Count);
        for (var index = 0; index < common; index++)
        {
            if (_operations[index] == _building[index]) continue;
            Mark(_operations[index]);
            Mark(_building[index]);
        }
        for (var index = common; index < _operations.Count; index++) Mark(_operations[index]);
        for (var index = common; index < _building.Count; index++) Mark(_building[index]);
        return;

        void Mark(DisplayOperation operation)
        {
            if (operation.Kind != DisplayOperationKind.Command) { RequiresFullRaster = true; MarkAll(); return; }
            var bounds = operation.Command.Bounds;
            var start = Math.Clamp(bounds.Y, 0, height);
            var end = Math.Clamp(Math.Max(bounds.Y + bounds.Height, bounds.Y + 1), 0, height);
            for (var row = start; row < end; row++)
                if (!_damagedRows[row]) { _damagedRows[row] = true; DamagedRowCount++; }
        }

        void MarkAll()
        {
            Array.Fill(_damagedRows, true);
            DamagedRowCount = height;
        }
    }

    private bool IntersectsDamage(Layout.LayoutRect bounds)
    {
        var start = Math.Clamp(bounds.Y, 0, _damagedRows.Length);
        var end = Math.Clamp(Math.Max(bounds.Bottom, bounds.Y + 1), 0, _damagedRows.Length);
        for (var row = start; row < end; row++) if (_damagedRows[row]) return true;
        return false;
    }

    private void EnsureDamageRows(int height)
    {
        if (_damagedRows.Length != height) _damagedRows = new bool[height];
    }

    private readonly record struct DisplayListKey(
        ulong TreeRevision,
        int Width,
        int Height,
        ThemeKey Theme,
        ColorSystem ColorSystem,
        TimeSpan Elapsed);

    private readonly record struct DisplayOperation(DisplayOperationKind Kind, DisplayCommand Command, int X, int Y);

    private readonly record struct ComponentSliceKey(
        ulong TreeRevision, int Width, int Height, ThemeKey Theme, ColorSystem ColorSystem, TimeSpan Elapsed);

    private readonly record struct ComponentSlice(
        ComponentSliceKey Key, int Start, int Count, int StartX, int StartY, int EndX, int EndY);

    private readonly record struct PendingSlice(
        ComponentId Component, ComponentSliceKey Key, int Start, int StartX, int StartY);

    private readonly record struct ComponentRevisions(TuiRevision Layout, TuiRevision Paint);

    private enum DisplayOperationKind { Command, LineBreak, Move }
}
