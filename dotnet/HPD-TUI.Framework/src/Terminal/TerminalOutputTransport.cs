using System.Buffers;

namespace HPD.TUI.Terminal;

/// <summary>Describes the outcome of one terminal payload publication attempt.</summary>
public enum TerminalWriteStatus
{
    /// <summary>The complete payload was accepted.</summary>
    Written,

    /// <summary>No payload bytes were accepted because the transport is not currently writable.</summary>
    Backpressured,

    /// <summary>The write failed and an unknown payload prefix may have been accepted.</summary>
    Failed
}

/// <summary>Reports the result of publishing one terminal payload.</summary>
/// <param name="Status">The publication status.</param>
/// <param name="Error">The failure when <paramref name="Status"/> is <see cref="TerminalWriteStatus.Failed"/>.</param>
public readonly record struct TerminalWriteResult(TerminalWriteStatus Status, Exception? Error = null)
{
    /// <summary>Gets a successful publication result.</summary>
    public static TerminalWriteResult Written { get; } = new(TerminalWriteStatus.Written);

    /// <summary>Gets a zero-byte backpressure result.</summary>
    public static TerminalWriteResult Backpressured { get; } = new(TerminalWriteStatus.Backpressured);
}

/// <summary>Owns immutable encoded terminal memory for the duration of one publication attempt.</summary>
public sealed class TerminalFrameLease : IDisposable
{
    private char[]? _buffer;

    internal TerminalFrameLease(ReadOnlySpan<char> payload)
    {
        _buffer = ArrayPool<char>.Shared.Rent(Math.Max(1, payload.Length));
        payload.CopyTo(_buffer);
        Length = payload.Length;
    }

    /// <summary>Gets the immutable encoded payload.</summary>
    public ReadOnlyMemory<char> Payload
        => _buffer is { } buffer
            ? buffer.AsMemory(0, Length)
            : throw new ObjectDisposedException(nameof(TerminalFrameLease));

    /// <summary>Gets the encoded character count.</summary>
    public int Length { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
            ArrayPool<char>.Shared.Return(buffer);
    }
}

/// <summary>Publishes serialized terminal payloads with explicit ownership and backpressure.</summary>
public interface ITerminalOutputTransport
{
    /// <summary>Attempts to publish one complete immutable payload.</summary>
    /// <param name="frame">The payload lease, owned by the caller until this operation completes.</param>
    /// <param name="cancellationToken">Cancels the publication attempt.</param>
    /// <returns>The publication result.</returns>
    ValueTask<TerminalWriteResult> TryWriteFrameAsync(
        TerminalFrameLease frame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until another write may succeed. This operation is level-triggered and completes immediately when the
    /// transport is already writable.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default);
}

internal sealed class SynchronousTerminalOutputTransport(ITerminalDisplay terminal) : ITerminalOutputTransport
{
    public ValueTask<TerminalWriteResult> TryWriteFrameAsync(
        TerminalFrameLease frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            terminal.Write(frame.Payload.Span);
            terminal.Flush();
            return ValueTask.FromResult(TerminalWriteResult.Written);
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(new TerminalWriteResult(TerminalWriteStatus.Failed, exception));
        }
    }

    public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
