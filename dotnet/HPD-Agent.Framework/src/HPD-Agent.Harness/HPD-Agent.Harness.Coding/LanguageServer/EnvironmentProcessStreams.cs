namespace HPDOS.ToolHarnesses.Middleware;

using System.Threading.Channels;
using HPD.Environment.Contracts;

/// <summary>Adapts Environment stdin to the Stream surface used by LSP framing.</summary>
internal sealed class EnvironmentProcessInputStream(
    IProcessInvocationHandle process)
    : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public override int Read(
        byte[] buffer,
        int offset,
        int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count) =>
        process.WriteStdinAsync(
            buffer.AsMemory(offset, count))
            .AsTask()
            .GetAwaiter()
            .GetResult();

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        await process.WriteStdinAsync(
            buffer,
            cancellationToken).ConfigureAwait(false);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                process.CloseStdinAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
            }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Adapts Environment stdout chunks to a cancellable read-only Stream while
/// discarding stderr from the LSP byte stream.
/// </summary>
internal sealed class EnvironmentProcessOutputStream
    : Stream
{
    private readonly IProcessInvocationHandle _process;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Channel<byte[]> _chunks =
        Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
    private readonly Task _pump;
    private byte[] _current = [];
    private int _offset;

    public EnvironmentProcessOutputStream(
        IProcessInvocationHandle process)
    {
        _process = process;
        _pump = PumpAsync();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count) =>
        ReadAsync(
            buffer.AsMemory(offset, count),
            CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        while (_offset >= _current.Length)
        {
            if (!await _chunks.Reader.WaitToReadAsync(
                    cancellationToken).ConfigureAwait(false) ||
                !_chunks.Reader.TryRead(out _current!))
            {
                return 0;
            }
            _offset = 0;
        }
        int count = Math.Min(
            buffer.Length,
            _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposeCts.Cancel();
            _chunks.Writer.TryComplete();
            try
            {
                _pump.GetAwaiter().GetResult();
            }
            catch
            {
            }
            _disposeCts.Dispose();
        }
        base.Dispose(disposing);
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (ProcessOutputChunk chunk in
                           _process.ReadOutputAsync(
                               _disposeCts.Token))
            {
                if (chunk.Stream == ProcessOutputStream.Stdout &&
                    !chunk.Bytes.IsEmpty)
                {
                    await _chunks.Writer.WriteAsync(
                        chunk.Bytes.ToArray(),
                        _disposeCts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
            when (_disposeCts.IsCancellationRequested)
        {
        }
        finally
        {
            _chunks.Writer.TryComplete();
        }
    }
}
