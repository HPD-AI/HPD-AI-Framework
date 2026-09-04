namespace HPD.TUI.Terminal;

/// <summary>Serializes every payload sent through one terminal transport.</summary>
internal sealed class TerminalPublicationCoordinator
{
    private readonly ITerminalOutputTransport _transport;
    private readonly object _publicationGate = new();
    private ulong _nextPublicationTicket;
    private ulong _servingPublicationTicket;
    private int _waiting;

    public TerminalPublicationCoordinator(ITerminalOutputTransport transport)
        => _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public TerminalWriteResult TryPublish(ReadOnlySpan<char> payload, CancellationToken cancellationToken = default)
    {
        var cancelledWhileQueued = false;
        ulong ticket;
        lock (_publicationGate)
        {
            ticket = _nextPublicationTicket++;
            while (ticket != _servingPublicationTicket)
            {
                Monitor.Wait(_publicationGate, 50);
                cancelledWhileQueued |= cancellationToken.IsCancellationRequested;
            }
        }
        try
        {
            if (cancelledWhileQueued || cancellationToken.IsCancellationRequested)
                return new TerminalWriteResult(TerminalWriteStatus.Failed, new OperationCanceledException(cancellationToken));
            if (_transport is ISynchronousTerminalOutputTransport synchronous)
                return synchronous.TryWrite(payload, cancellationToken);
            using var lease = new TerminalFrameLease(payload);
            return _transport.TryWriteFrameAsync(lease, cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException exception)
        {
            return new TerminalWriteResult(TerminalWriteStatus.Failed, exception);
        }
        catch (Exception exception)
        {
            return new TerminalWriteResult(TerminalWriteStatus.Failed, exception);
        }
        finally
        {
            lock (_publicationGate)
            {
                _servingPublicationTicket++;
                Monitor.PulseAll(_publicationGate);
            }
        }
    }

    public async ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _waiting, 1, 0) != 0)
            throw new InvalidOperationException("Only one terminal writability wait may be active.");
        try
        {
            await _transport.WaitUntilWritableAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _waiting, 0);
        }
    }
}
