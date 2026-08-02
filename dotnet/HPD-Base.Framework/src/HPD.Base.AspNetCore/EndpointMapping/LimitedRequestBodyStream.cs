using System.Text.Json;

namespace HPD.Base.AspNetCore;

internal sealed class LimitedRequestBodyStream(Stream inner, long maximumBytes) : Stream
{
    private long _bytesRead;

    /// <summary>Gets the can read.</summary>
    public override bool CanRead => inner.CanRead;
    /// <summary>Gets the can seek.</summary>
    public override bool CanSeek => false;
    /// <summary>Gets the can write.</summary>
    public override bool CanWrite => false;
    /// <summary>Gets the length.</summary>
    public override long Length => throw new NotSupportedException();
    /// <summary>Gets or sets the position.</summary>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Executes the read operation.</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Count(read);
        return read;
    }

    /// <summary>Executes the read operation.</summary>
    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Count(read);
        return read;
    }

    /// <summary>Executes the read async operation.</summary>
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Count(read);
        return read;
    }

    /// <summary>Executes the read async operation.</summary>
    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
        Count(read);
        return read;
    }

    /// <summary>Executes the flush operation.</summary>
    public override void Flush() => throw new NotSupportedException();
    /// <summary>Executes the seek operation.</summary>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <summary>Executes the set length operation.</summary>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <summary>Executes the write operation.</summary>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>Executes the dispose operation.</summary>
    protected override void Dispose(bool disposing)
    {
        // The HTTP request owns the inner stream.
        base.Dispose(disposing);
    }

    /// <summary>Executes the dispose async operation.</summary>
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Count(int read)
    {
        _bytesRead += read;
        if (_bytesRead > maximumBytes)
            throw new JsonException("Request body exceeds the configured maximum length.");
    }
}
