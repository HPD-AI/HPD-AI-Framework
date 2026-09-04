using HPD.TUI.Core;

namespace HPD.TUI.Rendering;

/// <summary>Describes one immutable visual cell prepared for terminal scrollback.</summary>
/// <param name="Grapheme">The grapheme displayed by the leading cell.</param>
/// <param name="Style">The semantic terminal style.</param>
/// <param name="Metadata">Structural metadata such as a hyperlink destination.</param>
/// <param name="DisplayWidth">The terminal-column width of the grapheme.</param>
public readonly record struct ScrollbackCell(
    string Grapheme,
    Style Style,
    TerminalRunMetadata Metadata,
    byte DisplayWidth);

/// <summary>Describes one immutable, prewrapped terminal scrollback row.</summary>
/// <param name="Id">The stable semantic row identity.</param>
/// <param name="Cells">The leading visual cells in display order.</param>
public sealed record ScrollbackRow(string Id, IReadOnlyList<ScrollbackCell> Cells);

/// <summary>Describes one contiguous scrollback publication prepared from an application model.</summary>
/// <param name="PresentationEpoch">The application presentation epoch.</param>
/// <param name="FirstSequence">The first semantic sequence represented by the batch.</param>
/// <param name="Rows">The immutable rows to publish.</param>
public sealed record ScrollbackBatch(
    long PresentationEpoch,
    long FirstSequence,
    IReadOnlyList<ScrollbackRow> Rows);

/// <summary>Projects immutable application history into managed-terminal scrollback batches.</summary>
public interface IScrollbackSource
{
    /// <summary>Prepares the next contiguous batch without advancing the source watermark.</summary>
    /// <param name="context">The current render context.</param>
    /// <param name="maxRows">The maximum number of rows to prepare.</param>
    /// <returns>The next batch, or <see langword="null"/> when no immutable rows are available.</returns>
    ScrollbackBatch? PrepareScrollback(in RenderContext context, int maxRows);

    /// <summary>Advances the source watermark after complete transport acceptance.</summary>
    /// <param name="batch">The accepted batch returned by <see cref="PrepareScrollback"/>.</param>
    void CommitScrollback(ScrollbackBatch batch);

    /// <summary>Releases a batch that was not accepted without advancing the source watermark.</summary>
    /// <param name="batch">The unaccepted batch returned by <see cref="PrepareScrollback"/>.</param>
    void RollbackScrollback(ScrollbackBatch batch);
}
