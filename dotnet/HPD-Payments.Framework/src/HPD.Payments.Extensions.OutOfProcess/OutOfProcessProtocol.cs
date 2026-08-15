using System.Buffers.Binary;
using System.Security.Cryptography;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Extensions.OutOfProcess;

/// <summary>Names an authenticated out-of-process frame kind.</summary>
public enum OutOfProcessFrameKind
{
    /// <summary>Invalid default kind.</summary>
    None = 0,
    /// <summary>Invocation request.</summary>
    Request = 1,
    /// <summary>Invocation response.</summary>
    Response = 2,
}

/// <summary>Names transport knowledge after one out-of-process exchange attempt.</summary>
public enum OutOfProcessTransportState
{
    /// <summary>No write began; retry may be considered under current policy.</summary>
    DefiniteNotSent = 0,
    /// <summary>The request may have reached the host; synchronization is required.</summary>
    PossibleDispatch,
    /// <summary>An authenticated response frame was received.</summary>
    ResponseReceived,
}

/// <summary>Owns one bounded authenticated protocol frame.</summary>
public sealed class OutOfProcessFrame
{
    /// <summary>Maximum admitted payload bytes.</summary>
    public const int MaximumPayloadBytes = 1_048_576;
    private readonly byte[] _payload;
    private readonly byte[] _authenticationTag;

    /// <summary>Gets the protocol version.</summary>
    public ContractVersion ProtocolVersion { get; }
    /// <summary>Gets the frame kind.</summary>
    public OutOfProcessFrameKind Kind { get; }
    /// <summary>Gets the stable request identity shared by request and response.</summary>
    public SemanticId RequestId { get; }
    /// <summary>Gets the monotone connection/session nonce.</summary>
    public ulong Nonce { get; }
    /// <summary>Gets the copied payload length.</summary>
    public int PayloadLength => _payload.Length;

    internal OutOfProcessFrame(ContractVersion protocolVersion, OutOfProcessFrameKind kind, SemanticId requestId,
        ulong nonce, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> authenticationTag)
    {
        if (!protocolVersion.IsValid || kind == OutOfProcessFrameKind.None || !Enum.IsDefined(kind) ||
            !requestId.IsValid || nonce == 0 || payload.Length > MaximumPayloadBytes ||
            authenticationTag.Length != OutOfProcessProtocol.AuthenticationTagBytes)
            throw new ArgumentException("Out-of-process frame requires version, kind, request, nonce, bounded payload, and authentication tag.");
        ProtocolVersion = protocolVersion; Kind = kind; RequestId = requestId; Nonce = nonce;
        _payload = payload.ToArray(); _authenticationTag = authenticationTag.ToArray();
    }

    /// <summary>Returns a new payload copy.</summary>
    public byte[] CopyPayload() => _payload.ToArray();
    internal ReadOnlySpan<byte> PayloadSpan => _payload;
    internal ReadOnlySpan<byte> AuthenticationTagSpan => _authenticationTag;
}

/// <summary>Creates and validates authenticated, versioned out-of-process frames.</summary>
public static class OutOfProcessProtocol
{
    private const int FixedWireBytes = 4 + 1 + 2 + 2 + 1 + 8 + 2 + 4 + AuthenticationTagBytes;
    private static ReadOnlySpan<byte> WireMagic => "HPDP"u8;
    /// <summary>Size of the HMAC-SHA256 authentication tag.</summary>
    public const int AuthenticationTagBytes = 32;
    /// <summary>Maximum accepted authentication key bytes.</summary>
    public const int MaximumKeyBytes = 1024;

    /// <summary>Creates an authenticated frame without retaining the key.</summary>
    public static OutOfProcessFrame Create(ContractVersion version, OutOfProcessFrameKind kind, SemanticId requestId,
        ulong nonce, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        if (payload.Length > OutOfProcessFrame.MaximumPayloadBytes) throw new ArgumentException("Payload exceeds protocol bound.", nameof(payload));
        var tag = ComputeTag(version, kind, requestId, nonce, payload, key);
        return new(version, kind, requestId, nonce, payload, tag);
    }

    /// <summary>Authenticates the exact frame fields in fixed order using constant-time tag comparison.</summary>
    public static bool Authenticate(OutOfProcessFrame frame, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateKey(key);
        var expected = ComputeTag(frame.ProtocolVersion, frame.Kind, frame.RequestId, frame.Nonce, frame.PayloadSpan, key);
        return CryptographicOperations.FixedTimeEquals(expected, frame.AuthenticationTagSpan);
    }

