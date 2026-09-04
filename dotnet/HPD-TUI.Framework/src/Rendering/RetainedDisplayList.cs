using HPD.TUI.Core;
using System.Buffers;

namespace HPD.TUI.Rendering;

internal sealed class RetainedDisplayList : ISegmentSink, IRetainedDisplayListSink, IDisplayCommandSink, IOwnedTextSink, IDisposable
{
    private PooledTextArena _textArena = new();
    private PooledTextArena _buildingTextArena = new();
    private PooledBuffer<DisplayOperation> _operations = new();
    private PooledBuffer<DisplayOperation> _building = new();
    private Dictionary<ComponentId, ComponentSlice> _slices = [];
    private Dictionary<ComponentId, ComponentSlice> _buildingSlices = [];
    private readonly Stack<PendingSlice> _pendingSlices = [];
    private readonly List<Layout.LayoutRect> _replayClips = [];
    private readonly List<Layout.LayoutRect> _buildingClips = [];
    private readonly Dictionary<ComponentId, ComponentRevisions> _committedRevisions = [];
    private Dictionary<ComponentId, RenderContextFields> _sliceDependencies = [];
    private Dictionary<ComponentId, RenderContextFields> _buildingSliceDependencies = [];
    private RenderContextFields _treeDependencies;
    private readonly HashSet<TuiSurface> _observedSurfaces = [];
    private bool _surfaceDirty;
    private int[] _rowHeads = [];
    private int[] _linkOperations = [];
    private int[] _linkNext = [];
    private int[] _candidateMarks = [];
    private int[] _candidates = [];
    private int _linkCount;
    private int _candidateEpoch;
    private int _terminalCursorOperation = -1;
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
    /// <summary>Gets the number of spatially selected operations in the most recent damaged replay.</summary>
    internal int LastReplayCandidateCount { get; private set; }

    public bool Prepare(IComponent root, in RenderContext context, int maxWidth)
    {
        var dependencies = _operations.Count == 0 ? CollectDependencies(root) : _treeDependencies;
        var key = CreateDisplayListKey(root, in context, dependencies);
        if (_operations.Count > 0 && key == _key && !_surfaceDirty)
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

        ReleaseSurfaceLeases(_building);
        _building.Clear();
        _buildingTextArena.Reset();
        _buildingSlices.Clear();
        _buildingSliceDependencies.Clear();
        _pendingSlices.Clear();
        _buildingClips.Clear();
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
        (_textArena, _buildingTextArena) = (_buildingTextArena, _textArena);
        (_slices, _buildingSlices) = (_buildingSlices, _slices);
        (_sliceDependencies, _buildingSliceDependencies) = (_buildingSliceDependencies, _sliceDependencies);
        _committedRevisions.Clear();
        RecordTreeRevisions(root);
        _treeDependencies = CollectDependencies(root);
        _key = CreateDisplayListKey(root, in context, _treeDependencies);
        BuildSpatialIndex(context.Height);
        _surfaceDirty = false;
        ComputeDamage(context.Height);
        return false;
    }

    public void Replay(ISegmentSink destination)
        => ReplayCore(destination, damagedOnly: false);

    public void ReplayDamaged(ISegmentSink destination)
    {
        if (destination is HPD.TUI.Terminal.TerminalGrid grid) grid.ClearTerminalCursor();
        var count = CollectDamagedOperations();
        LastReplayCandidateCount = count;
        Array.Sort(_candidates, 0, count);
        for (var candidate = 0; candidate < count; candidate++)
            ReplayOperation(destination, _operations[_candidates[candidate]], _damagedRows);
        if (_terminalCursorOperation >= 0)
            ReplayOperation(destination, _operations[_terminalCursorOperation], _damagedRows);
    }

