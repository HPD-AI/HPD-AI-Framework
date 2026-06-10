#nullable enable

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using HPD.Buffers;

namespace HPD.Media.Transport;

/// <summary>
/// Describes coarse datagram path state.
/// </summary>
public enum PathState
{
    Created = 0,
    Connecting = 1,
    Ready = 2,
    Degraded = 3,
    Failed = 4,
    Closed = 5
}

/// <summary>
/// Gives a best-effort classification for received datagrams.
/// </summary>
public enum DatagramProtocolHint
{
    Unknown = 0,
    Stun = 1,
    Dtls = 2,
    SrtpOrSrtcp = 3
}

/// <summary>
/// Represents a complete datagram received from a selected path.
/// </summary>
public readonly struct Datagram
{
    /// <summary>Gets the complete datagram payload.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Gets the local endpoint that received the datagram.</summary>
    public required IPEndPoint LocalEndPoint { get; init; }

    /// <summary>Gets the remote endpoint that sent the datagram.</summary>
    public required IPEndPoint RemoteEndPoint { get; init; }

    /// <summary>Gets the receive timestamp.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Gets a best-effort protocol hint.</summary>
    public DatagramProtocolHint Hint { get; init; }
}

/// <summary>
/// Transfers ownership of a retained datagram backed by leased memory.
/// </summary>
public readonly struct OwnedDatagram : IDisposable
{
    /// <summary>Gets the retained datagram.</summary>
    public required Datagram Datagram { get; init; }

    /// <summary>Gets the lease that owns the memory referenced by the datagram.</summary>
    public required IByteBufferLease Lease { get; init; }

    /// <summary>Releases the owned datagram memory.</summary>
    public void Dispose() => Lease.Dispose();
}

/// <summary>
/// Represents a stack-only datagram view over caller-owned bytes.
/// </summary>
public readonly ref struct DatagramView
{
    /// <summary>Initializes a new instance of the <see cref="DatagramView"/> struct.</summary>
    public DatagramView(
        ReadOnlySpan<byte> payload,
        IPEndPoint localEndPoint,
        IPEndPoint remoteEndPoint,
        DateTimeOffset receivedAt,
        DatagramProtocolHint hint = DatagramProtocolHint.Unknown)
    {
        Payload = payload;
        LocalEndPoint = localEndPoint;
        RemoteEndPoint = remoteEndPoint;
        ReceivedAt = receivedAt;
        Hint = hint;
    }

    /// <summary>Gets the complete datagram payload.</summary>
    public ReadOnlySpan<byte> Payload { get; }

    /// <summary>Gets the local endpoint that received the datagram.</summary>
    public IPEndPoint LocalEndPoint { get; }

    /// <summary>Gets the remote endpoint that sent the datagram.</summary>
    public IPEndPoint RemoteEndPoint { get; }

    /// <summary>Gets the receive timestamp.</summary>
    public DateTimeOffset ReceivedAt { get; }

    /// <summary>Gets a best-effort protocol hint.</summary>
    public DatagramProtocolHint Hint { get; }
}

/// <summary>
/// Represents the result of a caller-buffer datagram receive operation.
/// </summary>
public readonly struct DatagramReceiveResult
{
    /// <summary>Gets a value indicating whether a datagram was received.</summary>
    public required bool HasDatagram { get; init; }

    /// <summary>Gets the number of bytes written to the caller-provided buffer.</summary>
    public int BytesWritten { get; init; }

    /// <summary>Gets the local endpoint that received the datagram.</summary>
    public IPEndPoint? LocalEndPoint { get; init; }

    /// <summary>Gets the remote endpoint that sent the datagram.</summary>
    public IPEndPoint? RemoteEndPoint { get; init; }

    /// <summary>Gets the receive timestamp.</summary>
    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Gets a best-effort protocol hint.</summary>
    public DatagramProtocolHint Hint { get; init; }

    /// <summary>Gets a value indicating whether the path completed.</summary>
    public bool IsCompleted => !HasDatagram;
}

