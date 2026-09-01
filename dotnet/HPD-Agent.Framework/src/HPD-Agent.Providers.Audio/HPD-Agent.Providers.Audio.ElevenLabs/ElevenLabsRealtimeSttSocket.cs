using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

internal interface IElevenLabsRealtimeSttSocket : IAsyncDisposable
{
    bool IsOpen { get; }

    ValueTask ConnectAsync(Uri uri, string apiKey, CancellationToken cancellationToken);

    ValueTask SendTextAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    ValueTask<ElevenLabsRealtimeSttSocketMessage> ReceiveAsync(CancellationToken cancellationToken);

    ValueTask CloseAsync(CancellationToken cancellationToken);
}

internal readonly record struct ElevenLabsRealtimeSttSocketMessage(
    bool IsClose,
    ReadOnlyMemory<byte> Payload,
    bool CapacityExceeded = false,
    string? EvidenceSha256 = null);

internal sealed class ClientWebSocketRealtimeSttSocket : IElevenLabsRealtimeSttSocket
{
    private const int MaximumMessageBytes = 256 * 1024;
    private readonly ClientWebSocket _socket = new();

    public bool IsOpen => _socket.State == WebSocketState.Open;

    public async ValueTask ConnectAsync(Uri uri, string apiKey, CancellationToken cancellationToken)
    {
        _socket.Options.SetRequestHeader("xi-api-key", apiKey);
        await _socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendTextAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ElevenLabsRealtimeSttSocketMessage> ReceiveAsync(CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var message = new MemoryStream();
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var capacityExceeded = false;
            while (true)
            {
                var result = await _socket.ReceiveAsync(rented.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return new ElevenLabsRealtimeSttSocketMessage(true, ReadOnlyMemory<byte>.Empty);
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new InvalidDataException("ElevenLabs realtime STT returned a non-text message.");
                digest.AppendData(rented, 0, result.Count);
                if (message.Length + result.Count > MaximumMessageBytes)
                    capacityExceeded = true;

                if (!capacityExceeded)
                    message.Write(rented, 0, result.Count);
                if (result.EndOfMessage)
                    return capacityExceeded
                        ? new ElevenLabsRealtimeSttSocketMessage(
                            false,
                            ReadOnlyMemory<byte>.Empty,
                            true,
                            Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant())
                        : new ElevenLabsRealtimeSttSocketMessage(false, message.ToArray());
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "completed",
                cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
