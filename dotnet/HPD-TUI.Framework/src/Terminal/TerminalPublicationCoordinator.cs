using System.Collections.Concurrent;

namespace HPD.TUI.Terminal;

internal enum TerminalCertainty { Known, Uncertain }

internal readonly record struct TerminalPresentationState(
    long PresentationEpoch,
    long CommittedWatermark,
    int LiveTop,
    int LiveHeight,
    int CursorRow,
    bool CursorVisible,
    TerminalCertainty Certainty);

/// <summary>
/// Owns the single asynchronous FIFO for terminal frames, history, control traffic, external output, recovery, and
/// shutdown. A proposed presentation tuple becomes observable only after its complete payload is accepted.
/// </summary>
internal sealed class TerminalPublicationCoordinator
{
    private readonly ITerminalOutputTransport _transport;
    private readonly ConcurrentQueue<Publication> _mailbox = new();
    private int _draining;
    private int _waiting;
    private TerminalPresentationState _state;

    public TerminalPublicationCoordinator(ITerminalOutputTransport transport)
        => _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public TerminalPresentationState State => _state;

    public TerminalWriteResult TryPublish(
        ReadOnlySpan<char> payload,
        CancellationToken cancellationToken = default,
        TerminalPresentationState? acceptedState = null)
    {
        var publication = new Publication(new TerminalFrameLease(payload), acceptedState, cancellationToken);
        _mailbox.Enqueue(publication);
        DrainMailbox();
        return publication.Completion.Task.GetAwaiter().GetResult();
    }

    private void DrainMailbox()
    {
        if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0) return;
        _ = DrainMailboxAsync();
    }

    private async Task DrainMailboxAsync()
    {
        try
        {
            while (_mailbox.TryDequeue(out var publication))
            {
                TerminalWriteResult result;
                if (publication.CancellationToken.IsCancellationRequested)
                {
                    result = new(TerminalWriteStatus.Failed,
                        new OperationCanceledException(publication.CancellationToken));
                }
                else
                {
                    try
                    {
                        result = await _transport.TryWriteFrameAsync(publication.Lease, publication.CancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        result = new(TerminalWriteStatus.Failed, exception);
                    }
                }

                if (result.Status == TerminalWriteStatus.Written && publication.AcceptedState is { } state)
                    _state = state;
                else if (result.Status == TerminalWriteStatus.Failed)
                    _state = _state with { Certainty = TerminalCertainty.Uncertain };
                publication.Lease.Dispose();
                publication.Completion.TrySetResult(result);
            }
        }
        finally
        {
            Volatile.Write(ref _draining, 0);
            if (!_mailbox.IsEmpty) DrainMailbox();
        }
    }

    public async ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _waiting, 1, 0) != 0)
            throw new InvalidOperationException("Only one terminal writability wait may be active.");
        try { await _transport.WaitUntilWritableAsync(cancellationToken).ConfigureAwait(false); }
        finally { Volatile.Write(ref _waiting, 0); }
    }

    private sealed record Publication(
        TerminalFrameLease Lease,
        TerminalPresentationState? AcceptedState,
        CancellationToken CancellationToken)
    {
        public TaskCompletionSource<TerminalWriteResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
