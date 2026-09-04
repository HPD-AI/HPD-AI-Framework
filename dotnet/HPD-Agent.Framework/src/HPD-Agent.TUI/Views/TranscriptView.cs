using System.Diagnostics;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Observability;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Markdown;
using HPD.TUI.Core;
using HPD.TUI.Observability;
using HPD.TUI.Markdown;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.Agent.TUI.Views;

public sealed class TranscriptView : IComponent, IScrollbackSource
{
    private readonly TranscriptModel _model;
    private readonly AgentTuiTranscriptRendererRegistry _renderers;
    private readonly AgentTuiRuntimeScope? _scope;
    private readonly IHpdTuiPerformanceEventSink? _performanceSink;
    private TranscriptSequence _entries = TranscriptSequence.Empty;
    private readonly List<RenderedTranscriptEntry?> _renderedEntries = [];
    private readonly List<VisibleTranscriptRow> _visibleRows = [];
    private int _modelVersion = -1;
    private int _renderWidth;
    private ThemeKey _renderThemeKey;
    private Theme _renderTheme = Theme.Default;
    private ColorSystem _renderColorSystem;
    private bool _cacheInitialized;
    private bool _disposed;
    private int _scrollOffset;
    private int _committedCount;
    private long _committedRowSequence;
    private ScrollbackBatch? _pendingScrollback;
    private int _pendingEntryCount;

    public TranscriptView(
        TranscriptModel model,
        AgentTuiTranscriptRendererRegistry renderers,
        int height = 15,
        AgentTuiRuntimeScope? scope = null,
        IHpdTuiPerformanceEventSink? performanceSink = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(renderers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _model = model;
        _renderers = renderers;
        _scope = scope;
        _performanceSink = performanceSink;
        Height = height;
    }

    public int Height { get; set; }

    /// <summary>
    /// Gets the number of rendered transcript rows between the viewport and the newest row.
    /// </summary>
    public int ScrollOffset => _scrollOffset;

    public TranscriptViewDiagnostics LastDiagnostics { get; private set; } = TranscriptViewDiagnostics.Empty;

    public Measurement Measure(in RenderContext context, int maxWidth)
        => new(Math.Min(maxWidth, 20), maxWidth, Height);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        RenderRows(in context, maxWidth, ref output);
    }

    public bool HandleInput(in TuiInputEvent key)
    {
        if (_model.HistoryPresentation == TranscriptHistoryPresentation.TerminalScrollback)
            return false;

        if (key.Key is KeyCode.PageUp or KeyCode.PageDown && TryNavigateFocusedMarkdownPage(key.Key == KeyCode.PageDown))
            return true;

        switch (key.Key)
        {
            case KeyCode.PageUp:
                _scrollOffset = _scrollOffset > int.MaxValue - Height
                    ? int.MaxValue
                    : _scrollOffset + Height;
                return true;
            case KeyCode.PageDown:
                _scrollOffset = Math.Max(0, _scrollOffset - Height);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public ScrollbackBatch? PrepareScrollback(in RenderContext context, int maxRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        if (_pendingScrollback is not null)
            return _pendingScrollback;

        var diagnostics = new TranscriptViewDiagnosticsBuilder();
        RefreshCache(in context, context.Width, ref diagnostics);
        if (_model.HistoryPresentation != TranscriptHistoryPresentation.TerminalScrollback)
            return null;

        var rows = new List<ScrollbackRow>();
        var entryCount = 0;
        for (var index = _committedCount; index < _entries.Count; index++)
        {
            var source = _entries[index];
            if (source.State != TranscriptEntryState.Final || source.CommitPolicy == TranscriptCommitPolicy.Never)
                break;

            var rendered = GetRenderedEntry(index, in context, context.Width, ref diagnostics);
            var required = rendered.LineCount + source.VerticalSpacing;
            if (rows.Count > 0 && rows.Count + required > maxRows)
                break;

            for (var line = 0; line < rendered.LineCount; line++)
                rows.Add(rendered.CreateScrollbackRow($"{source.Id}:{line}", line));
            for (var spacing = 0; spacing < source.VerticalSpacing; spacing++)
                rows.Add(new ScrollbackRow($"{source.Id}:space:{spacing}", Array.Empty<ScrollbackCell>()));
            entryCount++;
        }

        if (entryCount == 0)
            return null;

        _pendingEntryCount = entryCount;
        _committedCount += entryCount;
        _pendingScrollback = new ScrollbackBatch(
            _model.HistoryEpoch,
            _committedRowSequence,
            rows.ToArray());
        return _pendingScrollback;
    }

    /// <inheritdoc />
    public void CommitScrollback(ScrollbackBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!ReferenceEquals(batch, _pendingScrollback))
            throw new InvalidOperationException("Only the currently prepared scrollback batch can be committed.");

        _model.CommitPrefix(_committedCount - _pendingEntryCount, _pendingEntryCount);
        _committedRowSequence += batch.Rows.Count;
        _pendingEntryCount = 0;
        _pendingScrollback = null;
    }

    /// <inheritdoc />
    public void RollbackScrollback(ScrollbackBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!ReferenceEquals(batch, _pendingScrollback))
            throw new InvalidOperationException("Only the currently prepared scrollback batch can be rolled back.");
        _committedCount -= _pendingEntryCount;
        _pendingEntryCount = 0;
        _pendingScrollback = null;
    }

