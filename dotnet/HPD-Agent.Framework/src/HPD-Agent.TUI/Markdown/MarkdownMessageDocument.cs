using HPD.TUI.Markdown;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Markdown;

/// <summary>Identifies one source-backed Markdown stream.</summary>
public readonly record struct MarkdownStreamIdentity(MarkdownStreamKind Kind, string MessageId);

/// <summary>Identifies the semantic kind of an agent Markdown stream.</summary>
public enum MarkdownStreamKind { Assistant, Reasoning }

/// <summary>Identifies the lifecycle state of a source-backed message.</summary>
public enum MarkdownMessageState { Streaming, Completed, Interrupted, Cancelled, Failed }

/// <summary>Controls how rich presentation treats the incomplete physical line of a live stream.</summary>
public enum MarkdownIncompleteLinePolicy
{
    /// <summary>
    /// Renders the incomplete line as rich Markdown at each publication while briefly withholding
    /// a bounded suffix whose Markdown meaning is still ambiguous.
    /// </summary>
    StreamRich,

    /// <summary>Parses complete physical lines and displays the incomplete line as literal text.</summary>
    CompleteLineWithLiteralTail
}

/// <summary>Captures non-layout message presentation metadata.</summary>
public sealed record MarkdownMessagePresentation(
    string? Role = null,
    AgentMessageSource Source = AgentMessageSource.AssistantOutput,
    AgentMessageVisibility Visibility = AgentMessageVisibility.Transcript,
    string? AuthorName = null,
    AgentMessagePersistence Persistence = AgentMessagePersistence.ThreadHistory,
    DateTimeOffset? CreatedAt = null,
    string? ClientInputId = null,
    string? AgentId = null,
    string? AgentName = null,
    string? ParentAgentId = null,
    IReadOnlyList<string>? AgentChain = null,
    int AgentDepth = 0,
    string? SessionId = null,
    string? ThreadId = null,
    MarkdownIncompleteLinePolicy IncompleteLinePolicy = MarkdownIncompleteLinePolicy.StreamRich);

/// <summary>Represents one immutable publication of canonical agent Markdown.</summary>
public sealed record MarkdownMessageDocument
{
    /// <summary>Gets the stream identity.</summary>
    public required MarkdownStreamIdentity Identity { get; init; }
    /// <summary>Gets the identity of this accepted Start lineage.</summary>
    public required Guid LineageId { get; init; }
    /// <summary>Gets the message identity.</summary>
    public required string MessageId { get; init; }
    internal MarkdownDocumentSnapshot Parsed { get; init; } = null!;
    internal string UnparsedTail { get; init; } = string.Empty;
    /// <summary>Gets the stable-prefix boundary within the current epoch.</summary>
    public required int StableSourceLength { get; init; }
    /// <summary>Gets the lifecycle state.</summary>
    public required MarkdownMessageState State { get; init; }
    /// <summary>Gets a non-source terminal diagnostic, when one was supplied.</summary>
    public string? FailureDetail { get; init; }
    /// <summary>Gets immutable presentation metadata.</summary>
    public required MarkdownMessagePresentation Presentation { get; init; }
    /// <summary>Gets the accepted source revision.</summary>
    public required long Revision { get; init; }
    /// <summary>Gets the global-invalidation epoch.</summary>
    public required long Epoch { get; init; }
    internal ImmutableDictionary<string, object?> AdditionalProperties { get; init; } = ImmutableDictionary<string, object?>.Empty;

    internal string GetCanonicalSource() => Parsed.Source + UnparsedTail;

    /// <summary>Gets control-neutralized text suitable for ordinary display, clipboard, and export surfaces.</summary>
    public string GetSafeDisplayText()
    {
        if (Presentation.Visibility == AgentMessageVisibility.Hidden)
            throw new UnauthorizedAccessException("Hidden Markdown has no display projection.");
        return SanitizeForDisclosure(GetCanonicalSource());
    }