    private static void ReplayOperation(ISegmentSink destination, DisplayOperation operation, bool[] damagedRows)
    {
        if (operation.Kind != DisplayOperationKind.Command) return;
        var command = operation.Command;
        switch (command.Kind)
        {
            case DisplayCommandKind.TextRun: ReplayText(destination, command, operation.Clip, damagedRows); break;
            case DisplayCommandKind.Fill: ReplayFill(destination, command, operation.Clip, damagedRows); break;
            case DisplayCommandKind.Border: ReplayBorder(destination, command, operation.Clip, damagedRows); break;
            case DisplayCommandKind.ReplaySurface: ReplaySurface(destination, command, operation.Clip, damagedRows); break;
            case DisplayCommandKind.SetCursor: destination.SetTerminalCursor(command.Bounds.X, command.Bounds.Y); break;
        }
    }

    private void ReplayCore(ISegmentSink destination, bool damagedOnly)
    {
        var clips = _replayClips;
        clips.Clear();
        foreach (var operation in _operations)
        {
            switch (operation.Kind)
            {
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.TextRun:
                    if (!damagedOnly || IntersectsDamage(EffectiveBounds(operation.Command.Bounds, CurrentClip(clips))))
                        ReplayText(destination, operation.Command, CurrentClip(clips), damagedOnly ? _damagedRows : null);
                    break;
                case DisplayOperationKind.LineBreak:
                    if (!damagedOnly) destination.WriteLineBreak();
                    break;
                case DisplayOperationKind.Move:
                    if (!damagedOnly) destination.MoveTo(operation.X, operation.Y);
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.SetCursor:
                    destination.SetTerminalCursor(operation.Command.Bounds.X, operation.Command.Bounds.Y);
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.Fill:
                    if (!damagedOnly || IntersectsDamage(EffectiveBounds(operation.Command.Bounds, CurrentClip(clips))))
                        ReplayFill(destination, operation.Command, CurrentClip(clips), damagedOnly ? _damagedRows : null);
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.Border:
                    if (!damagedOnly || IntersectsDamage(EffectiveBounds(operation.Command.Bounds, CurrentClip(clips))))
                        ReplayBorder(destination, operation.Command, CurrentClip(clips), damagedOnly ? _damagedRows : null);
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.ReplaySurface:
                    if (!damagedOnly || IntersectsDamage(EffectiveBounds(operation.Command.Bounds, CurrentClip(clips))))
                        ReplaySurface(destination, operation.Command, CurrentClip(clips), damagedOnly ? _damagedRows : null);
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.PushClip:
                    clips.Add(clips.Count == 0 ? operation.Command.Bounds : Intersect(clips[^1], operation.Command.Bounds));
                    break;
                case DisplayOperationKind.Command when operation.Command.Kind == DisplayCommandKind.PopClip:
                    if (clips.Count == 0) throw new InvalidOperationException("Display-list clip stack underflow.");
                    clips.RemoveAt(clips.Count - 1);
                    break;
            }
        }
        if (clips.Count != 0) throw new InvalidOperationException("Display-list clip stack was not balanced.");
    }

