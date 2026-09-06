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

public sealed class TranscriptView : Component, IScrollbackSource
{
    private readonly TranscriptModel _model;
    private readonly AgentTuiTranscriptRendererRegistry _renderers;
    private readonly AgentTuiRuntimeScope? _scope;
    private readonly IHpdTuiPerformanceEventSink? _performanceSink;
    private TranscriptSequence _entries = TranscriptSequence.Empty;
    private readonly Dictionary<int, PreparedTranscriptEntry?> _renderedEntries = [];
    private readonly TranscriptLayoutCache _layoutCache;
    private readonly List<VisibleTranscriptRow> _visibleRows = [];
    private int _modelVersion = -1;
    private int _renderWidth;
    private ThemeKey _renderThemeKey;
    private Theme _renderTheme = Theme.Default;
    private ColorSystem _renderColorSystem;
    private bool _cacheInitialized;
    private bool _disposed;
    private int _scrollOffset;
    private int _height;
    private int _committedCount;
    private long _committedRowSequence;
    private ScrollbackBatch? _pendingScrollback;
    private int _pendingEntryCount;
    private long _presentationEpoch;
    private readonly Dictionary<string, PublishedMarkdownPrefix> _publishedMarkdown = [];
    private KeyValuePair<string, PublishedMarkdownPrefix>? _pendingMarkdown;

    // Only the incompletely published final entry owns a frozen visual continuation.
    // Rows already accepted are released; resize wraps remaining cells without replaying history.
    private string? _continuationId;
    private List<ScrollbackRow> _continuation = [];
    private int _pendingContinuationRows;
    private bool _continuationAccepted;
    private int _continuationWidth;

    private sealed record PublishedMarkdownPrefix(MarkdownMessageDocument Document, int SourceEnd);


    /// <summary>Creates a bounded live transcript tail or an explicit history viewport with retained entry layouts.</summary>
    /// <param name="model">The durable transcript model.</param>
    /// <param name="renderers">Renderers for semantic transcript entry types.</param>
    /// <param name="height">The maximum live-tail or viewport height in terminal rows.</param>
    /// <param name="scope">Optional runtime scope used by entry renderers.</param>
    /// <param name="performanceSink">Optional detailed performance-event sink.</param>
    /// <param name="cacheByteBudget">Maximum retained entry-layout bytes.</param>
    /// <param name="performanceCounters">Optional common cache-counter recorder.</param>
    public TranscriptView(
        TranscriptModel model,
        AgentTuiTranscriptRendererRegistry renderers,
        int height = 15,
        AgentTuiRuntimeScope? scope = null,
        IHpdTuiPerformanceEventSink? performanceSink = null,
        long cacheByteBudget = 32 * 1024 * 1024,
        TuiPerformanceCounters? performanceCounters = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(renderers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cacheByteBudget);

        _model = model;
        _renderers = renderers;
        _scope = scope;
        _performanceSink = performanceSink;
        _height = height;
        _presentationEpoch = model.HistoryEpoch;
        CacheByteBudget = cacheByteBudget;
        _layoutCache = new TranscriptLayoutCache(cacheByteBudget, PrepareEntry, performanceCounters);
    }

    /// <inheritdoc />
    public long HistoryRevision => _model.HistoryEpoch;

    /// <inheritdoc />
    public bool IsFullScreen => false;

    /// <inheritdoc />
    public ManagedTerminalRecoveryPolicy HistoryResetPolicy => _model.HistoryResetPolicy switch
    {
        CommittedHistoryMutationPolicy.ClearAndReplay => ManagedTerminalRecoveryPolicy.ClearAndReplay,
        CommittedHistoryMutationPolicy.SwitchToAlternateScreen => ManagedTerminalRecoveryPolicy.SwitchToAlternateScreen,
        _ => ManagedTerminalRecoveryPolicy.VisibleEpochBoundary
    };

    /// <summary>Gets the number of rows exposed by the transcript viewport.</summary>
    public int Height => _height;

    /// <summary>Changes the transcript viewport height and invalidates its layout.</summary>
    public void SetHeight(int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        SetLayout(ref _height, height);
    }

    /// <summary>Gets the maximum retained transcript raster storage in bytes.</summary>
    public long CacheByteBudget { get; }

    /// <summary>
    /// Gets the number of rendered transcript rows between the viewport and the newest row.
    /// </summary>
    public int ScrollOffset => _scrollOffset;

