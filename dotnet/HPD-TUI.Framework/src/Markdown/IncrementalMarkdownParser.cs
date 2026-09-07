using Markdig.Syntax;
using HPD.TUI.Observability;

namespace HPD.TUI.Markdown;

/// <summary>Parses a canonical Markdown source through conservative suffix reparsing.</summary>
public interface IIncrementalMarkdownParser
{
    /// <summary>Parses an initial canonical source.</summary>
    MarkdownParseState ParseInitial(ReadOnlyMemory<char> source, MarkdownParseOptions options);

    /// <summary>Appends a complete-line suffix, reusing a proven-stable top-level prefix when safe.</summary>
    MarkdownParseState Append(MarkdownParseState previous, ReadOnlyMemory<char> completeLineSuffix, bool terminal);
}

/// <summary>Immutable state produced by a conservative incremental Markdown parse.</summary>
public sealed class MarkdownParseState
{
    internal MarkdownParseState(MarkdownDocumentSnapshot document, MarkdownParseOptions options,
        int stableSourceLength, long reparsedCharacters, int stablePrefixNodes, long fallbackCount,
        long peakParseStateBytes = 0)
    {
        Document = document;
        Options = options;
        StableSourceLength = stableSourceLength;
        ReparsedCharacters = reparsedCharacters;
        StablePrefixNodes = stablePrefixNodes;
        FallbackCount = fallbackCount;
        RetainedSourceBytes = document.CanonicalSource.RetainedBytes;
        PeakParseStateBytes = Math.Max(peakParseStateBytes, RetainedSourceBytes);
    }

    /// <summary>Gets the immutable semantic document.</summary>
    public MarkdownDocumentSnapshot Document { get; }
    /// <summary>Gets the source boundary before which top-level nodes are proven stable.</summary>
    public int StableSourceLength { get; }
    /// <summary>Gets the cumulative number of UTF-16 code units reparsed.</summary>
    public long ReparsedCharacters { get; }
    /// <summary>Gets the number of top-level nodes reused by the latest append.</summary>
    public int StablePrefixNodes { get; }
    /// <summary>Gets the cumulative conservative full-parse fallback count.</summary>
    public long FallbackCount { get; }
    /// <summary>Gets the bytes retained by canonical UTF-16 source chunks.</summary>
    public long RetainedSourceBytes { get; }
    /// <summary>Gets the maximum estimated bytes retained by parse state.</summary>
    public long PeakParseStateBytes { get; }
    internal MarkdownParseOptions Options { get; }
}

/// <summary>Markdig-backed parser that splices immutable stable-prefix blocks with a reparsed suffix.</summary>
public sealed class ConservativeIncrementalMarkdownParser : IIncrementalMarkdownParser
{
    private readonly IMarkdownDocumentParser _parser;
    private readonly TuiPerformanceCounters? _performanceCounters;

    /// <summary>Creates a parser using the default semantic Markdown parser.</summary>
    public ConservativeIncrementalMarkdownParser() : this(new MarkdownDocumentParser(), null) { }

    /// <summary>Creates a parser over a supplied full-document parser.</summary>
    public ConservativeIncrementalMarkdownParser(IMarkdownDocumentParser parser) : this(parser, null) { }