    /// <summary>Encodes one frame into the strict version-one wire representation.</summary>
    public static byte[] Encode(OutOfProcessFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var id = frame.RequestId.GetCanonicalBytes();
        var bytes = new byte[checked(FixedWireBytes + id.Length + frame.PayloadLength)];
        WireMagic.CopyTo(bytes);
        bytes[4] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(5, 2), checked((ushort)frame.ProtocolVersion.Major));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(7, 2), checked((ushort)frame.ProtocolVersion.Minor));
        bytes[9] = (byte)frame.Kind;
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(10, 8), frame.Nonce);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(18, 2), checked((ushort)id.Length));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), frame.PayloadLength);
        id.CopyTo(bytes, 24);
        frame.PayloadSpan.CopyTo(bytes.AsSpan(24 + id.Length));
        frame.AuthenticationTagSpan.CopyTo(bytes.AsSpan(24 + id.Length + frame.PayloadLength));
        return bytes;
    }

    /// <summary>Decodes one exact frame and rejects unknown schemas, malformed lengths, invalid identities, and trailing bytes.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out OutOfProcessFrame? frame)
    {
        frame = null;
        if (bytes.Length < FixedWireBytes || !bytes[..4].SequenceEqual(WireMagic) || bytes[4] != 1)
            return false;
        var major = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(5, 2));
        var minor = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(7, 2));
        var kind = (OutOfProcessFrameKind)bytes[9];
        var nonce = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(10, 8));
        var identityLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(18, 2));
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        if (identityLength is 0 or > SemanticId.MaximumCanonicalBytes || payloadLength is < 0 or > OutOfProcessFrame.MaximumPayloadBytes ||
            bytes.Length != FixedWireBytes + identityLength + payloadLength ||
            !SemanticId.TryParseCanonical(bytes.Slice(24, identityLength), out var requestId))
            return false;
        try
        {
            frame = new(ContractVersion.Create(major, minor), kind, requestId, nonce,
                bytes.Slice(24 + identityLength, payloadLength), bytes[^AuthenticationTagBytes..]);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static byte[] ComputeTag(ContractVersion version, OutOfProcessFrameKind kind, SemanticId requestId,
        ulong nonce, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> key)
    {
        var id = requestId.GetCanonicalBytes();
        var header = new byte[4 + 1 + 8 + 4 + id.Length + 4 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0, 2), checked((ushort)version.Major));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2, 2), checked((ushort)version.Minor));
        header[4] = (byte)kind;
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(5, 8), nonce);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(13, 4), id.Length);
        id.CopyTo(header, 17);
        var payloadOffset = 17 + id.Length;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(payloadOffset, 4), payload.Length);
        payload.CopyTo(header.AsSpan(payloadOffset + 4));
        return HMACSHA256.HashData(key, header);
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length is < 32 or > MaximumKeyBytes) throw new ArgumentException("Protocol key must contain 32-1024 bytes.", nameof(key));
    }
}

/// <summary>Represents the transport's exact knowledge after one exchange.</summary>
public sealed record OutOfProcessTransportResult(OutOfProcessTransportState State, OutOfProcessFrame? Response, string Code);

/// <summary>Defines a bounded transport seam; implementations own process, pipe, socket, and resource mechanics.</summary>
public interface IOutOfProcessTransport
{
    /// <summary>Attempts one request exchange without claiming effect non-occurrence on host failure.</summary>
    ValueTask<OutOfProcessTransportResult> ExchangeAsync(OutOfProcessFrame request, CancellationToken cancellationToken);
}

/// <summary>Validates request/response protocol bindings around an injected out-of-process transport.</summary>
public sealed class OutOfProcessClient
{
    private readonly IOutOfProcessTransport _transport;
    private readonly byte[] _key;
    private readonly ContractVersion _version;

    /// <summary>Copies the protocol key and freezes the expected version.</summary>
    public OutOfProcessClient(IOutOfProcessTransport transport, ContractVersion version, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (!version.IsValid || key.Length is < 32 or > OutOfProcessProtocol.MaximumKeyBytes)
            throw new ArgumentException("Client requires transport, version, and bounded protocol key.");
        _transport = transport; _version = version; _key = key.ToArray();
    }

    /// <summary>Executes one exchange and rejects unauthenticated, skewed, or mismatched responses.</summary>
    public async ValueTask<OutOfProcessTransportResult> InvokeAsync(SemanticId requestId, ulong nonce,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var request = OutOfProcessProtocol.Create(_version, OutOfProcessFrameKind.Request, requestId, nonce, payload.Span, _key);
        var result = await _transport.ExchangeAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.State != OutOfProcessTransportState.ResponseReceived)
            return result;
        if (result.Response is not { } response || response.Kind != OutOfProcessFrameKind.Response ||
            response.RequestId != requestId || response.Nonce != nonce)
            return new(OutOfProcessTransportState.PossibleDispatch, null, "response-binding-invalid");
        if (response.ProtocolVersion != _version)
            return new(OutOfProcessTransportState.PossibleDispatch, null, "protocol-skew");
        if (!OutOfProcessProtocol.Authenticate(response, _key))
            return new(OutOfProcessTransportState.PossibleDispatch, null, "response-authentication-failed");
        return result;
    }
}