    public TranscriptViewDiagnostics LastDiagnostics { get; private set; } = TranscriptViewDiagnostics.Empty;

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        if (_model.HistoryPresentation != TranscriptHistoryPresentation.TerminalScrollback)
            return new(Math.Min(constraints.MaxWidth, 20), constraints.MaxWidth, Height);
        var diagnostics = new TranscriptViewDiagnosticsBuilder();
        RefreshCache(in context, constraints.MaxWidth, ref diagnostics);
        BuildVisibleRowsFromBottom(in context, constraints.MaxWidth, ref diagnostics);
        var height = _visibleRows.Count;
        _layoutCache.EndProjection();
        return new(Math.Min(constraints.MaxWidth, 20), constraints.MaxWidth, height);
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        RenderRows(in context, maxWidth, ref output);
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        if (_model.HistoryPresentation == TranscriptHistoryPresentation.TerminalScrollback &&
            _model.CommittedCount == _model.Count) return false;

        if (key.Key is KeyCode.PageUp or KeyCode.PageDown && TryNavigateFocusedMarkdownPage(key.Key == KeyCode.PageDown))
            return true;

        switch (key.Key)
        {
            case KeyCode.PageUp:
                SetPaint(ref _scrollOffset, _scrollOffset > int.MaxValue - Height
                    ? int.MaxValue
                    : _scrollOffset + Height);
                return true;
            case KeyCode.PageDown:
                SetPaint(ref _scrollOffset, Math.Max(0, _scrollOffset - Height));
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public void ResetPresentation(long presentationEpoch, in RenderContext context)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(presentationEpoch);
        if (_pendingScrollback is not null)
        {
            _committedCount -= _pendingEntryCount;
            _pendingEntryCount = 0;
            _pendingScrollback = null;
        }
        _presentationEpoch = presentationEpoch;
        _model.ResetPublication();
        _publishedMarkdown.Clear();
        _continuationId = null;
        _continuationAccepted = false;
        _continuation.Clear();
        _pendingContinuationRows = 0;
        _pendingMarkdown = null;
        _committedRowSequence = 0;
        InvalidateLayout();
        var diagnostics = new TranscriptViewDiagnosticsBuilder();
        RefreshCache(in context, context.Width, ref diagnostics);
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
        _layoutCache.BeginProjection();
        try
        {
            for (var index = _committedCount; index < _entries.Count; index++)
            {
                var source = _entries[index];
                if (source.CommitPolicy == TranscriptCommitPolicy.Never) break;
                if (_continuationId == source.Id)
                {
                    WrapContinuation(context.Width);
                    var take = Math.Min(maxRows - rows.Count, _continuation.Count);
                    rows.AddRange(_continuation.Take(take));
                    _pendingContinuationRows = take;
                    if (take == _continuation.Count) entryCount++;
                    break;
                }
                var rendered = GetRenderedEntry(index, in context, context.Width, ref diagnostics);
                var startRow = PublishedRows(source, in context, context.Width);
                var endRow = rendered.LineCount;
                var sourceEnd = 0;
                MarkdownMessageDocument? document = null;
                if (source.State != TranscriptEntryState.Final)
                {
                    if (!TryMarkdownLayout(source, in context, context.Width, out document, out var layout)) break;
                    // Reference-style syntax may resolve only when a later definition arrives.
                    // Keep that candidate mutable rather than publishing a layout that can change.
                    if ((document.Parsed.Features & (MarkdownDocumentFeatures.ReferenceDefinitions |
                        MarkdownDocumentFeatures.ExtensionGlobalState)) != 0) break;
                    endRow = 0;
                    for (var row = 0; row < layout.Rows.Length; row++)
                    {
                        var mapped = layout.Rows[row];
                        if (mapped.Kind == MarkdownLayoutRowKind.LiteralTail) break;
                        if (mapped.SourceEndExclusive is not { } end) continue;
                        if (end > document.StableSourceLength) break;
                        endRow = row + 1;
                        sourceEnd = end;
                    }
                    if (document.Parsed.Source.AsSpan(0, sourceEnd).IndexOf('[') >= 0) break;
                    if (endRow > 0) endRow += MarkdownRowOffset(source);
                    endRow = Math.Min(endRow, rendered.LineCount);
                    if (endRow <= startRow) break;
                }
                var spacing = source.State == TranscriptEntryState.Final ? source.VerticalSpacing : 0;
                if (endRow - startRow + spacing > maxRows - rows.Count)
                {
                    if (rows.Count > 0) break;
                    if (source.State != TranscriptEntryState.Final)
                    {
                        // Publish only complete stable source ranges. An oversized block stays
                        // mutable until finalization, when it can use a frozen continuation.
                        var limit = startRow + maxRows - MarkdownRowOffset(source);
                        TryMarkdownLayout(source, in context, context.Width, out document, out var boundedLayout);
                        endRow = startRow;
                        sourceEnd = 0;
                        for (var row = 0; row < Math.Min(limit, boundedLayout.Rows.Length); row++)
                        {
                            if (boundedLayout.Rows[row].SourceEndExclusive is not { } end ||
                                end > document.StableSourceLength) continue;
                            // Repeated source ends belong to one block; do not split its rows.
                            if (row + 1 < boundedLayout.Rows.Length &&
                                boundedLayout.Rows[row + 1].SourceEndExclusive == end) continue;
                            endRow = row + 1 + MarkdownRowOffset(source);
                            sourceEnd = end;
                        }
                        if (endRow <= startRow) break;
                    }
                    else
                    {
                        _continuationId = source.Id;
                        _continuationWidth = context.Width;
                        _continuationAccepted = false;
                        for (var line = startRow; line < endRow; line++)
                            _continuation.Add(rendered.CreateScrollbackRow($"{source.Id}:{line}", line));
                        for (var line = 0; line < spacing; line++)
                            _continuation.Add(new ScrollbackRow($"{source.Id}:space:{line}", Array.Empty<ScrollbackCell>()));
                        rows.AddRange(_continuation.Take(maxRows));
                        _pendingContinuationRows = maxRows;
                        break;
                    }
                }
                for (var line = startRow; line < endRow; line++)
                    rows.Add(rendered.CreateScrollbackRow($"{source.Id}:{line}", line));
                for (var line = 0; line < spacing; line++)
                    rows.Add(new ScrollbackRow($"{source.Id}:space:{line}", Array.Empty<ScrollbackCell>()));
                if (source.State != TranscriptEntryState.Final)
                {
                    _pendingMarkdown = new(source.Id, new PublishedMarkdownPrefix(document!, sourceEnd));
                    break;
                }
                entryCount++;
            }
        }
        finally { _layoutCache.EndProjection(); }
        if (rows.Count == 0 && entryCount == 0) return null;
        InvalidateLayout();
        _pendingEntryCount = entryCount;
        _committedCount += entryCount;
        _pendingScrollback = new ScrollbackBatch(
            _presentationEpoch,
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

        for (var index = _committedCount - _pendingEntryCount; index < _committedCount; index++)
            _publishedMarkdown.Remove(_entries[index].Id);
        _model.CommitPrefix(_committedCount - _pendingEntryCount, _pendingEntryCount);
        if (_pendingMarkdown is { } partial)
        {
            _publishedMarkdown[partial.Key] = partial.Value;
            _model.CommitPartialMarkdown(partial.Key,
                partial.Value.Document.GetCanonicalSource()[..partial.Value.SourceEnd]);
        }
        if (_pendingContinuationRows > 0)
        {
            _continuation.RemoveRange(0, _pendingContinuationRows);
            if (_continuation.Count == 0) _continuationId = null;
            else { _model.CommitPartialFinal(_continuationId!); _continuationAccepted = true; }
        }
        _pendingContinuationRows = 0;
        _pendingMarkdown = null;
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
        _pendingMarkdown = null;
        _pendingContinuationRows = 0;
        if (!_continuationAccepted) { _continuationId = null; _continuation.Clear(); }
        InvalidateLayout();
    }

    private void WrapContinuation(int width)
    {
        if (_continuationWidth == width) return;
        _continuationWidth = width;
        if (_continuation.All(row => row.Cells.Sum(cell => cell.DisplayWidth) <= width)) return;
        var wrapped = new List<ScrollbackRow>();
        foreach (var row in _continuation)
        {
            var cells = new List<ScrollbackCell>();
            var columns = 0;
            var part = 0;
            foreach (var cell in row.Cells)
            {
                if (columns > 0 && columns + cell.DisplayWidth > width)
                {
                    wrapped.Add(new ScrollbackRow($"{row.Id}:wrap:{part++}", cells.ToArray()));
                    cells.Clear();
                    columns = 0;
                }
                cells.Add(cell);
                columns += cell.DisplayWidth;
            }
            wrapped.Add(new ScrollbackRow($"{row.Id}:wrap:{part}", cells.ToArray()));
        }
        _continuation = wrapped;
    }

    private int PublishedRows(TranscriptEntry entry, in RenderContext context, int width)
    {
        var prefix = _pendingMarkdown is { } pending && pending.Key == entry.Id ? pending.Value :
            _publishedMarkdown.GetValueOrDefault(entry.Id);
        if (prefix is null) return 0;
        if (!TryMarkdownLayout(entry, in context, width, out var document, out var layout))
            throw new InvalidOperationException("A published Markdown range cannot be replaced by another cell type.");
        var source = document.GetCanonicalSource();
        var accepted = prefix.Document.GetCanonicalSource();
        if (source.Length < prefix.SourceEnd ||
            !source.AsSpan(0, prefix.SourceEnd).SequenceEqual(accepted.AsSpan(0, prefix.SourceEnd)))
            throw new InvalidOperationException("A published Markdown prefix changed without a history transition.");
        var rows = 0;
        for (var row = 0; row < layout.Rows.Length; row++)
        {
            if (layout.Rows[row].SourceEndExclusive is not { } end) continue;
            if (end > prefix.SourceEnd) break;
            rows = row + 1;
        }
        return rows + (rows > 0 ? MarkdownRowOffset(entry) : 0);
    }

    private int MarkdownRowOffset(TranscriptEntry entry)
    {
        object? renderer = null;
        if (entry.Cell is AssistantMessageCell && _renderers.TryFindRenderer<AssistantMessageCell>(
                AgentTuiTranscriptRendererKeys.AssistantMessage, out var assistant)) renderer = assistant;
        else if (entry.Cell is ReasoningMessageCell && _renderers.TryFindRenderer<ReasoningMessageCell>(
                     AgentTuiTranscriptRendererKeys.ReasoningMessage, out var reasoning)) renderer = reasoning;
        return renderer is IAgentTuiMarkdownPublicationRenderer publication ? publication.MarkdownRowOffset : -1;
    }

    private bool TryMarkdownLayout(TranscriptEntry entry, in RenderContext context, int width,
        out MarkdownMessageDocument document, out MarkdownLayout layout)
    {
        if (MarkdownRowOffset(entry) < 0) { document = null!; layout = null!; return false; }
        MarkdownMessageProjection projection;
        var reasoning = entry.Cell is ReasoningMessageCell;
        if (entry.Cell is AssistantMessageCell assistant)
        { document = assistant.Document; projection = assistant.Projection; }
        else if (entry.Cell is ReasoningMessageCell thought)
        { document = thought.Document; projection = thought.Projection; }
        else { document = null!; layout = null!; return false; }
        var theme = _renderers.Services.ResolveMarkdownTheme(context.Theme, reasoning);
        var options = new MarkdownLayoutOptions(
            Math.Max(1, width - Math.Max(0, entry.Metadata.AgentDepth) * 2 - (reasoning ? 2 : 0)),
            theme, context.ColorSystem);
        layout = projection.RequireVisiblePrepared(document, options);
        return layout.DegradationReason == MarkdownDegradationReason.None &&
            layout.Key.Mode == MarkdownPresentationMode.Rich;
    }

    private bool TryNavigateFocusedMarkdownPage(bool forward)
    {
        if (!_cacheInitialized) return false;
        for (var index = _entries.Count - 1; index >= _committedCount; index--)
        {
            var entry = _entries[index];
            var reasoning = entry.Cell is ReasoningMessageCell;
            if (entry.Cell is not AssistantMessageCell && !reasoning) continue;
            var document = reasoning ? ((ReasoningMessageCell)entry.Cell).Document : ((AssistantMessageCell)entry.Cell).Document;
            var projection = reasoning ? ((ReasoningMessageCell)entry.Cell).Projection : ((AssistantMessageCell)entry.Cell).Projection;
            var theme = _renderers.Services.ResolveMarkdownTheme(_renderTheme, reasoning);
            var depth = Math.Max(0, entry.Metadata.AgentDepth) * 2;
            var options = new MarkdownLayoutOptions(
                Math.Max(1, _renderWidth - depth - (reasoning ? 2 : 0)),
                theme, _renderColorSystem);
            if (!projection.TryNavigateRawPage(document, options, new MarkdownLayoutEngine(), forward))
                continue;
            _layoutCache.Remove(entry);
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
        var themeKey = _renderers.Services.ResolveMarkdownTheme(_renderTheme, reasoning).ThemeKey;
        var key = new HPD.TUI.Markdown.MarkdownLayoutKey(document.Parsed.PipelineId, "terminal-v1",
            Math.Max(1, _renderWidth - depth - (reasoning ? 2 : 0)), themeKey, _renderColorSystem,
            HPD.TUI.Markdown.MarkdownPresentationMode.Rich, new HPD.TUI.Markdown.MarkdownSpacing().Key,
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
        ref DisplayListBuilder output)
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
        _layoutCache.EndProjection();

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
            _renderedEntries.Clear();
        }

        var snapshot = _model.Snapshot();
        _entries = snapshot.Entries;
        _committedCount = snapshot.CommittedCount;
        foreach (var index in _renderedEntries.Keys.ToArray())
        {
            var rendered = _renderedEntries[index];
            if (index < _committedCount || index >= _entries.Count || rendered is null ||
                !ReferenceEquals(rendered.Source, _entries[index]) || rendered.IsDisposed)
                _renderedEntries.Remove(index);
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
        _layoutCache.BeginProjection();
        var rowsToSkip = _scrollOffset;
        var totalRows = 0;

        var visibleRowLimit = Height;
        for (var index = _entries.Count - 1; index >= _committedCount && _visibleRows.Count < visibleRowLimit; index--)
        {
            var continuation = _entries[index].Id == _continuationId;
            var lineCount = continuation ? Math.Max(0, _continuation.Count - _entries[index].VerticalSpacing) :
                GetRenderedEntry(index, in context, maxWidth, ref diagnostics).LineCount;
            var firstLine = continuation ? _pendingContinuationRows : PublishedRows(_entries[index], in context, maxWidth);
            for (var line = lineCount - 1; line >= firstLine && _visibleRows.Count < visibleRowLimit; line--)
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

            if (index > _committedCount)
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
            SetPaint(ref _scrollOffset, Math.Max(0, totalRows - Height));
            BuildVisibleRowsFromBottom(in context, maxWidth, ref diagnostics);
            return;
        }

        _visibleRows.Reverse();
    }

    private PreparedTranscriptEntry GetRenderedEntry(
        int index,
        in RenderContext context,
        int maxWidth,
        ref TranscriptViewDiagnosticsBuilder diagnostics)
    {
        diagnostics.EntriesVisited++;
        var key = new TranscriptLayoutKey(maxWidth, context.Theme.Key, context.ColorSystem, 1);
        var rendered = _renderedEntries.GetValueOrDefault(index);
        if (rendered is not null && !rendered.IsDisposed)
        {
            rendered = _layoutCache.Resolve(_entries[index], key);
            _renderedEntries[index] = rendered;
            diagnostics.CacheHits++;
            diagnostics.RowsMeasured++;
            return rendered;
        }

        diagnostics.CacheMisses++;
        diagnostics.RowsCaptured++;
        rendered = _layoutCache.Resolve(_entries[index], key);
        _renderedEntries[index] = rendered;
        if (_layoutCache.LastResolveWasHit)
        {
            diagnostics.CacheHits++;
            diagnostics.CacheMisses--;
            diagnostics.RowsCaptured--;
        }
        diagnostics.RowsMeasured++;
        return rendered;
    }

    private void WriteVisibleRow(VisibleTranscriptRow row, ref DisplayListBuilder output)
    {
        if (row.IsBlank)
        {
            return;
        }

        if (_entries[row.EntryIndex].Id == _continuationId)
        {
            foreach (var cell in _continuation[row.LineIndex].Cells)
                output.Write(cell.Grapheme, cell.Style, cell.Metadata);
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
        _layoutCache.Dispose();
        _renderedEntries.Clear();
        _continuation.Clear();
        _continuationId = null;
        _publishedMarkdown.Clear();
        _pendingMarkdown = null;
        _pendingScrollback = null;
    }

    private PreparedTranscriptEntry PrepareEntry(TranscriptEntry entry, TranscriptLayoutKey key)
    {
        var context = new RenderContext(key.Width, Math.Max(1, Height), _renderTheme, key.ColorSystem);
        var component = _renderers.Create(entry, key.Width, _renderTheme, key.ColorSystem);
        var measuredHeight = Math.Max(1, component.Measure(in context,
            HPD.TUI.Layout.LayoutConstraints.Loose(key.Width, context.Height)).Height);
        var captureHeight = measuredHeight;
        while (true)
        {
            var grid = TuiCapture.RenderToGrid(component, key.Width, captureHeight,
                _renderTheme, key.ColorSystem, context.Elapsed);
            if (grid.CursorY < grid.Height)
                return new PreparedTranscriptEntry(entry, key, grid, Math.Max(1, TuiCapture.GetUsedLineCount(grid)));
            grid.Dispose();
            captureHeight = checked(captureHeight * 2);
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
