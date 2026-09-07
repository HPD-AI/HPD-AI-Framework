using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace HPD.TUI.Markdown;

/// <summary>Immutable public metadata for one exact canonical-source parse.</summary>
public sealed class MarkdownDocumentSnapshot
{
    internal MarkdownDocumentSnapshot(string source, IReadOnlyList<MarkdownTopLevelBlock> blocks,
        MarkdownDocumentFeatures features, IReadOnlyList<MarkdownNodeCapability> nodeCapabilities,
        int maximumObservedNestingDepth, MarkdownPipelineDescriptor pipeline, Markdig.Syntax.MarkdownDocument syntax)
    {
        CanonicalSource = MarkdownSourceText.FromString(source);
        Blocks = blocks;
        Features = features;
        NodeCapabilities = nodeCapabilities;
        MaximumObservedNestingDepth = maximumObservedNestingDepth;
        PipelineId = pipeline.StableId;
        Pipeline = pipeline;
        Syntax = syntax;
    }

    internal MarkdownDocumentSnapshot(MarkdownSourceText source, IReadOnlyList<MarkdownTopLevelBlock> blocks,
        MarkdownDocumentFeatures features, IReadOnlyList<MarkdownNodeCapability> nodeCapabilities,
        int maximumObservedNestingDepth, MarkdownPipelineDescriptor pipeline, Markdig.Syntax.MarkdownDocument syntax)
    {
        CanonicalSource = source;
        Blocks = blocks;
        Features = features;
        NodeCapabilities = nodeCapabilities;
        MaximumObservedNestingDepth = maximumObservedNestingDepth;
        PipelineId = pipeline.StableId;
        Pipeline = pipeline;
        Syntax = syntax;
    }

    /// <summary>Gets the exact parsed UTF-16 source.</summary>
    public string Source => CanonicalSource.Materialize();
    /// <summary>Gets the exact canonical-source length without flattening shared chunks.</summary>
    public int SourceLength => CanonicalSource.Length;
    /// <summary>Gets normalized top-level block spans.</summary>
    public IReadOnlyList<MarkdownTopLevelBlock> Blocks { get; }
    /// <summary>Gets document-wide semantic features.</summary>
    public MarkdownDocumentFeatures Features { get; }
    /// <summary>Gets sorted runtime node capabilities encountered during semantic analysis.</summary>
    public IReadOnlyList<MarkdownNodeCapability> NodeCapabilities { get; }
    /// <summary>Gets the deepest parser node level observed in this immutable snapshot.</summary>
    public int MaximumObservedNestingDepth { get; }
    /// <summary>Gets the structural pipeline identity.</summary>
    public string PipelineId { get; }
    internal Markdig.Syntax.MarkdownDocument Syntax { get; }
    internal MarkdownPipelineDescriptor Pipeline { get; }
    internal MarkdownSourceText CanonicalSource { get; }
}

/// <summary>Declares the audited terminal handling selected for one parser runtime node type.</summary>
/// <param name="RuntimeType">The fully qualified Markdig runtime-node type.</param>
/// <param name="TerminalHandling">The handling selected from the frozen terminal registry.</param>
/// <param name="RendererType">The renderer responsible for the node, when known.</param>
public sealed record MarkdownNodeCapability(
    string RuntimeType,
    MarkdownTerminalNodeHandling TerminalHandling,
    string? RendererType = null);

/// <summary>Identifies whether a parser node has typed terminal behavior or sanitized span fallback.</summary>
public enum MarkdownTerminalNodeHandling
{
    /// <summary>A registered object renderer directly accepts the node.</summary>
    TypedRenderer,
    /// <summary>A registered parent renderer consumes the node structurally.</summary>
    OwnedByParentRenderer,
    /// <summary>The terminal's sanitized canonical-source fallback handles the node.</summary>
    SanitizedSourceFallback
}

/// <summary>Describes one top-level parsed block and its canonical-source range.</summary>
public sealed record MarkdownTopLevelBlock(int SourceStart, int SourceEndExclusive, MarkdownBlockKind Kind, int Ordinal)
{
    internal Markdig.Syntax.Block Syntax { get; init; } = null!;

    internal static MarkdownTopLevelBlock From(Markdig.Syntax.Block block, int ordinal, int sourceLength)
    {
        var start = Math.Clamp(block.Span.Start, 0, sourceLength);
        var end = block.Span.End < start ? start : Math.Clamp(block.Span.End + 1, start, sourceLength);
        return new(start, end, MarkdownSemanticAnalysis.GetKind(block), ordinal) { Syntax = block };
    }
}

/// <summary>Classifies top-level Markdown blocks for spacing and streaming policy.</summary>
public enum MarkdownBlockKind { Paragraph, Heading, List, Quote, Code, Table, ThematicBreak, Html, Other }

/// <summary>Flags semantics that can invalidate previously stable output.</summary>
[Flags]
public enum MarkdownDocumentFeatures { None = 0, ReferenceDefinitions = 1, Tables = 2, Html = 4, ExtensionGlobalState = 8 }

internal static class MarkdownSemanticAnalysis
{
    internal static MarkdownDocumentFeatures GetFeatures(Markdig.Syntax.MarkdownDocument document)
    {
        var features = MarkdownDocumentFeatures.None;
        if (document.GetLinkReferenceDefinitions(addGroup: false).Links.Count > 0)
            features |= MarkdownDocumentFeatures.ReferenceDefinitions;
        foreach (var node in document.Descendants())
        {
            if (node is Table) features |= MarkdownDocumentFeatures.Tables;
            if (node is HtmlBlock) features |= MarkdownDocumentFeatures.Html;
            if (node.GetType().Name.Contains("LinkReferenceDefinition", StringComparison.Ordinal)) features |= MarkdownDocumentFeatures.ReferenceDefinitions;
        }
        return features;
    }

    internal static MarkdownBlockKind GetKind(Markdig.Syntax.Block block) => block switch
    {
        HeadingBlock => MarkdownBlockKind.Heading,
        ParagraphBlock => MarkdownBlockKind.Paragraph,
        ListBlock => MarkdownBlockKind.List,
        QuoteBlock => MarkdownBlockKind.Quote,
        CodeBlock => MarkdownBlockKind.Code,
        Table => MarkdownBlockKind.Table,
        ThematicBreakBlock => MarkdownBlockKind.ThematicBreak,
        HtmlBlock => MarkdownBlockKind.Html,
        _ => MarkdownBlockKind.Other
    };
}