/// <summary>
/// Represents a datagram path state transition.
/// </summary>
public readonly struct PathStateChange
{
    /// <summary>Gets the new path state.</summary>
    public required PathState State { get; init; }

    /// <summary>Gets the transition timestamp.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets an optional transition reason.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Receives datagrams without requiring per-datagram collection allocation.
/// </summary>
public interface IDatagramSink
{
    /// <summary>Attempts to accept a received datagram.</summary>
    bool TryWrite(in Datagram datagram);
}

/// <summary>
/// Represents a selected, validated, liveness-monitored datagram path.
/// </summary>
public interface IDatagramPath : IAsyncDisposable
{
    /// <summary>Gets the selected local endpoint.</summary>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>Gets the selected remote endpoint.</summary>
    IPEndPoint RemoteEndPoint { get; }

    /// <summary>Gets the current path state.</summary>
    PathState State { get; }

    /// <summary>Reads the next path state change or null when state events complete.</summary>
    ValueTask<PathStateChange?> ReadStateChangeAsync(CancellationToken cancellationToken = default);

    /// <summary>Receives one non-control datagram into a caller-provided buffer.</summary>
    ValueTask<DatagramReceiveResult> ReceiveAsync(Memory<byte> destination, CancellationToken cancellationToken = default);

    /// <summary>Sends one complete datagram on the selected path.</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional ergonomic datagram receive facade.
/// </summary>
public interface IAsyncDatagramPath : IDatagramPath
{
    /// <summary>Receives complete non-control datagrams from the selected path.</summary>
    IAsyncEnumerable<Datagram> ReceiveDatagramsAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads path state changes.</summary>
    IAsyncEnumerable<PathStateChange> StateChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// UDP-backed datagram path for already-selected local and remote endpoints.
/// </summary>
public sealed class UdpDatagramPath : IDatagramPath
{
    private readonly Socket socket;
    private readonly CancellationTokenSource disposal = new();
    private readonly TransportAsyncEventQueue<PathStateChange> stateChanges = new();
    private readonly object gate = new();
    private readonly bool ownsSocket;
    private PathState state;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpDatagramPath"/> class.
    /// </summary>
    public UdpDatagramPath(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
        : this(CreateSocket(localEndPoint), remoteEndPoint)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpDatagramPath"/> class over an existing socket.
    /// </summary>
    public UdpDatagramPath(Socket socket, IPEndPoint remoteEndPoint)
        : this(socket, remoteEndPoint, ownsSocket: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpDatagramPath"/> class over an existing socket.
    /// </summary>
    public UdpDatagramPath(Socket socket, IPEndPoint remoteEndPoint, bool ownsSocket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (socket.SocketType != SocketType.Dgram || socket.ProtocolType != ProtocolType.Udp)
        {
            throw new ArgumentException("The socket must be a UDP datagram socket.", nameof(socket));
        }

        this.socket = socket;
        this.ownsSocket = ownsSocket;
        RemoteEndPoint = remoteEndPoint;
        LocalEndPoint = socket.LocalEndPoint as IPEndPoint
            ?? throw new ArgumentException("The socket must be bound to an IPEndPoint.", nameof(socket));
        if (LocalEndPoint.AddressFamily != remoteEndPoint.AddressFamily)
        {
            throw new ArgumentException("The remote endpoint address family must match the bound socket address family.", nameof(remoteEndPoint));
        }

        state = PathState.Ready;
        EnqueueStateChange(PathState.Ready, "UDP path ready.");
    }

    /// <inheritdoc />
    public IPEndPoint LocalEndPoint { get; }

    /// <inheritdoc />
    public IPEndPoint RemoteEndPoint { get; }

    /// <inheritdoc />
    public PathState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<PathStateChange?> ReadStateChangeAsync(CancellationToken cancellationToken = default)
    {
        return stateChanges.ReadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<DatagramReceiveResult> ReceiveAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (destination.IsEmpty)
        {
            throw new ArgumentException("The datagram receive destination must not be empty.", nameof(destination));
        }

        using CancellationTokenSource? linkedCancellation = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposal.Token)
            : null;
        CancellationToken receiveCancellationToken = linkedCancellation?.Token ?? disposal.Token;

        while (true)
        {
            EndPoint remote = new IPEndPoint(RemoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
            SocketReceiveFromResult result;
            try
            {
                result = await socket.ReceiveFromAsync(
                    destination,
                    SocketFlags.None,
                    remote,
                    receiveCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && IsDisposed)
            {
                return CompletedReceiveResult();
            }
            catch (ObjectDisposedException) when (IsDisposed)
            {
                return CompletedReceiveResult();
            }
            catch (SocketException) when (IsDisposed)
            {
                return CompletedReceiveResult();
            }

            var remoteEndPoint = (IPEndPoint)result.RemoteEndPoint;
            if (!EndPointsEqual(remoteEndPoint, RemoteEndPoint))
            {
                continue;
            }

            return new DatagramReceiveResult
            {
                HasDatagram = true,
                BytesWritten = result.ReceivedBytes,
                LocalEndPoint = LocalEndPoint,
                RemoteEndPoint = remoteEndPoint,
                ReceivedAt = DateTimeOffset.UtcNow,
                Hint = ClassifyProtocol(destination.Span[..result.ReceivedBytes])
            };
        }
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        int bytesSent = await socket.SendToAsync(payload, SocketFlags.None, RemoteEndPoint, cancellationToken).ConfigureAwait(false);
        if (bytesSent != payload.Length)
        {
            throw new IOException($"UDP datagram send completed with {bytesSent} bytes sent from {payload.Length} requested bytes.");
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
        }

        disposal.Cancel();
        if (ownsSocket)
        {
            socket.Dispose();
        }

        EnqueueStateChange(PathState.Closed, "UDP path disposed.");
        stateChanges.Complete();
        disposal.Dispose();
        return ValueTask.CompletedTask;
    }

    private static DatagramReceiveResult CompletedReceiveResult()
    {
        return new DatagramReceiveResult { HasDatagram = false };
    }

    private static Socket CreateSocket(IPEndPoint localEndPoint)
    {
        var socket = new Socket(localEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(localEndPoint);
        return socket;
    }

    private static DatagramProtocolHint ClassifyProtocol(ReadOnlySpan<byte> payload)
    {
        if (IsStunDatagram(payload))
        {
            return DatagramProtocolHint.Stun;
        }

        if (!payload.IsEmpty && payload[0] is >= 20 and <= 63)
        {
            return DatagramProtocolHint.Dtls;
        }

        if (!payload.IsEmpty && payload[0] is >= 128 and <= 191)
        {
            return DatagramProtocolHint.SrtpOrSrtcp;
        }

        return DatagramProtocolHint.Unknown;
    }

    private static bool IsStunDatagram(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 20 ||
            (payload[0] & 0xC0) != 0 ||
            payload[4] != 0x21 ||
            payload[5] != 0x12 ||
            payload[6] != 0xA4 ||
            payload[7] != 0x42)
        {
            return false;
        }

        int messageLength = (payload[2] << 8) | payload[3];
        return (messageLength & 0x03) == 0 &&
            messageLength == payload.Length - 20;
    }

    private static bool EndPointsEqual(IPEndPoint left, IPEndPoint right)
    {
        return left.Port == right.Port && left.Address.Equals(right.Address);
    }

    private void EnqueueStateChange(PathState newState, string reason)
    {
        lock (gate)
        {
            state = newState;
            stateChanges.Enqueue(new PathStateChange
            {
                State = newState,
                At = DateTimeOffset.UtcNow,
                Reason = reason
            });
        }
    }

    private void ThrowIfDisposed()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }

    private bool IsDisposed
    {
        get
        {
            lock (gate)
            {
                return disposed;
            }
        }
    }
}

internal sealed class TransportAsyncEventQueue<T>
    where T : struct
{
    private readonly Queue<T> events = new();
    private readonly Queue<AsyncEventWaiter> waiters = new();
    private readonly object gate = new();
    private bool completed;

    public void Enqueue(T value)
    {
        AsyncEventWaiter? waiter = null;
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            while (waiters.Count != 0)
            {
                waiter = waiters.Dequeue();
                if (waiter.TrySetResult(value))
                {
                    break;
                }

                waiter.Dispose();
                waiter = null;
            }

            if (waiter is null)
            {
                events.Enqueue(value);
                return;
            }
        }

        waiter.Dispose();
    }

    public ValueTask<T?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (events.Count != 0)
            {
                return new ValueTask<T?>((T?)events.Dequeue());
            }

            if (completed)
            {
                return new ValueTask<T?>((T?)null);
            }

            var waiter = new AsyncEventWaiter(cancellationToken);
            waiters.Enqueue(waiter);
            return new ValueTask<T?>(waiter.Task);
        }
    }

    public void Complete()
    {
        Queue<AsyncEventWaiter> drained;
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            drained = new Queue<AsyncEventWaiter>(waiters);
            waiters.Clear();
        }

        while (drained.Count != 0)
        {
            AsyncEventWaiter waiter = drained.Dequeue();
            _ = waiter.TrySetResult(null);
            waiter.Dispose();
        }
    }

    private sealed class AsyncEventWaiter : IDisposable
    {
        private readonly CancellationTokenRegistration cancellationRegistration;

        public AsyncEventWaiter(CancellationToken cancellationToken)
        {
            Source = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(
                    static state => ((AsyncEventWaiter)state!).Source.TrySetCanceled(),
                    this);
            }
        }

        public Task<T?> Task => Source.Task;

        private TaskCompletionSource<T?> Source { get; }

        public bool TrySetResult(T? value) => Source.TrySetResult(value);

        public void Dispose() => cancellationRegistration.Dispose();
    }
}

