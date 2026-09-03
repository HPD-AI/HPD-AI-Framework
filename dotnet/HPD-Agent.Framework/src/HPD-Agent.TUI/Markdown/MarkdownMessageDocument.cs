using HPD.TUI.Markdown;
using System.Collections.Immutable;
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
    string? ThreadId = null);

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
    internal static MarkdownExactSourceAuthority AuthorizeExact(MarkdownMessageDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Presentation.Visibility == AgentMessageVisibility.Hidden)
            throw new UnauthorizedAccessException("This Markdown source is not authorized for exact export.");
        return new(document.Identity, document.LineageId);
    }
}

/// <summary>Identifies an inclusive visual selection in a prepared Markdown layout.</summary>
public readonly record struct MarkdownVisualSelection(int StartRow, int StartColumn, int EndRow, int EndColumn);

/// <summary>Retains prepared block projections across immutable document publications.</summary>
public sealed class MarkdownMessageProjection
{
    private const int MaximumCacheEntries = 256;
    private const long MaximumCacheBytes = 4 * 1024 * 1024;
    private const long MaximumEntryBytes = 512 * 1024;
    private readonly Dictionary<BlockCacheKey, CacheEntry> _blocks = [];
    private readonly LinkedList<BlockCacheKey> _lru = [];
    private readonly Dictionary<PreparedKey, MarkdownLayout> _prepared = [];
    private IAgentTuiDispatcher? _dispatcher;
    private long _cacheBytes;
    private long _cachedEpoch = -1;
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

    internal void BindDispatcher(IAgentTuiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (_dispatcher is not null && !ReferenceEquals(_dispatcher, dispatcher))
            throw new InvalidOperationException("A Markdown projection cannot change dispatcher ownership.");
        _dispatcher = dispatcher;
    }

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
        foreach (var block in document.Parsed.Blocks)
        {
            var exactSource = document.Parsed.Source[block.SourceStart..block.SourceEndExclusive];
            var key = new BlockCacheKey(block.Ordinal, block.SourceStart, block.SourceEndExclusive, exactSource,
                document.Parsed.PipelineId, options.Width, frameworkThemeKey, options.ColorSystem, options.Mode,
                options.SyntaxThemeRevision, (options.Spacing ?? new MarkdownSpacing()).Key,
                (document.Parsed.Features & (MarkdownDocumentFeatures.ReferenceDefinitions | MarkdownDocumentFeatures.ExtensionGlobalState)) != 0
                    ? document.Revision : 0);
            MarkdownBlockLayout layout;
            if (_blocks.TryGetValue(key, out var cached))
            {
                layout = cached.Layout;
                _lru.Remove(cached.Node);
                _lru.AddLast(cached.Node);
            }
            else
            {
                layout = engine.LayoutBlock(document.Parsed, block, options);
                var isStable = block.SourceEndExclusive <= document.StableSourceLength;
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
                for (var gap = 0; gap < MarkdownLayoutEngine.GetSeparatorRows(previous!, block, options.Spacing ?? new MarkdownSpacing()); gap++)
                    rows.Add(new(MarkdownLayoutRowKind.Separator, StyledTerminalLine.Empty, null, null, null, true));
            foreach (var line in layout.Lines)
                rows.Add(new(MarkdownLayoutRowKind.BlockContent, line, block.Ordinal, block.SourceStart, block.SourceEndExclusive, false));
            layouts.Add(layout);
            previous = block;
        }

        if (document.UnparsedTail.Length > 0)
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
        }

        return new MarkdownLayout
        {
            Key = new(document.Parsed.PipelineId, "terminal-v1", options.Width, frameworkThemeKey,
                options.ColorSystem, options.Mode, options.SyntaxThemeRevision, (options.Spacing ?? new MarkdownSpacing()).Key),
            Blocks = layouts.ToImmutable(),
            Rows = rows.ToImmutable()
        };
    }

    /// <summary>Prepares and retains an immutable layout at a dispatcher publication boundary.</summary>
    internal MarkdownLayout Prepare(
        MarkdownMessageDocument document,
        MarkdownLayoutOptions options,
        IMarkdownLayoutEngine engine)
    {
        if (_dispatcher is not null && !_dispatcher.CheckAccess())
            throw new InvalidOperationException("Markdown layout preparation must run on the owning TUI dispatcher.");
        var expectedKey = new MarkdownLayoutKey(document.Parsed.PipelineId, "terminal-v1", options.Width,
            options.Theme.ThemeKey, options.ColorSystem, options.Mode, options.SyntaxThemeRevision,
            (options.Spacing ?? new MarkdownSpacing()).Key);
        if (_prepared.TryGetValue(new(document.Revision, expectedKey), out var prepared)) return prepared;
        var layout = ResolveLayout(document, options, engine);
        _prepared[new(document.Revision, layout.Key)] = layout;
        if (_prepared.Count > 8) _prepared.Remove(_prepared.Keys.First());
        return layout;
    }

    /// <summary>Gets an already prepared publication without parsing or laying out.</summary>
    public MarkdownLayout RequirePrepared(long revision, MarkdownLayoutKey key) =>
        _prepared.TryGetValue(new(revision, key), out var layout)
            ? layout
            : throw new InvalidOperationException("Markdown layout was not prepared for this publication context.");

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
            run.IsDecorative)).ToImmutableArray());

    private void EvictOldest()
    {
        var node = _lru.First;
        if (node is null) return;
        _lru.RemoveFirst();
        if (_blocks.Remove(node.Value, out var entry)) _cacheBytes -= entry.Weight;
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

    private readonly record struct BlockCacheKey(
        int Ordinal, int Start, int End, string ExactSource, string PipelineId, int Width,
        ThemeKey ThemeKey, ColorSystem ColorSystem, MarkdownPresentationMode Mode, long SyntaxThemeRevision,
        MarkdownSpacingKey SpacingKey,
        long GlobalRevision);

    private readonly record struct PreparedKey(long Revision, MarkdownLayoutKey LayoutKey);
}