    private bool TryNavigateFocusedMarkdownPage(bool forward)
    {
        if (!_cacheInitialized) return false;
        for (var index = _entries.Count - 1; index >= 0; index--)
        {
            var entry = _entries[index];
            var reasoning = entry.Cell is ReasoningMessageCell;
            if (entry.Cell is not AssistantMessageCell && !reasoning) continue;
            var document = reasoning ? ((ReasoningMessageCell)entry.Cell).Document : ((AssistantMessageCell)entry.Cell).Document;
            var projection = reasoning ? ((ReasoningMessageCell)entry.Cell).Projection : ((AssistantMessageCell)entry.Cell).Projection;
            var theme = reasoning
                ? AgentTuiTranscriptRenderServices.Default.CreateMutedTheme(_renderTheme)
                : _renderTheme;
            var depth = Math.Max(0, entry.Metadata.AgentDepth) * 2;
            var options = new MarkdownLayoutOptions(
                Math.Max(1, _renderWidth - depth - (reasoning ? 2 : 0)),
                MarkdownTheme.FromTheme(theme), _renderColorSystem);
            if (!projection.TryNavigateRawPage(document, options, new MarkdownLayoutEngine(), forward))
                continue;
            _renderedEntries[index]?.Dispose();
            _renderedEntries[index] = null;
            return true; // Handled input is the shell's repaint request.
        }
        return false;
    }

    /// <summary>Routes a source-backed entry selection through its semantic Markdown projection.</summary>
    /// <param name="entryId">The stable transcript entry identifier.</param>
    /// <param name="selection">A visual range local to the Markdown body.</param>
    /// <param name="text">Receives safe semantic clipboard text without presentation decoration.</param>
    /// <returns><see langword="true"/> when the entry is source-backed and has a prepared layout.</returns>
    public bool TryGetSemanticClipboardText(string entryId, MarkdownVisualSelection selection, out string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        text = string.Empty;
        if (!_cacheInitialized) return false;
        var entry = _entries.FirstOrDefault(candidate => string.Equals(candidate.Id, entryId, StringComparison.Ordinal));
        if (entry?.Cell is not (AssistantMessageCell or ReasoningMessageCell)) return false;
        var depth = Math.Max(0, entry.Metadata.AgentDepth) * 2;
        var reasoning = entry.Cell is ReasoningMessageCell;
        var document = reasoning
            ? ((ReasoningMessageCell)entry.Cell).Document
            : ((AssistantMessageCell)entry.Cell).Document;
        var projection = reasoning
            ? ((ReasoningMessageCell)entry.Cell).Projection
            : ((AssistantMessageCell)entry.Cell).Projection;
        var theme = reasoning
            ? AgentTuiTranscriptRenderServices.Default.CreateMutedTheme(_renderTheme)
            : null;
        // The structural theme key is captured from the last rendered context; RequirePrepared never computes layout.
        var themeKey = reasoning && theme is not null ? theme.Key : _renderThemeKey;
        var key = new HPD.TUI.Markdown.MarkdownLayoutKey(document.Parsed.PipelineId, "terminal-v1",
            Math.Max(1, _renderWidth - depth - (reasoning ? 2 : 0)), themeKey, _renderColorSystem,
            HPD.TUI.Markdown.MarkdownPresentationMode.Rich, 0, new HPD.TUI.Markdown.MarkdownSpacing().Key,
            new HPD.TUI.Markdown.MarkdownResourceLimits().Key);
        MarkdownLayout layout;
        try { layout = projection.RequireVisiblePrepared(document.Revision, key); }
        catch (InvalidOperationException) { return false; }
        text = projection.GetSafeClipboardText(layout, selection);
        return true;
    }