    /// <summary>Exports exact accepted UTF-16 source after validating explicit authority for this lineage.</summary>
    public string ExportExact(MarkdownExactSourceAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.Identity != Identity || authority.LineageId != LineageId)
            throw new UnauthorizedAccessException("Exact-source authority belongs to a different Markdown lineage.");
        return GetCanonicalSource();
    }

    internal static string SanitizeForDisclosure(string source)
    {
        var chars = source.Replace("\r\n", "\n", StringComparison.Ordinal).ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is not ('\t' or '\n') && TerminalTextSafety.IsUnsafe(chars[i])) chars[i] = '�';
        return new(chars);
    }
}

/// <summary>Unforgeable authorization to export exact source from one retained Markdown lineage.</summary>
public sealed class MarkdownExactSourceAuthority
{
    internal MarkdownExactSourceAuthority(MarkdownStreamIdentity identity, Guid lineageId)
    { Identity = identity; LineageId = lineageId; }
    internal MarkdownStreamIdentity Identity { get; }
    internal Guid LineageId { get; }
}

/// <summary>Applies visibility and privilege policy before granting forensic exact-source access.</summary>
internal static class MarkdownExportPolicy
{
    internal static MarkdownExactSourceAuthority AuthorizeExact(
        MarkdownMessageDocument document,
        IMarkdownExactSourceAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(authorization);
        if (document.Presentation.Visibility == AgentMessageVisibility.Hidden ||
            !authorization.CanExportExact(document.Identity, document.LineageId, document.Presentation))
            throw new UnauthorizedAccessException("This Markdown source is not authorized for exact export.");
        return new(document.Identity, document.LineageId);
    }
}

/// <summary>Provides the host's privileged decision for forensic exact-source disclosure.</summary>
internal interface IMarkdownExactSourceAuthorization
{
    /// <summary>Decides whether the specified retained lineage may be disclosed exactly.</summary>
    bool CanExportExact(
        MarkdownStreamIdentity identity,
        Guid lineageId,
        MarkdownMessagePresentation presentation);
}

/// <summary>Identifies an inclusive visual selection in a prepared Markdown layout.</summary>
public readonly record struct MarkdownVisualSelection(int StartRow, int StartColumn, int EndRow, int EndColumn);

/// <summary>Reports source-safe layout and bounded-cache measurements for one projection lineage.</summary>
public readonly record struct MarkdownProjectionDiagnosticsSnapshot(
    long LayoutCount,
    TimeSpan LayoutDuration,
    long StableBlocksReused,
    long CacheHits,
    long CacheMisses,
    long CacheEvictions,
    long MutableBlocksRerendered,
    long LayoutFallbacks,
    long Degradations);

/// <summary>Retains prepared block projections across immutable document publications.</summary>
public sealed class MarkdownMessageProjection
{
    private const int MaximumCacheEntries = 256;
    private const long MaximumCacheBytes = 4 * 1024 * 1024;
    private const long MaximumEntryBytes = 512 * 1024;
    private readonly Dictionary<BlockCacheKey, CacheEntry> _blocks = [];
    private readonly LinkedList<BlockCacheKey> _lru = [];
    private readonly ConcurrentDictionary<PreparedKey, MarkdownLayout> _prepared = [];
    private readonly ConcurrentDictionary<PreparedKey, PreparedDocumentStamp> _preparedStamps = [];
    private readonly Queue<PreparedKey> _preparedOrder = [];
    private readonly ConcurrentDictionary<PreparedKey, RawPageState> _rawPages = [];
    private long _cacheBytes;
    private long _cachedEpoch = -1;
    private long _layoutCount;
    private long _layoutTicks;
    private long _stableBlocksReused;
    private long _cacheHits;
    private long _cacheMisses;
    private long _cacheEvictions;
    private long _mutableBlocksRerendered;
    private long _layoutFallbacks;
    private long _degradations;
    internal MarkdownMessageProjection(MarkdownStreamIdentity identity, Guid lineageId)
    {
        Identity = identity;
        LineageId = lineageId;
    }