/// <summary>
/// Exposes a generic asynchronous transport control plane.
/// </summary>
public interface ITransportControlPlane<TEvent, TCommand>
{
    /// <summary>Reads control-plane events.</summary>
    IAsyncEnumerable<TEvent> EventsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a control-plane command.</summary>
    ValueTask SendAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Exposes generic offer, answer, and update negotiation.
/// </summary>
public interface ISessionNegotiator<TEvent, TOffer, TAnswer, TUpdate>
{
    /// <summary>Reads negotiation events.</summary>
    IAsyncEnumerable<TEvent> EventsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a local offer.</summary>
    ValueTask SendOfferAsync(TOffer offer, string negotiationId, CancellationToken cancellationToken = default);

    /// <summary>Sends a local answer.</summary>
    ValueTask SendAnswerAsync(TAnswer answer, string negotiationId, CancellationToken cancellationToken = default);

    /// <summary>Sends a negotiation update.</summary>
    ValueTask SendUpdateAsync(TUpdate update, string negotiationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Identifies the local DTLS role.
/// </summary>
public enum DtlsRole
{
    Client = 0,
    Server = 1
}

/// <summary>
/// Identifies an SRTP protection profile.
/// </summary>
public enum SrtpProtectionProfile
{
    Aes128CmHmacSha1_80 = 1,
    Aes128CmHmacSha1_32 = 2,
    AeadAes128Gcm = 3
}

/// <summary>
/// Carries a local certificate for a secure handshake.
/// </summary>
public sealed class LocalCertificate
{
    /// <summary>Gets the local certificate.</summary>
    public required X509Certificate2 Certificate { get; init; }
}

/// <summary>
/// Configures a secure handshake.
/// </summary>
public sealed class SecureHandshakeOptions
{
    /// <summary>Gets the preferred local DTLS role, or null when role is selected by the handshake provider.</summary>
    public DtlsRole? PreferredLocalRole { get; init; }

    /// <summary>Gets the acceptable SRTP protection profiles.</summary>
    public ReadOnlyMemory<SrtpProtectionProfile> SrtpProfiles { get; init; }
}

/// <summary>
/// Represents provider-neutral peer proof material from a secure handshake.
/// </summary>
public readonly struct PeerProofMaterial
{
    /// <summary>Gets the peer certificate in DER form when available.</summary>
    public required ReadOnlyMemory<byte> CertificateDer { get; init; }
}

/// <summary>
/// Identifies a certificate fingerprint algorithm.
/// </summary>
public enum CertificateFingerprintAlgorithm
{
    Unknown = 0,
    Sha256 = 1,
    Sha384 = 2,
    Sha512 = 3
}

/// <summary>
/// Represents the expected peer identity from signaling or configuration.
/// </summary>
public readonly struct ExpectedPeerIdentity
{
    /// <summary>Gets the expected certificate fingerprint algorithm.</summary>
    public required CertificateFingerprintAlgorithm FingerprintAlgorithm { get; init; }

    /// <summary>Gets the expected certificate fingerprint bytes.</summary>
    public required ReadOnlyMemory<byte> Fingerprint { get; init; }
}

/// <summary>
/// Represents the result of peer identity verification.
/// </summary>
public readonly struct PeerIdentityVerificationResult
{
    /// <summary>Gets a value indicating whether the identity matched.</summary>
    public required bool IsVerified { get; init; }

    /// <summary>Gets an optional verification failure reason.</summary>
    public string? FailureReason { get; init; }
}

/// <summary>
/// Performs a secure datagram handshake and exposes key export material.
/// </summary>
public interface ISecureHandshake
{
    /// <summary>Runs the secure handshake over a validated datagram path.</summary>
    ValueTask<SecureHandshakeResult> HandshakeAsync(
        IDatagramPath path,
        LocalCertificate localCertificate,
        SecureHandshakeOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a secure handshake.
/// </summary>
public readonly struct SecureHandshakeResult
{
    /// <summary>Gets the peer proof material.</summary>
    public required PeerProofMaterial PeerProof { get; init; }

    /// <summary>Gets the negotiated local DTLS role.</summary>
    public required DtlsRole LocalRole { get; init; }

    /// <summary>Gets the negotiated SRTP protection profile.</summary>
    public required SrtpProtectionProfile NegotiatedSrtpProfile { get; init; }

    /// <summary>Gets the handshake key exporter.</summary>
    public required IKeyExporter KeyExporter { get; init; }
}

/// <summary>
/// Exports keying material from a completed secure handshake.
/// </summary>
public interface IKeyExporter
{
    /// <summary>Exports keying material for the supplied label and context into caller-provided storage.</summary>
    bool TryExport(string label, ReadOnlySpan<byte> context, Span<byte> destination);
}

/// <summary>
/// Verifies out-of-band peer identity against handshake proof material.
/// </summary>
public interface IPeerIdentityVerifier
{
    /// <summary>Verifies the peer proof against the expected identity.</summary>
    PeerIdentityVerificationResult Verify(PeerProofMaterial proof, ExpectedPeerIdentity expected);
}

/// <summary>
/// Derives role-resolved SRTP protection material from a secure handshake.
/// </summary>
public interface ISrtpKeySchedule
{
    /// <summary>Derives SRTP protection material.</summary>
    SrtpProtectionMaterial Derive(SecureHandshakeResult handshake);
}

/// <summary>
/// Contains role-resolved SRTP and SRTCP master key material.
/// </summary>
public readonly struct SrtpProtectionMaterial
{
    /// <summary>Gets the SRTP protection profile.</summary>
    public required SrtpProtectionProfile Profile { get; init; }

    /// <summary>Gets the outbound master key.</summary>
    public required ReadOnlyMemory<byte> OutboundMasterKey { get; init; }

    /// <summary>Gets the outbound master salt.</summary>
    public required ReadOnlyMemory<byte> OutboundMasterSalt { get; init; }

    /// <summary>Gets the inbound master key.</summary>
    public required ReadOnlyMemory<byte> InboundMasterKey { get; init; }

    /// <summary>Gets the inbound master salt.</summary>
    public required ReadOnlyMemory<byte> InboundMasterSalt { get; init; }

    /// <summary>Gets the master key identifier, empty for WebRTC.</summary>
    public ReadOnlyMemory<byte> Mki { get; init; }
}

/// <summary>
/// Identifies packet protection direction.
/// </summary>
public enum PacketDirection
{
    Inbound = 0,
    Outbound = 1
}

/// <summary>
/// Identifies the protected packet family.
/// </summary>
public enum PacketProtectionPurpose
{
    Rtp = 0,
    Rtcp = 1
}

/// <summary>
/// Creates stateful packet protectors for a protection material set.
/// </summary>
public interface IPacketProtectorFactory
{
    /// <summary>Creates a protector for a purpose, direction, and SSRC.</summary>
    IPacketProtector Create(PacketProtectionPurpose purpose, PacketDirection direction, uint ssrc);
}

/// <summary>
/// Protects or unprotects RTP-family packets in place.
/// </summary>
public interface IPacketProtector
{
    /// <summary>Gets the number of bytes required for authentication tag, index, and MKI expansion.</summary>
    int MaximumExpansionBytes { get; }

    /// <summary>Protects a packet in place and reports the valid output length.</summary>
    PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength);

    /// <summary>Unprotects a packet in place and reports the valid output length.</summary>
    PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength);
}

/// <summary>
/// Classifies SRTP/SRTCP packet protection results without using exceptions for normal packet flow.
/// </summary>
public enum PacketProtectionStatus
{
    Success = 0,
    InvalidPacket = 1,
    DestinationTooSmall = 2,
    AuthenticationFailed = 3,
    ReplayRejected = 4,
    UnsupportedProfile = 5,
    WrongSsrc = 6
}
