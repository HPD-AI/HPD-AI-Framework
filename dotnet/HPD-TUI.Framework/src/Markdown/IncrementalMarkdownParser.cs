using Markdig.Syntax;

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
        int stableSourceLength, long reparsedCharacters, int stablePrefixNodes, long fallbackCount)
    {
        Document = document;
        Options = options;
        StableSourceLength = stableSourceLength;
        ReparsedCharacters = reparsedCharacters;
        StablePrefixNodes = stablePrefixNodes;
        FallbackCount = fallbackCount;
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
    internal MarkdownParseOptions Options { get; }
}

/// <summary>Markdig-backed parser that splices immutable stable-prefix blocks with a reparsed suffix.</summary>
public sealed class ConservativeIncrementalMarkdownParser : IIncrementalMarkdownParser
{
    private readonly IMarkdownDocumentParser _parser;

    /// <summary>Creates a parser using the default semantic Markdown parser.</summary>
    public ConservativeIncrementalMarkdownParser() : this(new MarkdownDocumentParser()) { }

    /// <summary>Creates a parser over a supplied full-document parser.</summary>
    public ConservativeIncrementalMarkdownParser(IMarkdownDocumentParser parser) =>
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));

    /// <inheritdoc />
    public MarkdownParseState ParseInitial(ReadOnlyMemory<char> source, MarkdownParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var text = source.ToString();
        var document = _parser.Parse(text, options);
        return new(document, options, FindStableBoundary(document, terminal: false), text.Length, 0, 0);
    }

    /// <inheritdoc />
    public MarkdownParseState Append(MarkdownParseState previous, ReadOnlyMemory<char> completeLineSuffix, bool terminal)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (completeLineSuffix.IsEmpty && !terminal) return previous;
        var old = previous.Document;
        var appended = completeLineSuffix.ToString();
        var source = string.Concat(old.Source, appended);
        var reparseStart = terminal ? previous.StableSourceLength : previous.StableSourceLength;
        var stableBlocks = old.Blocks.TakeWhile(block => block.SourceEndExclusive <= reparseStart).ToArray();
        var tailText = source[reparseStart..];
        var tail = _parser.Parse(tailText, previous.Options);

        if (RequiresFullParse(old, tail, appended))
        {
            var full = _parser.Parse(source, previous.Options);
            return new(full, previous.Options, FindStableBoundary(full, terminal),
                previous.ReparsedCharacters + source.Length, 0, previous.FallbackCount + 1);
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
        // Canonicalize a block exactly once as it crosses into the stable prefix. Markdig may
        // retain inline segmentation from a suffix parse; a boundary parse prevents chunking
        // choices from becoming observable in immutable layout snapshots.
        if (terminal || stableBoundary > previous.StableSourceLength)
        {
            var canonical = _parser.Parse(source, previous.Options);
            return new(canonical, previous.Options, FindStableBoundary(canonical, terminal),
                previous.ReparsedCharacters + tailText.Length + source.Length,
                stableBlocks.Length, previous.FallbackCount);
        }
        return new(document, previous.Options, stableBoundary,
            previous.ReparsedCharacters + tailText.Length, stableBlocks.Length, previous.FallbackCount);
    }

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
        if (terminal) return snapshot.Source.Length;
        if ((snapshot.Features & (MarkdownDocumentFeatures.ReferenceDefinitions |
                                  MarkdownDocumentFeatures.ExtensionGlobalState)) != 0) return 0;
        if (snapshot.Blocks.Count < 2) return 0;
        // Keep the final top-level block mutable; its start is the conservative suffix boundary.
        return snapshot.Blocks[^1].SourceStart;
    }
}
