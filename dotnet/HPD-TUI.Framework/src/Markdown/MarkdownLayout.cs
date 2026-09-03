using System.Collections.Immutable;
using HPD.TUI.Core;

namespace HPD.TUI.Markdown;

/// <summary>Specifies the prepared Markdown presentation mode.</summary>
public enum MarkdownPresentationMode { Rich, Raw }

/// <summary>Identifies every semantic input to a prepared Markdown layout.</summary>
public readonly record struct MarkdownLayoutKey(
    string PipelineId,
    string RendererPolicyId,
    int Width,
    ThemeKey ThemeKey,
    ColorSystem ColorSystem,
    MarkdownPresentationMode Mode,
    long SyntaxThemeRevision);

/// <summary>Represents an immutable, prepared terminal layout.</summary>
public sealed class MarkdownLayout
{
    /// <summary>Gets the exact preparation key.</summary>
    public required MarkdownLayoutKey Key { get; init; }
    /// <summary>Gets independently addressable block layouts.</summary>
    public required ImmutableArray<MarkdownBlockLayout> Blocks { get; init; }
    /// <summary>Gets the fully composed rows rendered by <c>MarkdownView</c>.</summary>
    public required ImmutableArray<MarkdownLayoutRow> Rows { get; init; }
    /// <summary>Gets the rendered height.</summary>
    public int Height => Rows.Length;
}

/// <summary>Represents prepared content for one canonical source range.</summary>
public sealed class MarkdownBlockLayout
{
    /// <summary>Gets the inclusive canonical source start.</summary>
    public required int SourceStart { get; init; }
    /// <summary>Gets the exclusive canonical source end.</summary>
    public required int SourceEndExclusive { get; init; }
    /// <summary>Gets the styled terminal lines.</summary>
    public required ImmutableArray<StyledTerminalLine> Lines { get; init; }
}

/// <summary>Identifies the role of one composed layout row.</summary>
public enum MarkdownLayoutRowKind { BlockContent, Separator, LiteralTail }

/// <summary>Represents one composed Markdown row and its logical source mapping.</summary>
public sealed record MarkdownLayoutRow(
    MarkdownLayoutRowKind Kind,
    StyledTerminalLine Line,
    int? BlockOrdinal,
    int? SourceStart,
    int? SourceEndExclusive,
    bool IsDecorative);

/// <summary>Represents one immutable terminal line.</summary>
public sealed record StyledTerminalLine(ImmutableArray<StyledTerminalRun> Runs)
{
    /// <summary>Gets an empty styled line.</summary>
    public static StyledTerminalLine Empty { get; } = new([]);
}

/// <summary>Represents one immutable styled text run with optional structural link metadata.</summary>
public readonly record struct StyledTerminalRun(string Text, Style Style, TerminalHyperlink? Hyperlink = null);