    private static void ReplayFill(ISegmentSink destination, DisplayCommand command, Layout.LayoutRect? clip, bool[]? damagedRows)
    {
        var region = clip is { } clipping ? Intersect(command.Bounds, clipping) : command.Bounds;
        if (region.IsEmpty) return;
        var glyph = command.Payload.Character ?? command.Payload.Text![0];
        Span<char> chunk = stackalloc char[64];
        chunk.Fill(glyph);
        for (var row = region.Y; row < region.Bottom; row++)
        {
            if (damagedRows is not null && (row < 0 || row >= damagedRows.Length || !damagedRows[row])) continue;
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

    private static void ReplayText(ISegmentSink destination, DisplayCommand command, Layout.LayoutRect? clip, bool[]? damagedRows)
    {
        if (damagedRows is not null && (command.Bounds.Y < 0 || command.Bounds.Y >= damagedRows.Length || !damagedRows[command.Bounds.Y]))
            return;
        if (clip is null)
        {
            destination.MoveTo(command.Bounds.X, command.Bounds.Y);
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

    private static void ReplayBorder(ISegmentSink destination, DisplayCommand command, Layout.LayoutRect? clip, bool[]? damagedRows)
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
            if (damagedRows is not null && (y < 0 || y >= damagedRows.Length || !damagedRows[y])) return;
            if (clip is { } region && (x < region.X || x >= region.Right || y < region.Y || y >= region.Bottom)) return;
            destination.MoveTo(x, y);
            Span<char> text = stackalloc char[1];
            text[0] = glyph;
            destination.Write(text, command.Style, command.Metadata);
        }
    }

    private static void ReplaySurface(ISegmentSink destination, DisplayCommand command, Layout.LayoutRect? clip, bool[]? damagedRows)
    {
        var effectiveClip = clip;
        if (damagedRows is not null)
        {
            for (var row = Math.Max(0, command.Bounds.Y); row < Math.Min(damagedRows.Length, command.Bounds.Bottom); row++)
            {
                if (!damagedRows[row]) continue;
                var rowClip = new Layout.LayoutRect(command.Bounds.X, row, command.Bounds.Width, 1);
                rowClip = effectiveClip is { } clipping ? Intersect(rowClip, clipping) : rowClip;
                if (!rowClip.IsEmpty)
                    command.Payload.SurfaceLease!.ReplayTo(destination, command.Bounds.X, command.Bounds.Y, rowClip);
            }
            return;
        }
        command.Payload.SurfaceLease!.ReplayTo(destination, command.Bounds.X, command.Bounds.Y, effectiveClip);
    }

    private static Layout.LayoutRect? CurrentClip(List<Layout.LayoutRect> clips) => clips.Count == 0 ? null : clips[^1];

    private static void WritePayload(ISegmentSink destination, DisplayPayload payload, Style style, TerminalRunMetadata metadata)
    {
        if (payload.Character is { } character)
        {
            Span<char> text = stackalloc char[1];
            text[0] = character;
            destination.Write(text, style, metadata);
        }
        else destination.Write(payload.GetTextSpan(), style, metadata);
    }

    private static Layout.LayoutRect Intersect(Layout.LayoutRect left, Layout.LayoutRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.Right, right.Right);
        var bottom = Math.Min(left.Bottom, right.Bottom);
        return new Layout.LayoutRect(x, y, Math.Max(0, rightEdge - x), Math.Max(0, bottom - y));
    }

    private static Layout.LayoutRect EffectiveBounds(Layout.LayoutRect bounds, Layout.LayoutRect? clip) =>
        clip is { } clipping ? Intersect(bounds, clipping) : bounds;

    public bool Write(scoped ReadOnlySpan<char> text, Style style, TerminalRunMetadata metadata = default)
    {
        if (_cursorY >= _maxHeight) return false;
        var width = Utilities.UnicodeWidth.GetWidth(text);
        var stored = _buildingTextArena.Append(text);
        var command = new DisplayCommand(
            DisplayCommandKind.TextRun,
            new Layout.LayoutRect(_cursorX, _cursorY, width, 1),
            style,
            metadata,
            DisplayPayload.FromArena(_buildingTextArena, stored.Offset, stored.Length));
        _building.Add(new DisplayOperation(DisplayOperationKind.Command, command, 0, 0, default, CurrentClip(_buildingClips)));
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
        _building.Add(new DisplayOperation(DisplayOperationKind.Command, command, 0, 0, default, CurrentClip(_buildingClips)));
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
        if (command.Kind == DisplayCommandKind.PopClip)
        {
            if (_buildingClips.Count == 0) throw new InvalidOperationException("Display-list clip stack underflow.");
            _buildingClips.RemoveAt(_buildingClips.Count - 1);
        }
        if (command.Kind == DisplayCommandKind.ReplaySurface)
        {
            var surface = command.Payload.Surface!;
            if (_observedSurfaces.Add(surface)) surface.RevisionChanged += HandleSurfaceRevisionChanged;
            command = command with { Payload = DisplayPayload.FromSurfaceLease(surface, surface.AcquireLease()) };
        }
        _building.Add(CreateOperation(command, CurrentClip(_buildingClips)));
        if (command.Kind == DisplayCommandKind.PushClip)
            _buildingClips.Add(_buildingClips.Count == 0 ? command.Bounds : Intersect(_buildingClips[^1], command.Bounds));
        _commandsBuilt++;
    }

    public bool TryReuse(IComponent component, in RenderContext context, int maxWidth, out int commandCount)
    {
        var dependencies = _sliceDependencies.GetValueOrDefault(component.Lifecycle.Id, component.Dependencies.Layout | component.Dependencies.Paint);
        var key = CreateSliceKey(component, in context, maxWidth, dependencies);
        if (!_slices.TryGetValue(component.Lifecycle.Id, out var slice) ||
            slice.Key != key || slice.StartX != _cursorX || slice.StartY != _cursorY ||
            !SurfaceVersionsMatch(_operations, slice.Start, slice.Count))
        {
            commandCount = 0;
            return false;
        }

        var start = _building.Count;
        for (var index = 0; index < slice.Count; index++)
        {
            var operation = _operations[slice.Start + index];
            if (operation.Command.Kind == DisplayCommandKind.ReplaySurface)
                operation = operation with { Command = operation.Command with {
                    Payload = DisplayPayload.FromSurfaceLease(operation.Command.Payload.Surface!, operation.Command.Payload.SurfaceLease!.Clone()) } };
            _building.Add(operation);
        }
        _cursorX = slice.EndX;
        _cursorY = slice.EndY;
        _buildingSlices[component.Lifecycle.Id] = slice with { Start = start };
        _buildingSliceDependencies[component.Lifecycle.Id] = dependencies;
        _commandsReused += slice.Count;
        commandCount = slice.Count;
        return true;
    }

    public void Begin(IComponent component, in RenderContext context, int maxWidth)
    {
        _componentsPainted++;
        var dependencies = CollectDependencies(component);
        _buildingSliceDependencies[component.Lifecycle.Id] = dependencies;
        _pendingSlices.Push(new PendingSlice(component.Lifecycle.Id, CreateSliceKey(component, in context, maxWidth, dependencies),
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

    private static ComponentSliceKey CreateSliceKey(IComponent component, in RenderContext context, int maxWidth, RenderContextFields dependencies)
    {
        return new ComponentSliceKey(component.LayoutRevision, component.PaintRevision, GetSurfaceIdentity(component), maxWidth, context.Height,
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

    private void RecordTreeRevisions(IComponent component)
    {
        _committedRevisions[component.Lifecycle.Id] = new(component.LayoutRevision, component.PaintRevision);
        if (component is not Component owner) return;
        foreach (var child in owner.OwnedChildren) RecordTreeRevisions(child);
    }

    private static RenderContextFields CollectDependencies(IComponent component)
    {
        var dependencies = RenderContextFields.None;
        CollectDependenciesCore(component, ref dependencies);
        return dependencies;
    }

    private static void CollectDependenciesCore(IComponent component, ref RenderContextFields dependencies)
    {
        dependencies |= component.Dependencies.Layout | component.Dependencies.Paint;
        if (component is not Component owner) return;
        foreach (var child in owner.OwnedChildren) CollectDependenciesCore(child, ref dependencies);
    }

    private static DisplayListKey CreateDisplayListKey(IComponent root, in RenderContext context, RenderContextFields dependencies) =>
        new(root.Lifecycle.Id, root.LayoutRevision, root.PaintRevision, GetSurfaceIdentity(root), context.Width, context.Height,
            (dependencies & RenderContextFields.Theme) != 0 ? context.Theme.Key : default,
            (dependencies & RenderContextFields.ColorSystem) != 0 ? context.ColorSystem : default,
            (dependencies & RenderContextFields.Capabilities) != 0 ? context.Capabilities : default,
            (dependencies & RenderContextFields.Elapsed) != 0 ? context.Elapsed : default);

    private void ComputeDamage(int height)
    {
        EnsureDamageRows(height);
        Array.Clear(_damagedRows);
        DamagedRowCount = 0;
        RequiresFullRaster = _building.Count == 0;
        if (RequiresFullRaster) { MarkAll(); return; }
        // A surface payload is intentionally a live retained object. Its command value can remain
        // structurally identical while the pixels behind it advance, so damage the old placement
        // independently of ordinary command comparison.
        foreach (var operation in _building)
            if (operation.Command.Kind == DisplayCommandKind.ReplaySurface &&
                operation.Command.Payload.Surface!.CacheRevision != operation.SurfaceRevision)
                Mark(operation);
        var common = Math.Min(_operations.Count, _building.Count);
        for (var index = 0; index < common; index++)
        {
            var current = _operations[index];
            var previous = _building[index];
            if (OperationsEqual(current, previous) &&
                (current.Command.Kind != DisplayCommandKind.ReplaySurface ||
                 current.SurfaceRevision == previous.SurfaceRevision)) continue;
            Mark(current);
            Mark(previous);
        }
        for (var index = common; index < _operations.Count; index++) Mark(_operations[index]);
        for (var index = common; index < _building.Count; index++) Mark(_building[index]);
        ExpandWrappedRows(_operations);
        ExpandWrappedRows(_building);
        return;

        void ExpandWrappedRows(PooledBuffer<DisplayOperation> operations)
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
            if (operation.Kind != DisplayOperationKind.Command) return;
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

    private static DisplayOperation CreateOperation(DisplayCommand command, Layout.LayoutRect? clip = null) =>
        new(DisplayOperationKind.Command, command, 0, 0,
            command.Kind == DisplayCommandKind.ReplaySurface ? command.Payload.Surface!.CacheRevision : default, clip);

    private static bool OperationsEqual(DisplayOperation left, DisplayOperation right)
    {
        if (left.Kind != right.Kind || left.X != right.X || left.Y != right.Y) return false;
        if (left.Command.Kind == DisplayCommandKind.ReplaySurface && right.Command.Kind == DisplayCommandKind.ReplaySurface)
            return left.Command.Kind == right.Command.Kind && left.Command.Bounds == right.Command.Bounds &&
                   left.Command.Style == right.Command.Style && left.Command.Metadata == right.Command.Metadata &&
                   ReferenceEquals(left.Command.Payload.Surface, right.Command.Payload.Surface) &&
                   left.SurfaceRevision == right.SurfaceRevision;
        return left.Command == right.Command;
    }

    private static void ReleaseSurfaceLeases(PooledBuffer<DisplayOperation> operations)
    {
        foreach (var operation in operations) operation.Command.Payload.SurfaceLease?.Dispose();
    }

    private void HandleSurfaceRevisionChanged() => _surfaceDirty = true;

    private static bool SurfaceVersionsMatch(PooledBuffer<DisplayOperation> operations, int start, int count)
    {
        for (var index = start; index < start + count; index++)
        {
            var operation = operations[index];
            if (operation.Command.Kind == DisplayCommandKind.ReplaySurface &&
                operation.Command.Payload.Surface!.CacheRevision != operation.SurfaceRevision)
                return false;
        }
        return true;
    }

    private bool IntersectsDamage(Layout.LayoutRect bounds)
    {
        var start = Math.Clamp(bounds.Y, 0, _damagedRows.Length);
        var end = Math.Clamp(Math.Max(bounds.Bottom, bounds.Y + 1), 0, _damagedRows.Length);
        for (var row = start; row < end; row++) if (_damagedRows[row]) return true;
        return false;
    }

    private void BuildSpatialIndex(int height)
    {
        EnsurePooled(ref _rowHeads, Math.Max(1, height));
        Array.Fill(_rowHeads, -1, 0, height);
        _linkCount = 0;
        _terminalCursorOperation = -1;
        for (var index = 0; index < _operations.Count; index++)
        {
            var operation = _operations[index];
            if (operation.Command.Kind == DisplayCommandKind.SetCursor) _terminalCursorOperation = index;
            if (operation.Kind != DisplayOperationKind.Command ||
                operation.Command.Kind is DisplayCommandKind.PushClip or DisplayCommandKind.PopClip) continue;
            var bounds = EffectiveBounds(operation.Command.Bounds, operation.Clip);
            var start = Math.Clamp(bounds.Y, 0, height);
            var end = Math.Clamp(Math.Max(bounds.Bottom, bounds.Y + 1), 0, height);
            for (var row = start; row < end; row++)
            {
                EnsureLinkCapacity(_linkCount + 1);
                _linkOperations[_linkCount] = index;
                _linkNext[_linkCount] = _rowHeads[row];
                _rowHeads[row] = _linkCount++;
            }
        }
        EnsurePooled(ref _candidateMarks, Math.Max(1, _operations.Count));
        EnsurePooled(ref _candidates, Math.Max(1, _operations.Count));
        // ArrayPool contents are undefined. Reset stamps whenever the index generation is rebuilt;
        // otherwise a stale value equal to the next epoch can incorrectly suppress a candidate.
        Array.Clear(_candidateMarks, 0, _operations.Count);
        _candidateEpoch = 0;
    }

    private int CollectDamagedOperations()
    {
        if (++_candidateEpoch == 0) { Array.Clear(_candidateMarks); _candidateEpoch = 1; }
        var count = 0;
        for (var row = 0; row < _damagedRows.Length; row++)
        {
            if (!_damagedRows[row]) continue;
            for (var link = _rowHeads[row]; link >= 0; link = _linkNext[link])
            {
                var operation = _linkOperations[link];
                if (_candidateMarks[operation] == _candidateEpoch) continue;
                _candidateMarks[operation] = _candidateEpoch;
                _candidates[count++] = operation;
            }
        }
        return count;
    }

    private void EnsureLinkCapacity(int required)
    {
        EnsurePooled(ref _linkOperations, required);
        EnsurePooled(ref _linkNext, required);
    }

    private static void EnsurePooled(ref int[] buffer, int required)
    {
        if (buffer.Length >= required) return;
        var replacement = ArrayPool<int>.Shared.Rent(Math.Max(required, buffer.Length == 0 ? 16 : buffer.Length * 2));
        if (buffer.Length != 0) { buffer.CopyTo(replacement, 0); ArrayPool<int>.Shared.Return(buffer); }
        buffer = replacement;
    }

    private void EnsureDamageRows(int height)
    {
        if (_damagedRows.Length != height) _damagedRows = new bool[height];
    }

    private readonly record struct DisplayListKey(
        ComponentId Component,
        TuiRevision LayoutRevision,
        TuiRevision PaintRevision,
        SurfaceIdentity Surface,
        int Width,
        int Height,
        ThemeKey Theme,
        ColorSystem ColorSystem,
        TerminalCapabilities Capabilities,
        TimeSpan Elapsed);

    private readonly record struct DisplayOperation(
        DisplayOperationKind Kind, DisplayCommand Command, int X, int Y, SurfaceRevisionIdentity SurfaceRevision = default,
        Layout.LayoutRect? Clip = null);

    private readonly record struct ComponentSliceKey(
        TuiRevision LayoutRevision, TuiRevision PaintRevision, SurfaceIdentity Surface, int Width, int Height, ThemeKey Theme,
        ColorSystem ColorSystem, TerminalCapabilities Capabilities, TimeSpan Elapsed);

    private readonly record struct SurfaceIdentity(
        SurfaceId SurfaceId, SurfaceGeneration SurfaceGeneration, AttachmentGeneration AttachmentGeneration);

    private readonly record struct ComponentSlice(
        ComponentSliceKey Key, int Start, int Count, int StartX, int StartY, int EndX, int EndY);

    private readonly record struct PendingSlice(
        ComponentId Component, ComponentSliceKey Key, int Start, int StartX, int StartY);

    private readonly record struct ComponentRevisions(TuiRevision Layout, TuiRevision Paint);

    private enum DisplayOperationKind { Command, LineBreak, Move }

    public void Dispose()
    {
        ReleaseSurfaceLeases(_operations);
        ReleaseSurfaceLeases(_building);
        _operations.Dispose();
        _building.Dispose();
        foreach (var surface in _observedSurfaces) surface.RevisionChanged -= HandleSurfaceRevisionChanged;
        Return(ref _rowHeads); Return(ref _linkOperations); Return(ref _linkNext); Return(ref _candidateMarks); Return(ref _candidates);
        _textArena.Dispose();
        _buildingTextArena.Dispose();

        static void Return(ref int[] value)
        {
            if (value.Length != 0) ArrayPool<int>.Shared.Return(value);
            value = [];
        }
    }
}