    private void RenderRows(
        in RenderContext context,
        int maxWidth,
        ref SegmentWriter output)
    {
        if (Height <= 0 || maxWidth <= 0)
        {
            LastDiagnostics = TranscriptViewDiagnostics.Empty;
            return;
        }

        var diagnostics = new TranscriptViewDiagnosticsBuilder();
        var sink = _performanceSink;
        var startTimestamp = sink is null ? 0 : Stopwatch.GetTimestamp();
        RefreshCache(in context, maxWidth, ref diagnostics);
        if (_entries.Count == 0)
        {
            LastDiagnostics = diagnostics.ToDiagnostics();
            PublishRenderDiagnostics(sink, LastDiagnostics, startTimestamp);
            return;
        }

        BuildVisibleRowsFromBottom(in context, maxWidth, ref diagnostics);

        for (var i = 0; i < _visibleRows.Count; i++)
        {
            if (i > 0)
            {
                output.WriteLineBreak();
            }

            WriteVisibleRow(_visibleRows[i], ref output);
        }

        diagnostics.RenderedRows = _visibleRows.Count;
        LastDiagnostics = diagnostics.ToDiagnostics();
        PublishRenderDiagnostics(sink, LastDiagnostics, startTimestamp);
    }

    private void PublishRenderDiagnostics(
        IHpdTuiPerformanceEventSink? sink,
        TranscriptViewDiagnostics diagnostics,
        long startTimestamp)
    {
        if (sink is null)
        {
            return;
        }

        sink.Publish(new TranscriptViewRendered(
            _scope?.AgentId,
            diagnostics.EntriesVisited,
            diagnostics.RowsCaptured,
            diagnostics.RenderedRows,
            diagnostics.CacheHits,
            diagnostics.CacheMisses,
            Stopwatch.GetElapsedTime(startTimestamp))
        {
            SessionId = _scope?.SessionId,
            ThreadId = _scope?.ThreadId,
            Metadata = _scope is null
                ? null
                : new AgentMetadata
                {
                    AgentId = _scope.AgentId,
                    AgentName = _scope.AgentId
                }
        });
    }

    private void RefreshCache(
        in RenderContext context,
        int maxWidth,
        ref TranscriptViewDiagnosticsBuilder diagnostics)
    {
        var modelVersion = _model.Version;
        if (_cacheInitialized &&
            modelVersion == _modelVersion &&
            maxWidth == _renderWidth &&
            context.Theme.Key == _renderThemeKey &&
            context.ColorSystem == _renderColorSystem)
        {
            return;
        }

        var resetRenderedEntries =
            !_cacheInitialized ||
            maxWidth != _renderWidth ||
            context.Theme.Key != _renderThemeKey ||
            context.ColorSystem != _renderColorSystem;

        if (resetRenderedEntries)
        {
            DisposeRenderedEntries();
            _renderedEntries.Clear();
        }

        var snapshot = _model.Snapshot();
        _entries = snapshot.Entries;
        _committedCount = snapshot.CommittedCount;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (i >= _renderedEntries.Count)
            {
                _renderedEntries.Add(null);
            }

            var rendered = _renderedEntries[i];
            if (rendered is not null &&
                !rendered.CanReuse(_entries[i], maxWidth, context.Theme, context.ColorSystem))
            {
                rendered.Dispose();
                _renderedEntries[i] = null;
            }
        }

