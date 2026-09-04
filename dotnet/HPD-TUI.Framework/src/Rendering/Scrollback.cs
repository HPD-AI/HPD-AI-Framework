using HPD.TUI.Core;
using HPD.TUI.Terminal;

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

/// <summary>Owns an immutable scrollback batch for exactly one asynchronous commit attempt.</summary>
public sealed class ScrollbackBatchLease : IDisposable
{
    private ScrollbackBatch? _batch;

    /// <summary>Creates a lease over a prepared immutable batch.</summary>
    public ScrollbackBatchLease(ScrollbackBatch batch) => _batch = batch ?? throw new ArgumentNullException(nameof(batch));

    /// <summary>Gets the leased batch while the lease is active.</summary>
    public ScrollbackBatch Batch => _batch ?? throw new ObjectDisposedException(nameof(ScrollbackBatchLease));

    /// <summary>Releases the batch reference after the commit attempt completes.</summary>
    public void Dispose() => _batch = null;
}

/// <summary>Controls recovery policy for one append-only scrollback commit.</summary>
/// <param name="RecoveryPolicy">The explicit response to uncertain terminal-visible state.</param>
public readonly record struct ScrollbackCommitOptions(
    ManagedTerminalRecoveryPolicy RecoveryPolicy = ManagedTerminalRecoveryPolicy.VisibleEpochBoundary);

/// <summary>Identifies the transport outcome of a scrollback commit.</summary>
public enum ScrollbackCommitStatus
{
    /// <summary>The complete batch and reconstructed live screen were accepted.</summary>
    Written,
    /// <summary>No bytes were accepted because the transport was backpressured.</summary>
    Backpressured,
    /// <summary>The write failed and may have emitted a prefix.</summary>
    Failed
}

/// <summary>Reports the result of one scrollback commit attempt.</summary>
/// <param name="Status">The transport outcome.</param>
/// <param name="CommittedThroughSequence">The exclusive committed sequence watermark after success.</param>
/// <param name="Error">The publication error, when failed.</param>
public readonly record struct ScrollbackCommitResult(
    ScrollbackCommitStatus Status,
    long CommittedThroughSequence,
    Exception? Error = null);

/// <summary>Publishes contiguous immutable rows into terminal-owned append-only history.</summary>
public interface IScrollbackJournal
{
    /// <summary>Attempts one transactional history append and live-screen reconstruction.</summary>
    ValueTask<ScrollbackCommitResult> CommitAsync(
        ScrollbackBatchLease batch,
        ScrollbackCommitOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>Projects immutable application history into managed-terminal scrollback batches.</summary>
public interface IScrollbackSource
{
    /// <summary>Starts a new terminal presentation epoch and releases any unaccepted batch.</summary>
    /// <param name="presentationEpoch">The renderer epoch that subsequent batches must identify.</param>
    /// <param name="context">The new physical viewport used to reflow the uncommitted tail.</param>
    void ResetPresentation(long presentationEpoch, in RenderContext context);

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