    /// <summary>Creates a parser with optional common performance-counter recording.</summary>
    /// <param name="parser">The full-document parser used for conservative suffix work.</param>
    /// <param name="performanceCounters">
    /// The recorder to update, or <see langword="null"/> to keep diagnostics allocation-free.
    /// </param>
    public ConservativeIncrementalMarkdownParser(
        IMarkdownDocumentParser parser,
        TuiPerformanceCounters? performanceCounters)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _performanceCounters = performanceCounters;
    }

    /// <inheritdoc />
    public MarkdownParseState ParseInitial(ReadOnlyMemory<char> source, MarkdownParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var text = source.ToString();
        var document = _parser.Parse(text, options);
        var result = new MarkdownParseState(
            document, options, FindStableBoundary(document, terminal: false), text.Length, 0, 0);
        _performanceCounters?.RecordMarkdownWork(0, text.Length);
        return result;
    }

    /// <inheritdoc />
    public MarkdownParseState Append(MarkdownParseState previous, ReadOnlyMemory<char> completeLineSuffix, bool terminal)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (completeLineSuffix.IsEmpty && !terminal) return previous;
        var old = previous.Document;
        var appended = completeLineSuffix.ToString();
        var source = old.CanonicalSource.Append(completeLineSuffix);
        var reparseStart = terminal ? previous.StableSourceLength : previous.StableSourceLength;
        var stableBlocks = old.Blocks.TakeWhile(block => block.SourceEndExclusive <= reparseStart).ToArray();
        var tailText = source.Slice(reparseStart, source.Length - reparseStart);
        var tail = _parser.Parse(tailText, previous.Options);

        if (RequiresFullParse(old, tail, appended))
        {
            var parsed = _parser.Parse(source.Materialize(), previous.Options);
            var full = RebindSource(parsed, source);
            return Record(previous, new(full, previous.Options, FindStableBoundary(full, terminal),
                previous.ReparsedCharacters + source.Length, 0, previous.FallbackCount + 1,
                Math.Max(previous.PeakParseStateBytes, source.RetainedBytes + (long)source.Length * sizeof(char))));
        }

        var blocks = new List<MarkdownTopLevelBlock>(stableBlocks.Length + tail.Blocks.Count);
        blocks.AddRange(stableBlocks);
        foreach (var tailBlock in tail.Blocks)
        {
            ShiftTree(tailBlock.Syntax, reparseStart);
            blocks.Add(new MarkdownTopLevelBlock(
                tailBlock.SourceStart + reparseStart,
                tailBlock.SourceEndExclusive + reparseStart,
                tailBlock.Kind,
                blocks.Count) { Syntax = tailBlock.Syntax });
        }

        var capabilities = old.NodeCapabilities.Concat(tail.NodeCapabilities).Distinct().
            OrderBy(static capability => capability.RuntimeType, StringComparer.Ordinal).ToArray();
        var document = new MarkdownDocumentSnapshot(source, blocks,
            old.Features | tail.Features, Array.AsReadOnly(capabilities),
            Math.Max(old.MaximumObservedNestingDepth, tail.MaximumObservedNestingDepth),
            old.Pipeline, tail.Syntax);
        var stableBoundary = FindStableBoundary(document, terminal);
        if (!terminal && stableBoundary > previous.StableSourceLength)
        {
            // A block crossing the stable boundary is reparsed once in isolation. This removes
            // Markdig inline segmentation caused by arbitrarily small stream deltas without
            // flattening or reparsing the complete accumulated document.
            for (var index = 0; index < blocks.Count; index++)
            {
                var block = blocks[index];
                if (block.SourceEndExclusive > stableBoundary ||
                    block.SourceEndExclusive <= previous.StableSourceLength)
                    continue;
                var blockText = source.Slice(block.SourceStart, block.SourceEndExclusive - block.SourceStart);
                var canonicalBlock = _parser.Parse(blockText, previous.Options);
                if (canonicalBlock.Blocks.Count != 1) continue;
                var parsed = canonicalBlock.Blocks[0];
                ShiftTree(parsed.Syntax, block.SourceStart);
                blocks[index] = block with { Syntax = parsed.Syntax };
            }
            document = new MarkdownDocumentSnapshot(source, blocks,
                document.Features, document.NodeCapabilities, document.MaximumObservedNestingDepth,
                old.Pipeline, tail.Syntax);
        }
        // Finalization deliberately performs the clean parse required by the public semantic
        // equivalence contract. Stable-boundary advancement remains suffix-only: each newly
        // stable block was canonicalized independently above.
        if (terminal)
        {
            var parsed = _parser.Parse(source.Materialize(), previous.Options);
            var canonical = RebindSource(parsed, source);
            return Record(previous, new(canonical, previous.Options, FindStableBoundary(canonical, terminal),
                previous.ReparsedCharacters + tailText.Length + source.Length,
                stableBlocks.Length, previous.FallbackCount,
                Math.Max(previous.PeakParseStateBytes, source.RetainedBytes + (long)source.Length * sizeof(char))));
        }
        return Record(previous, new(document, previous.Options, stableBoundary,
            previous.ReparsedCharacters + tailText.Length, stableBlocks.Length, previous.FallbackCount,
            Math.Max(previous.PeakParseStateBytes, source.RetainedBytes + (long)tailText.Length * sizeof(char))));
    }

    private MarkdownParseState Record(MarkdownParseState previous, MarkdownParseState current)
    {
        _performanceCounters?.RecordMarkdownWork(
            current.StablePrefixNodes,
            current.ReparsedCharacters - previous.ReparsedCharacters);
        return current;
    }

    private static MarkdownDocumentSnapshot RebindSource(
        MarkdownDocumentSnapshot parsed, MarkdownSourceText canonicalSource) =>
        new(canonicalSource, parsed.Blocks, parsed.Features, parsed.NodeCapabilities,
            parsed.MaximumObservedNestingDepth, parsed.Pipeline, parsed.Syntax);

    private static bool RequiresFullParse(MarkdownDocumentSnapshot previous, MarkdownDocumentSnapshot tail, string suffix)
    {
        const MarkdownDocumentFeatures global = MarkdownDocumentFeatures.ReferenceDefinitions |
                                                MarkdownDocumentFeatures.ExtensionGlobalState;
        if (((previous.Features | tail.Features) & global) != 0) return true;
        // HTML blocks and reference/footnote definitions can change parsing outside the suffix boundary.
        return tail.Features.HasFlag(MarkdownDocumentFeatures.Html) ||
               suffix.Contains("[^", StringComparison.Ordinal) ||
               suffix.Contains("]: ", StringComparison.Ordinal) ||
               suffix.Contains("]:", StringComparison.Ordinal);
    }

    private static void ShiftTree(MarkdownObject root, int offset)
    {
        Shift(root);
        foreach (var node in root.Descendants()) Shift(node);
        void Shift(MarkdownObject node)
        {
            if (node.Span.Start < 0) return;
            node.Span = new SourceSpan(
                checked(node.Span.Start + offset), checked(node.Span.End + offset));
        }
    }

    private static int FindStableBoundary(MarkdownDocumentSnapshot snapshot, bool terminal)
    {
        if (terminal) return snapshot.SourceLength;
        if ((snapshot.Features & (MarkdownDocumentFeatures.ReferenceDefinitions |
                                  MarkdownDocumentFeatures.ExtensionGlobalState)) != 0) return 0;
        if (snapshot.Blocks.Count < 2) return 0;
        for (var candidateIndex = snapshot.Blocks.Count - 1; candidateIndex > 0; candidateIndex--)
        {
            var preceding = snapshot.Blocks[candidateIndex - 1];
            var following = snapshot.Blocks[candidateIndex];
            var blankSeparated = HasBlankLine(snapshot.CanonicalSource,
                preceding.SourceEndExclusive, following.SourceStart);
            var proven = preceding.Kind switch
            {
                MarkdownBlockKind.ThematicBreak => true,
                MarkdownBlockKind.Paragraph or MarkdownBlockKind.Heading => blankSeparated,
                MarkdownBlockKind.Quote => blankSeparated && following.Kind != MarkdownBlockKind.Quote,
                MarkdownBlockKind.List => blankSeparated && following.Kind != MarkdownBlockKind.List,
                MarkdownBlockKind.Table => following.Kind != MarkdownBlockKind.Table,
                MarkdownBlockKind.Code => blankSeparated,
                _ => false
            };
            if (proven) return following.SourceStart;
        }
        return 0;
    }

    private static bool HasBlankLine(MarkdownSourceText source, int start, int endExclusive)
    {
        var lineBreaks = 0;
        for (var index = Math.Clamp(start, 0, source.Length);
             index < Math.Clamp(endExclusive, 0, source.Length); index++)
            if (source[index] == '\n' && ++lineBreaks >= 2) return true;
        return false;
    }
}