        while (_renderedEntries.Count > _entries.Count)
        {
            var last = _renderedEntries.Count - 1;
            _renderedEntries[last]?.Dispose();
            _renderedEntries.RemoveAt(last);
        }

        _modelVersion = modelVersion;
        _renderWidth = maxWidth;
        _renderThemeKey = context.Theme.Key;
        _renderTheme = context.Theme;
        _renderColorSystem = context.ColorSystem;
        _cacheInitialized = true;
    }

    private void BuildVisibleRowsFromBottom(
        in RenderContext context,
        int maxWidth,
        ref TranscriptViewDiagnosticsBuilder diagnostics)
    {
        _visibleRows.Clear();
        var rowsToSkip = _scrollOffset;
        var totalRows = 0;

        var visibleRowLimit = Height;
        for (var index = _entries.Count - 1; index >= _committedCount && _visibleRows.Count < visibleRowLimit; index--)
        {
            var entry = GetRenderedEntry(index, in context, maxWidth, ref diagnostics);
            for (var line = entry.LineCount - 1; line >= 0 && _visibleRows.Count < visibleRowLimit; line--)
            {
                totalRows++;
                if (rowsToSkip > 0)
                {
                    rowsToSkip--;
                }
                else
                {
                    _visibleRows.Add(new VisibleTranscriptRow(index, line));
                }
            }

            if (index > 0)
            {
                for (var spacing = 0;
                     spacing < _entries[index - 1].VerticalSpacing && _visibleRows.Count < visibleRowLimit;
                     spacing++)
                {
                    totalRows++;
                    if (rowsToSkip > 0)
                    {
                        rowsToSkip--;
                    }
                    else
                    {
                        _visibleRows.Add(VisibleTranscriptRow.Blank);
                    }
                }
            }
        }

        if (_visibleRows.Count < Height && _scrollOffset > 0)
        {
            _scrollOffset = Math.Max(0, totalRows - Height);
            BuildVisibleRowsFromBottom(in context, maxWidth, ref diagnostics);
            return;
        }

        _visibleRows.Reverse();
    }

    private RenderedTranscriptEntry GetRenderedEntry(
        int index,
        in RenderContext context,
        int maxWidth,
        ref TranscriptViewDiagnosticsBuilder diagnostics)
    {
        diagnostics.EntriesVisited++;
        var rendered = _renderedEntries[index];
        if (rendered is not null)
        {
            diagnostics.CacheHits++;
            diagnostics.RowsMeasured++;
            return rendered;
        }

        diagnostics.CacheMisses++;
        diagnostics.RowsCaptured++;
        rendered = RenderedTranscriptEntry.Create(_entries[index], _renderers, in context, maxWidth);
        _renderedEntries[index] = rendered;
        diagnostics.RowsMeasured++;
        return rendered;
    }

    private void WriteVisibleRow(VisibleTranscriptRow row, ref SegmentWriter output)
    {
        if (row.IsBlank)
        {
            return;
        }

        var entry = _renderedEntries[row.EntryIndex];
        if (entry is null)
        {
            throw new InvalidOperationException("Visible transcript row was not captured.");
        }

        entry.WriteLine(row.LineIndex, ref output);
    }

    public void DisposeCache()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeRenderedEntries();
        _renderedEntries.Clear();
    }

    private void DisposeRenderedEntries()
    {
        for (var i = 0; i < _renderedEntries.Count; i++)
        {
            _renderedEntries[i]?.Dispose();
            _renderedEntries[i] = null;
        }
    }
}