    /// <summary>Gets the owning stream identity.</summary>
    public MarkdownStreamIdentity Identity { get; }
    /// <summary>Gets the owning lineage identity.</summary>
    public Guid LineageId { get; }
    /// <summary>Gets the most recently projected revision.</summary>
    public long Revision { get; internal set; }
    /// <summary>Gets the most recently projected epoch.</summary>
    public long Epoch { get; internal set; }
    /// <summary>Gets structured measurements that never include model source.</summary>
    public MarkdownProjectionDiagnosticsSnapshot Diagnostics => new(
        _layoutCount, TimeSpan.FromTicks(_layoutTicks), _stableBlocksReused,
        _cacheHits, _cacheMisses, _cacheEvictions, _mutableBlocksRerendered,
        _layoutFallbacks, _degradations);

    internal MarkdownLayout ResolveLayout(
        MarkdownMessageDocument document,
        MarkdownLayoutOptions options,
        IMarkdownLayoutEngine engine)
    {
        if (options.Mode == MarkdownPresentationMode.Raw)
            return engine.LayoutRaw(document.GetCanonicalSource(), document.Parsed.PipelineId, options);
        if (_cachedEpoch != document.Epoch)
        {
            _blocks.Clear();
            _lru.Clear();
            _cacheBytes = 0;
            _cachedEpoch = document.Epoch;
        }

        var frameworkThemeKey = options.Theme.ThemeKey;
        var layouts = ImmutableArray.CreateBuilder<MarkdownBlockLayout>(document.Parsed.Blocks.Count);
        var rows = ImmutableArray.CreateBuilder<MarkdownLayoutRow>();
        MarkdownTopLevelBlock? previous = null;
        var degradationReason = MarkdownDegradationReason.None;
        foreach (var block in document.Parsed.Blocks)
        {
            var exactSource = document.Parsed.Source[block.SourceStart..block.SourceEndExclusive];
            var key = new BlockCacheKey(block.Ordinal, block.SourceStart, block.SourceEndExclusive, exactSource,
                document.Parsed.PipelineId, options.Width, frameworkThemeKey, options.ColorSystem, options.Mode,
                options.SyntaxThemeRevision, (options.Spacing ?? new MarkdownSpacing()).Key,
                (options.ResourceLimits ?? new MarkdownResourceLimits()).Key,
                (document.Parsed.Features & (MarkdownDocumentFeatures.ReferenceDefinitions | MarkdownDocumentFeatures.ExtensionGlobalState)) != 0
                    ? document.Revision : 0);
            MarkdownBlockLayout layout;
            if (_blocks.TryGetValue(key, out var cached))
            {
                _cacheHits++;
                _stableBlocksReused++;
                layout = cached.Layout;
                _lru.Remove(cached.Node);
                _lru.AddLast(cached.Node);
            }
            else
            {
                _cacheMisses++;
                layout = engine.LayoutBlock(document.Parsed, block, options);
                var isStable = block.SourceEndExclusive <= document.StableSourceLength;
                if (!isStable) _mutableBlocksRerendered++;
                if (layout.DegradationReason == MarkdownDegradationReason.LayoutFailure) _layoutFallbacks++;
                var weight = EstimateBytes(exactSource, layout);
                if (isStable && weight <= MaximumEntryBytes)
                {
                    while (_blocks.Count >= MaximumCacheEntries || _cacheBytes + weight > MaximumCacheBytes)
                        EvictOldest();
                    var node = _lru.AddLast(key);
                    _blocks.Add(key, new(layout, node, weight));
                    _cacheBytes += weight;
                }
            }
            if (rows.Count > 0)
                for (var gap = 0; gap < MarkdownLayoutEngine.GetSeparatorRows(
                         previous!, block, options.Spacing ?? new MarkdownSpacing(), document.Parsed.Source); gap++)
                    rows.Add(new(MarkdownLayoutRowKind.Separator, StyledTerminalLine.Empty, null, null, null, true));
            foreach (var line in layout.Lines)
                rows.Add(new(MarkdownLayoutRowKind.BlockContent, line, block.Ordinal, block.SourceStart, block.SourceEndExclusive, false));
            layouts.Add(layout);
            if (degradationReason == MarkdownDegradationReason.None)
                degradationReason = layout.DegradationReason;
            previous = block;
            if (rows.Count > (options.ResourceLimits ?? new MarkdownResourceLimits()).MaximumLayoutRows)
                return engine.LayoutRaw(GetRichVisibleSource(document), document.Parsed.PipelineId, options);
        }

        if (document.UnparsedTail.Length > 0 &&
            (document.State != MarkdownMessageState.Streaming ||
             document.Presentation.IncompleteLinePolicy == MarkdownIncompleteLinePolicy.CompleteLineWithLiteralTail))
        {
            if (rows.Count > 0) rows.Add(new(MarkdownLayoutRowKind.Separator, StyledTerminalLine.Empty, null, null, null, true));
            var sourceOffset = document.Parsed.Source.Length;
            var tailLayout = engine.LayoutRaw(document.UnparsedTail, document.Parsed.PipelineId, options with
            {
                Mode = MarkdownPresentationMode.Raw
            });
            foreach (var tailRow in tailLayout.Rows)
                rows.Add(new(MarkdownLayoutRowKind.LiteralTail,
                    ShiftSourceOffsets(tailRow.Line, sourceOffset), null,
                    document.Parsed.Source.Length, document.GetCanonicalSource().Length, false));
            if (rows.Count > (options.ResourceLimits ?? new MarkdownResourceLimits()).MaximumLayoutRows)
                return engine.LayoutRaw(GetRichVisibleSource(document), document.Parsed.PipelineId, options);
        }

        return new MarkdownLayout
        {
            Key = new(document.Parsed.PipelineId, "terminal-v1", options.Width, frameworkThemeKey,
                options.ColorSystem, options.Mode, options.SyntaxThemeRevision, (options.Spacing ?? new MarkdownSpacing()).Key,
                (options.ResourceLimits ?? new MarkdownResourceLimits()).Key),
            Blocks = layouts.ToImmutable(),
            Rows = rows.ToImmutable(),
            DegradationReason = degradationReason
        };
    }

