namespace HPD.TUI.Terminal;

/// <summary>Serializes every payload sent through one terminal transport.</summary>
internal sealed class TerminalPublicationCoordinator
{
    private readonly ITerminalOutputTransport _transport;
    private int _publishing;
    private int _waiting;

    public TerminalPublicationCoordinator(ITerminalOutputTransport transport)
        => _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public TerminalWriteResult TryPublish(ReadOnlySpan<char> payload, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _publishing, 1, 0) != 0)
            throw new InvalidOperationException("Only one terminal payload publication may be active.");
        try
        {
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
            Volatile.Write(ref _publishing, 0);
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