public sealed record TranscriptViewDiagnostics(
    int EntriesVisited,
    int RowsMeasured,
    int RowsCaptured,
    int CacheHits,
    int CacheMisses,
    int RenderedRows)
{
    public static TranscriptViewDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

internal struct TranscriptViewDiagnosticsBuilder
{
    public int EntriesVisited;
    public int RowsMeasured;
    public int RowsCaptured;
    public int CacheHits;
    public int CacheMisses;
    public int RenderedRows;

    public readonly TranscriptViewDiagnostics ToDiagnostics()
        => new(
            EntriesVisited,
            RowsMeasured,
            RowsCaptured,
            CacheHits,
            CacheMisses,
            RenderedRows);
}

internal readonly record struct VisibleTranscriptRow(int EntryIndex, int LineIndex)
{
    public static VisibleTranscriptRow Blank { get; } = new(-1, -1);

    public bool IsBlank => EntryIndex < 0;
}

internal sealed class RenderedTranscriptEntry : IDisposable
{
    private readonly TerminalGrid _grid;

    private RenderedTranscriptEntry(
        TranscriptEntry source,
        int width,
        Theme theme,
        ColorSystem colorSystem,
        TerminalGrid grid,
        int lineCount)
    {
        Source = source;
        Width = width;
        ThemeKey = theme.Key;
        ColorSystem = colorSystem;
        _grid = grid;
        LineCount = lineCount;
    }

    public TranscriptEntry Source { get; }

    public int Width { get; }

    public ThemeKey ThemeKey { get; }

    public ColorSystem ColorSystem { get; }

    public int LineCount { get; }

    public static RenderedTranscriptEntry Create(
        TranscriptEntry entry,
        AgentTuiTranscriptRendererRegistry renderers,
        in RenderContext context,
        int maxWidth)
    {
        var component = renderers.Create(entry, maxWidth, context.Theme, context.ColorSystem);
        var measuredHeight = Math.Max(1, component.Measure(in context, maxWidth).Height);
        var grid = CaptureCompleteEntry(component, measuredHeight, maxWidth, in context);
        var lineCount = Math.Max(1, TuiCapture.GetUsedLineCount(grid));
        return new RenderedTranscriptEntry(entry, maxWidth, context.Theme, context.ColorSystem, grid, lineCount);
    }

    private static TerminalGrid CaptureCompleteEntry(
        IComponent component,
        int initialHeight,
        int maxWidth,
        in RenderContext context)
    {
        var captureHeight = initialHeight;
        while (true)
        {
            var grid = TuiCapture.RenderToGrid(
                component,
                maxWidth,
                captureHeight,
                context.Theme,
                context.ColorSystem,
                context.Elapsed);
            if (grid.CursorY < grid.Height)
            {
                return grid;
            }

            grid.Dispose();
            captureHeight = checked(captureHeight * 2);
        }
    }

    public bool CanReuse(
        TranscriptEntry source,
        int width,
        Theme theme,
        ColorSystem colorSystem)
        => ReferenceEquals(Source, source) &&
           Width == width &&
           ThemeKey == theme.Key &&
           ColorSystem == colorSystem;

    public void WriteLine(int line, ref SegmentWriter output)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        if (line >= LineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        TuiCapture.WriteLineTo(_grid, line, ref output);
    }

    public ScrollbackRow CreateScrollbackRow(string id, int line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        if (line >= LineCount)
            throw new ArgumentOutOfRangeException(nameof(line));

        var cells = new List<ScrollbackCell>();
        for (var column = 0; column < _grid.Width; column++)
        {
            var cell = _grid.GetCell(column, line);
            if (cell.IsContinuation)
                continue;
            cells.Add(new ScrollbackCell(
                _grid.GetGrapheme(cell).ToString(),
                cell.Style,
                new TerminalRunMetadata(_grid.GetHyperlink(cell)),
                cell.DisplayWidth));
        }
        while (cells.Count > 0 && cells[^1].Grapheme == " " && cells[^1].Metadata.Hyperlink is null)
            cells.RemoveAt(cells.Count - 1);
        return new ScrollbackRow(id, cells.ToArray());
    }

    public void Dispose()
        => _grid.Dispose();
}
