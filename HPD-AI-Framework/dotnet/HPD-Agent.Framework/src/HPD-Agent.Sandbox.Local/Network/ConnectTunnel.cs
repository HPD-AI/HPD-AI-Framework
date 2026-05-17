using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace HPD.Sandbox.Local.Network;

internal static class ConnectTunnel
{
    private const int MaxHeaderBytes = 16 * 1024;
    private static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);

    public static async Task<Stream> OpenAsync(
        Func<CancellationToken, Task<Stream>> dialProxy,
        string destinationHost,
        int destinationPort,
        string? proxyAuthorization,
        CancellationToken cancellationToken,
        TimeSpan? handshakeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(dialProxy);
        ValidateDestination(destinationHost, destinationPort);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(handshakeTimeout ?? DefaultHandshakeTimeout);
        var timeoutToken = timeoutCts.Token;

        var proxyStream = await dialProxy(timeoutToken);
        try
        {
            var hostPort = FormatHostPort(destinationHost, destinationPort);
            var request = new StringBuilder()
                .Append("CONNECT ")
                .Append(hostPort)
                .Append(" HTTP/1.1\r\nHost: ")
                .Append(hostPort)
                .Append("\r\n");

            if (!string.IsNullOrWhiteSpace(proxyAuthorization))
            {
                request.Append("Proxy-Authorization: ")
                    .Append(proxyAuthorization)
                    .Append("\r\n");
            }

            request.Append("\r\n");

            var requestBytes = Encoding.ASCII.GetBytes(request.ToString());
            await proxyStream.WriteAsync(requestBytes, timeoutToken);
            await proxyStream.FlushAsync(timeoutToken);

            var (headers, extraBytes) = await ReadResponseHeadersAsync(proxyStream, timeoutToken);
            var statusLine = headers.Split("\r\n", 2, StringSplitOptions.None)[0];
            if (!IsSuccessStatus(statusLine))
                throw new InvalidOperationException($"Parent proxy CONNECT failed: {statusLine}");

            return extraBytes.Length == 0
                ? proxyStream
                : new PrefixReadStream(proxyStream, extraBytes);
        }
        catch
        {
            await proxyStream.DisposeAsync();
            throw;
        }
    }

    public static string? BuildProxyAuthorization(Uri proxyUri)
    {
        if (string.IsNullOrEmpty(proxyUri.UserInfo))
            return null;

        var credentials = Uri.UnescapeDataString(proxyUri.UserInfo);
        return $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials))}";
    }

    private static async Task<(string Headers, byte[] ExtraBytes)> ReadResponseHeadersAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(MaxHeaderBytes);
        var length = 0;
        try
        {
            while (length < MaxHeaderBytes)
            {
                var read = await stream.ReadAsync(rented.AsMemory(length, 1), cancellationToken);
                if (read == 0)
                    throw new IOException("Parent proxy closed the connection before CONNECT response headers completed.");

                length += read;
                var headerEnd = IndexOfHeaderEnd(rented.AsSpan(0, length));
                if (headerEnd >= 0)
                {
                    var headerLength = headerEnd + 4;
                    var headers = Encoding.ASCII.GetString(rented, 0, headerLength);
                    var extraLength = length - headerLength;
                    var extraBytes = extraLength == 0
                        ? []
                        : rented.AsSpan(headerLength, extraLength).ToArray();
                    return (headers, extraBytes);
                }
            }

            throw new InvalidOperationException("Parent proxy CONNECT response headers exceeded the size limit.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int IndexOfHeaderEnd(ReadOnlySpan<byte> bytes)
    {
        for (var i = 3; i < bytes.Length; i++)
        {
            if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
                return i - 3;
        }

        return -1;
    }

    private static bool IsSuccessStatus(string statusLine)
    {
        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
            int.TryParse(parts[1], out var status) &&
            status is >= 200 and <= 299;
    }

    private static void ValidateDestination(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) ||
            host.Any(c => c is '\r' or '\n' or '\0') ||
            port is <= 0 or > 65535)
        {
            throw new ArgumentException("CONNECT destination host or port is invalid.");
        }
    }

    private static string FormatHostPort(string host, int port) =>
        host.Contains(':', StringComparison.Ordinal) && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]:{port}"
            : $"{host}:{port}";

    private sealed class PrefixReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly byte[] _prefix;
        private int _prefixOffset;

        public PrefixReadStream(Stream inner, byte[] prefix)
        {
            _inner = inner;
            _prefix = prefix;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_prefixOffset < _prefix.Length)
            {
                var copied = Math.Min(count, _prefix.Length - _prefixOffset);
                Array.Copy(_prefix, _prefixOffset, buffer, offset, copied);
                _prefixOffset += copied;
                return copied;
            }

            return _inner.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_prefixOffset < _prefix.Length)
            {
                var copied = Math.Min(buffer.Length, _prefix.Length - _prefixOffset);
                _prefix.AsMemory(_prefixOffset, copied).CopyTo(buffer);
                _prefixOffset += copied;
                return copied;
            }

            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