    private static string GetRichVisibleSource(MarkdownMessageDocument document) =>
        document.State == MarkdownMessageState.Streaming &&
        document.Presentation.IncompleteLinePolicy == MarkdownIncompleteLinePolicy.StreamRich
            ? document.Parsed.Source
            : document.GetCanonicalSource();

    /// <summary>Prepares and retains an immutable layout at a dispatcher publication boundary.</summary>
    internal MarkdownLayout Prepare(
        MarkdownMessageDocument document,
        MarkdownLayoutOptions options,
        IMarkdownLayoutEngine engine)
    {
        var expectedKey = new MarkdownLayoutKey(document.Parsed.PipelineId, "terminal-v1", options.Width,
            options.Theme.ThemeKey, options.ColorSystem, options.Mode, options.SyntaxThemeRevision,
            (options.Spacing ?? new MarkdownSpacing()).Key,
            (options.ResourceLimits ?? new MarkdownResourceLimits()).Key);
        var expectedPreparedKey = new PreparedKey(document.Revision, expectedKey);
        var expectedStamp = new PreparedDocumentStamp(
            document.State, document.Parsed.SourceLength, document.UnparsedTail.Length, document.Epoch);
        if (_prepared.TryGetValue(expectedPreparedKey, out var prepared) &&
            _preparedStamps.TryGetValue(expectedPreparedKey, out var stamp) && stamp == expectedStamp)
            return prepared;
        var started = Stopwatch.GetTimestamp();
        MarkdownLayout layout;
        try { layout = ResolveLayout(document, options, engine); }
        finally
        {
            _layoutCount++;
            _layoutTicks += Stopwatch.GetElapsedTime(started).Ticks;
        }
        if (layout.DegradationReason != MarkdownDegradationReason.None) _degradations++;
        var preparedKey = new PreparedKey(document.Revision, layout.Key);
        var replacesPreparedLayout = _prepared.ContainsKey(preparedKey);
        _prepared[preparedKey] = layout;
        _preparedStamps[preparedKey] = expectedStamp;
        _rawPages.TryRemove(preparedKey, out _);
        if (!replacesPreparedLayout) _preparedOrder.Enqueue(preparedKey);
        if (_prepared.Count > 8)
        {
            var oldest = _preparedOrder.Dequeue();
            _prepared.TryRemove(oldest, out _);
            _preparedStamps.TryRemove(oldest, out _);
            _rawPages.TryRemove(oldest, out _);
        }
        return layout;
    }

