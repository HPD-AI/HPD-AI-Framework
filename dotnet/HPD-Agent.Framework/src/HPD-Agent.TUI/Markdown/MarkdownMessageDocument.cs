using HPD.TUI.Markdown;
using System.Collections.Immutable;
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
    /// <summary>Gets the latest parsed complete-line prefix.</summary>
    public required MarkdownDocumentSnapshot Parsed { get; init; }
    /// <summary>Gets the exact incomplete physical-line tail.</summary>
    public required string UnparsedTail { get; init; }
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

    /// <summary>Reconstructs the exact accepted UTF-16 source.</summary>
    public string GetCanonicalSource() => Parsed.Source + UnparsedTail;
}

/// <summary>Retains prepared block projections across immutable document publications.</summary>
public sealed class MarkdownMessageProjection
{
    private const int MaximumCacheEntries = 256;
    private const long MaximumCacheBytes = 4 * 1024 * 1024;
    private const long MaximumEntryBytes = 512 * 1024;
    private readonly Dictionary<BlockCacheKey, CacheEntry> _blocks = [];
    private readonly LinkedList<BlockCacheKey> _lru = [];
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

    internal MarkdownLayout ResolveLayout(
        MarkdownMessageDocument document,
        MarkdownLayoutOptions options,
        IMarkdownLayoutEngine engine)
    {
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
        foreach (var block in document.Parsed.Blocks)
        {
            var exactSource = document.Parsed.Source[block.SourceStart..block.SourceEndExclusive];
            var key = new BlockCacheKey(block.Ordinal, block.SourceStart, block.SourceEndExclusive, exactSource,
                document.Parsed.PipelineId, options.Width, frameworkThemeKey, options.ColorSystem, options.Mode,
                options.SyntaxThemeRevision,
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
            if (rows.Count > 0) rows.Add(new(MarkdownLayoutRowKind.Separator, StyledTerminalLine.Empty, null, null, null, true));
            foreach (var line in layout.Lines)
                rows.Add(new(MarkdownLayoutRowKind.BlockContent, line, block.Ordinal, block.SourceStart, block.SourceEndExclusive, false));
            layouts.Add(layout);
        }

        if (document.UnparsedTail.Length > 0)
        {
            if (rows.Count > 0) rows.Add(new(MarkdownLayoutRowKind.Separator, StyledTerminalLine.Empty, null, null, null, true));
            var safeTail = Sanitize(document.UnparsedTail);
            foreach (var line in safeTail.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                rows.Add(new(MarkdownLayoutRowKind.LiteralTail,
                    new StyledTerminalLine([new StyledTerminalRun(line, options.Theme.Body)]), null,
                    document.Parsed.Source.Length, document.GetCanonicalSource().Length, false));
        }

        return new MarkdownLayout
        {
            Key = new(document.Parsed.PipelineId, "terminal-v1", options.Width, frameworkThemeKey,
                options.ColorSystem, options.Mode, options.SyntaxThemeRevision),
            Blocks = layouts.ToImmutable(),
            Rows = rows.ToImmutable()
        };
    }

    private static string Sanitize(string source)
    {
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var chars = source.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] != '\t' && chars[i] != '\n' && TerminalTextSafety.IsUnsafe(chars[i])) chars[i] = '�';
        return new string(chars);
    }

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
        long GlobalRevision);
}
