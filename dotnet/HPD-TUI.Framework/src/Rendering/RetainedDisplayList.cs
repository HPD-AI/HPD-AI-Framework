using HPD.TUI.Core;

namespace HPD.TUI.Rendering;

internal sealed class RetainedDisplayList : ISegmentSink, IRetainedDisplayListSink, IDisplayCommandSink, IOwnedTextSink
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
    private int _maxHeight;

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
            GetSurfaceIdentity(root),
            context.Width,
            context.Height,
            context.Theme.Key,
            context.ColorSystem,
            (dependencies & RenderContextFields.Capabilities) != 0 ? context.Capabilities : default,
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
        _maxHeight = context.Height;
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
        var clips = new Stack<Layout.LayoutRect>();
        foreach (var operation in _operations)
        {
            switch (operation.Kind)
            {
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.TextRun:
                    ReplayText(destination, operation.Command, CurrentClip(clips));
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
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.Fill:
                    ReplayFill(destination, operation.Command, CurrentClip(clips));
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.Border:
                    ReplayBorder(destination, operation.Command, CurrentClip(clips));
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.ReplaySurface:
                    operation.Command.Payload.Surface!.ReplayTo(destination, operation.Command.Bounds.X,
                        operation.Command.Bounds.Y, CurrentClip(clips));
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.PushClip:
                    clips.Push(clips.Count == 0 ? operation.Command.Bounds : Intersect(clips.Peek(), operation.Command.Bounds));
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.PopClip:
                    if (clips.Count == 0) throw new InvalidOperationException("Display-list clip stack underflow.");
                    clips.Pop();
                    break;
            }
        }
        if (clips.Count != 0) throw new InvalidOperationException("Display-list clip stack was not balanced.");
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
            WritePayload(destination, command.Payload, command.Style, command.Metadata);
        }
    }

    private static void ReplayFill(ISegmentSink destination, DisplayCommand command, Layout.LayoutRect? clip)
    {
        var region = clip is { } clipping ? Intersect(command.Bounds, clipping) : command.Bounds;
        if (region.IsEmpty) return;
        var glyph = command.Payload.Character ?? command.Payload.Text![0];
        Span<char> chunk = stackalloc char[64];
        chunk.Fill(glyph);
        for (var row = region.Y; row < region.Bottom; row++)
        {
            destination.MoveTo(region.X, row);
            var remaining = region.Width;
            while (remaining > 0)
            {
                var count = Math.Min(remaining, chunk.Length);
                destination.Write(chunk[..count], command.Style, command.Metadata);
                remaining -= count;
            }
        }
    }

    private static void ReplayText(ISegmentSink destination, DisplayCommand command, Layout.LayoutRect? clip)
    {
        if (clip is null)
        {
            WritePayload(destination, command.Payload, command.Style, command.Metadata);
            return;
        }
        var region = clip.Value;
        if (command.Bounds.Y < region.Y || command.Bounds.Y >= region.Bottom) return;
        Span<char> character = stackalloc char[1];
        character[0] = command.Payload.Character.GetValueOrDefault();
        var text = command.Payload.Character.HasValue ? character : command.Payload.Text.AsSpan();
        var x = command.Bounds.X;
        while (!text.IsEmpty)
        {
            var length = System.Globalization.StringInfo.GetNextTextElementLength(text);
            var grapheme = text[..length];
            var width = Math.Max(0, Utilities.UnicodeWidth.GetWidth(grapheme));
            if (x >= region.X && x + width <= region.Right)
            {
                destination.MoveTo(x, command.Bounds.Y);
                destination.Write(grapheme, command.Style, command.Metadata);
            }
            x += width;
            text = text[length..];
        }
    }

    private static void ReplayBorder(ISegmentSink destination, DisplayCommand command, Layout.LayoutRect? clip)
    {
        var bounds = command.Bounds;
        if (bounds.IsEmpty) return;
        var glyph = command.Payload.Character ?? command.Payload.Text![0];
        for (var x = bounds.X; x < bounds.Right; x++)
        {
            Write(x, bounds.Y);
            if (bounds.Height > 1) Write(x, bounds.Bottom - 1);
        }
        for (var y = bounds.Y + 1; y < bounds.Bottom - 1; y++)
        {
            Write(bounds.X, y);
            if (bounds.Width > 1) Write(bounds.Right - 1, y);
        }
        void Write(int x, int y)
        {
            if (clip is { } region && (x < region.X || x >= region.Right || y < region.Y || y >= region.Bottom)) return;
            destination.MoveTo(x, y);
            Span<char> text = stackalloc char[1];
            text[0] = glyph;
            destination.Write(text, command.Style, command.Metadata);
        }
    }

    private static Layout.LayoutRect? CurrentClip(Stack<Layout.LayoutRect> clips) => clips.Count == 0 ? null : clips.Peek();

    private static void WritePayload(ISegmentSink destination, DisplayPayload payload, Style style, TerminalRunMetadata metadata)
    {
        if (payload.Character is { } character)
        {
            Span<char> text = stackalloc char[1];
            text[0] = character;
            destination.Write(text, style, metadata);
        }
        else destination.Write(payload.Text.AsSpan(), style, metadata);
    }

    private static Layout.LayoutRect Intersect(Layout.LayoutRect left, Layout.LayoutRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.Right, right.Right);
        var bottom = Math.Min(left.Bottom, right.Bottom);
        return new Layout.LayoutRect(x, y, Math.Max(0, rightEdge - x), Math.Max(0, bottom - y));
    }

    public bool Write(scoped ReadOnlySpan<char> text, Style style, TerminalRunMetadata metadata = default)
    {
        if (_cursorY >= _maxHeight) return false;
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

    public bool WriteOwned(string text, Style style, TerminalRunMetadata metadata)
    {
        if (_cursorY >= _maxHeight) return false;
        var width = Utilities.UnicodeWidth.GetWidth(text.AsSpan());
        RecordText(DisplayPayload.FromText(text), width, style, metadata);
        return true;
    }

    public bool WriteCharacter(char value, Style style, TerminalRunMetadata metadata = default)
    {
        if (_cursorY >= _maxHeight) return false;
        Span<char> text = stackalloc char[1];
        text[0] = value;
        var width = Utilities.UnicodeWidth.GetWidth(text);
        RecordText(DisplayPayload.FromCharacter(value), width, style, metadata);
        return true;
    }

    private void RecordText(DisplayPayload payload, int width, Style style, TerminalRunMetadata metadata)
    {
        var command = new DisplayCommand(DisplayCommandKind.TextRun,
            new Layout.LayoutRect(_cursorX, _cursorY, width, 1), style, metadata, payload);
        _building.Add(new DisplayOperation(DisplayOperationKind.Command, command, 0, 0));
        _commandsBuilt++;
        _cursorX += width;
    }

    public bool WriteLineBreak()
    {
        if (_cursorY >= _maxHeight) return false;
        _building.Add(new DisplayOperation(DisplayOperationKind.LineBreak, default, 0, 0));
        _commandsBuilt++;
        _cursorX = 0;
        _cursorY++;
        return _cursorY < _maxHeight;
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

    public void RecordCommand(DisplayCommand command)
    {
        _building.Add(new DisplayOperation(DisplayOperationKind.Command, command, 0, 0));
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
        return new ComponentSliceKey(treeRevision, GetSurfaceIdentity(component), maxWidth, context.Height,
            (dependencies & RenderContextFields.Theme) != 0 ? context.Theme.Key : default,
            (dependencies & RenderContextFields.ColorSystem) != 0 ? context.ColorSystem : default,
            (dependencies & RenderContextFields.Capabilities) != 0 ? context.Capabilities : default,
            (dependencies & RenderContextFields.Elapsed) != 0 ? context.Elapsed : default);
    }

    private static SurfaceIdentity GetSurfaceIdentity(IComponent component)
    {
        var attachment = component.Lifecycle.Attachment;
        return attachment is { } value
            ? new SurfaceIdentity(value.SurfaceId, value.CurrentSurfaceGeneration(), value.AttachmentGeneration)
            : default;
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
        ExpandWrappedRows(_operations);
        ExpandWrappedRows(_building);
        return;

        void ExpandWrappedRows(List<DisplayOperation> operations)
        {
            if (_key.Width <= 0) return;
            foreach (var operation in operations)
            {
                if (operation.Kind != DisplayOperationKind.Command ||
                    operation.Command.Kind != DisplayCommandKind.TextRun) continue;
                var bounds = operation.Command.Bounds;
                if (bounds.Y < 0 || bounds.Y >= height || !_damagedRows[bounds.Y] || bounds.Right <= _key.Width) continue;
                var lastRow = Math.Clamp(bounds.Y + ((bounds.Right - 1) / _key.Width), 0, height - 1);
                for (var row = bounds.Y + 1; row <= lastRow; row++)
                    if (!_damagedRows[row]) { _damagedRows[row] = true; DamagedRowCount++; }
            }
        }

        void Mark(DisplayOperation operation)
        {
            if (operation.Kind != DisplayOperationKind.Command) { RequiresFullRaster = true; MarkAll(); return; }
            if (operation.Command.Kind is not DisplayCommandKind.TextRun and not DisplayCommandKind.SetCursor)
            {
                RequiresFullRaster = true;
                MarkAll();
                return;
            }
            var bounds = operation.Command.Bounds;
            var start = Math.Clamp(bounds.Y, 0, height);
            var wrappedRows = operation.Command.Kind == DisplayCommandKind.TextRun && _key.Width > 0
                ? Math.Max(1, (Math.Max(0, bounds.X) + Math.Max(1, bounds.Width) + _key.Width - 1) / _key.Width)
                : Math.Max(bounds.Height, 1);
            var end = Math.Clamp(bounds.Y + wrappedRows, 0, height);
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
        SurfaceIdentity Surface,
        int Width,
        int Height,
        ThemeKey Theme,
        ColorSystem ColorSystem,
        TerminalCapabilities Capabilities,
        TimeSpan Elapsed);

    private readonly record struct DisplayOperation(DisplayOperationKind Kind, DisplayCommand Command, int X, int Y);

    private readonly record struct ComponentSliceKey(
        ulong TreeRevision, SurfaceIdentity Surface, int Width, int Height, ThemeKey Theme,
        ColorSystem ColorSystem, TerminalCapabilities Capabilities, TimeSpan Elapsed);

    private readonly record struct SurfaceIdentity(
        SurfaceId SurfaceId, SurfaceGeneration SurfaceGeneration, AttachmentGeneration AttachmentGeneration);

    private readonly record struct ComponentSlice(
        ComponentSliceKey Key, int Start, int Count, int StartX, int StartY, int EndX, int EndY);

    private readonly record struct PendingSlice(
        ComponentId Component, ComponentSliceKey Key, int Start, int StartX, int StartY);

    private readonly record struct ComponentRevisions(TuiRevision Layout, TuiRevision Paint);

    private enum DisplayOperationKind { Command, LineBreak, Move }
}