    /// <summary>Gets an already prepared publication without parsing or laying out.</summary>
    public MarkdownLayout RequirePrepared(long revision, MarkdownLayoutKey key) =>
        _prepared.TryGetValue(new(revision, key), out var layout)
            ? layout
            : throw new InvalidOperationException(
                $"Markdown layout was not prepared for revision {revision}, width {key.Width}, " +
                $"colors {key.ColorSystem}, mode {key.Mode}. Available revisions: " +
                string.Join(", ", _prepared.Keys.Select(static prepared => prepared.Revision)));

    /// <summary>Gets the persisted raw page currently visible for a prepared publication.</summary>
    public MarkdownLayout RequireVisiblePrepared(long revision, MarkdownLayoutKey key)
    {
        var preparedKey = new PreparedKey(revision, key);
        return _rawPages.TryGetValue(preparedKey, out var page)
            ? page.Current
            : RequirePrepared(revision, key);
    }

    /// <summary>Gets the persisted visible page for an already-prepared document and layout context.</summary>
    public MarkdownLayout RequireVisiblePrepared(
        MarkdownMessageDocument document,
        MarkdownLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        var key = new MarkdownLayoutKey(
            document.Parsed.PipelineId,
            "terminal-v1",
            options.Width,
            options.Theme.ThemeKey,
            options.ColorSystem,
            options.Mode,
            options.SyntaxThemeRevision,
            (options.Spacing ?? new MarkdownSpacing()).Key,
            (options.ResourceLimits ?? new MarkdownResourceLimits()).Key);
        return RequireVisiblePrepared(document.Revision, key);
    }

    internal bool TryNavigateRawPage(
        MarkdownMessageDocument document,
        MarkdownLayoutOptions options,
        IMarkdownLayoutEngine engine,
        bool forward)
    {
        var key = new MarkdownLayoutKey(document.Parsed.PipelineId, "terminal-v1", options.Width,
            options.Theme.ThemeKey, options.ColorSystem, MarkdownPresentationMode.Rich,
            options.SyntaxThemeRevision, (options.Spacing ?? new MarkdownSpacing()).Key,
            (options.ResourceLimits ?? new MarkdownResourceLimits()).Key);
        var preparedKey = new PreparedKey(document.Revision, key);
        var state = _rawPages.GetValueOrDefault(preparedKey);
        var current = state?.Current ?? RequirePrepared(document.Revision, key);
        if (forward)
        {
            if (current.NextSourceOffset is not { } offset) return false;
            state ??= new RawPageState(current);
            state.Previous.Push(current);
            state.Current = engine.LayoutRawPage(document.GetCanonicalSource(), document.Parsed.PipelineId,
                options with { Mode = MarkdownPresentationMode.Raw }, offset);
            _rawPages[preparedKey] = state;
            return true;
        }
        if (state is null || !state.Previous.TryPop(out var previous)) return false;
        state.Current = previous;
        if (state.Previous.Count == 0 && ReferenceEquals(previous, RequirePrepared(document.Revision, key)))
            _rawPages.TryRemove(preparedKey, out _);
        return true;
    }

