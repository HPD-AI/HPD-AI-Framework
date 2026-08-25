using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace HPD.Agent.Providers.Audio.OpenAI;

internal interface IOpenAIRealtimeSttSocket : IAsyncDisposable
{
    bool IsOpen { get; }
    ValueTask ConnectAsync(Uri uri, string apiKey, IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken);
    ValueTask SendTextAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    ValueTask<OpenAIRealtimeSttSocketMessage> ReceiveAsync(CancellationToken cancellationToken);
    ValueTask CloseAsync(CancellationToken cancellationToken);
}

internal readonly record struct OpenAIRealtimeSttSocketMessage(
    bool IsClose, ReadOnlyMemory<byte> Payload, bool CapacityExceeded = false,
    string? EvidenceSha256 = null);

internal sealed class ClientWebSocketOpenAIRealtimeSttSocket : IOpenAIRealtimeSttSocket
{
    private const int MaximumMessageBytes = 256 * 1024;
    private readonly ClientWebSocket _socket = new();

    public bool IsOpen => _socket.State == WebSocketState.Open;

    public async ValueTask ConnectAsync(Uri uri, string apiKey,
        IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        _socket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        if (headers is not null)
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                    continue;
                _socket.Options.SetRequestHeader(header.Key, header.Value);
            }
        await _socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendTextAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

    public async ValueTask<OpenAIRealtimeSttSocketMessage> ReceiveAsync(CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var message = new MemoryStream();
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var exceeded = false;
            while (true)
            {
                var result = await _socket.ReceiveAsync(rented.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return new(true, ReadOnlyMemory<byte>.Empty);
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new InvalidDataException("OpenAI realtime transcription returned a non-text message.");
                digest.AppendData(rented, 0, result.Count);
                if (message.Length + result.Count > MaximumMessageBytes) exceeded = true;
                if (!exceeded) message.Write(rented, 0, result.Count);
                if (!result.EndOfMessage) continue;
                return exceeded
                    ? new(false, ReadOnlyMemory<byte>.Empty, true,
                        Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant())
                    : new(false, message.ToArray());
            }
        }
        finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    public async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "completed", cancellationToken)
                .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