    /// <summary>Copies a visual range semantically, excluding decorative borders, rails, markers, and padding.</summary>
    public string GetSafeClipboardText(MarkdownLayout layout, MarkdownVisualSelection selection)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Rows.IsEmpty) return string.Empty;
        var forward = selection.StartRow < selection.EndRow ||
            selection.StartRow == selection.EndRow && selection.StartColumn <= selection.EndColumn;
        var startRow = Math.Clamp(forward ? selection.StartRow : selection.EndRow, 0, layout.Rows.Length - 1);
        var endRow = Math.Clamp(forward ? selection.EndRow : selection.StartRow, 0, layout.Rows.Length - 1);
        var startColumn = Math.Max(0, forward ? selection.StartColumn : selection.EndColumn);
        var endColumn = Math.Max(0, forward ? selection.EndColumn : selection.StartColumn);
        var result = new StringBuilder();
        for (var row = startRow; row <= endRow; row++)
        {
            if (row > startRow) result.AppendLine();
            var column = 0;
            foreach (var run in layout.Rows[row].Line.Runs)
            {
                var offset = 0;
                while (offset < run.Text.Length)
                {
                    var length = StringInfo.GetNextTextElementLength(run.Text.AsSpan(offset));
                    var grapheme = run.Text.AsSpan(offset, length);
                    var graphemeWidth = HPD.TUI.Utilities.UnicodeWidth.GetWidth(grapheme);
                    var selected = !run.IsDecorative &&
                        (row != startRow || column + graphemeWidth > startColumn) &&
                        (row != endRow || column <= endColumn);
                    if (selected) result.Append(grapheme);
                    column += graphemeWidth;
                    offset += length;
                }
            }
        }
        return MarkdownMessageDocument.SanitizeForDisclosure(result.ToString().TrimEnd());
    }

    private static StyledTerminalLine ShiftSourceOffsets(StyledTerminalLine line, int offset) =>
        new(line.Runs.Select(run => new StyledTerminalRun(
            run.Text,
            run.Style,
            run.Hyperlink,
            run.SourceStart is { } start ? start + offset : null,
            run.SourceEndExclusive is { } end ? end + offset : null,
            run.IsDecorative,
            run.SourceMap.IsDefault
                ? []
                : run.SourceMap.Select(segment => segment with
                {
                    SourceStart = segment.SourceStart + offset,
                    SourceEndExclusive = segment.SourceEndExclusive + offset
                }).ToImmutableArray())).ToImmutableArray());

    private void EvictOldest()
    {
        var node = _lru.First;
        if (node is null) return;
        _lru.RemoveFirst();
        if (_blocks.Remove(node.Value, out var entry))
        {
            _cacheBytes -= entry.Weight;
            _cacheEvictions++;
        }
    }

    private static long EstimateBytes(string source, MarkdownBlockLayout layout)
    {
        long bytes = source.Length * sizeof(char) + 96;
        foreach (var line in layout.Lines)
        {
            bytes += 48;
            foreach (var run in line.Runs) bytes += 64 + (run.Text.Length * sizeof(char));
        }
        return bytes;
    }

    private sealed record CacheEntry(
        MarkdownBlockLayout Layout,
        LinkedListNode<BlockCacheKey> Node,
        long Weight);

    private sealed class RawPageState(MarkdownLayout current)
    {
        internal MarkdownLayout Current { get; set; } = current;
        internal Stack<MarkdownLayout> Previous { get; } = [];
    }

    private readonly record struct BlockCacheKey(
        int Ordinal, int Start, int End, string ExactSource, string PipelineId, int Width,
        ThemeKey ThemeKey, ColorSystem ColorSystem, MarkdownPresentationMode Mode, long SyntaxThemeRevision,
        MarkdownSpacingKey SpacingKey,
        MarkdownResourceLimitsKey ResourceLimitsKey,
        long GlobalRevision);

    private readonly record struct PreparedKey(long Revision, MarkdownLayoutKey LayoutKey);
    private readonly record struct PreparedDocumentStamp(
        MarkdownMessageState State, int ParsedSourceLength, int UnparsedTailLength, long Epoch);
}
