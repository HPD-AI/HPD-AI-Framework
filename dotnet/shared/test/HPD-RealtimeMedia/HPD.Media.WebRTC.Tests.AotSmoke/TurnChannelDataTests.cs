#nullable enable

using System.Net;
using System.Net.Sockets;
using HPD.Media.Transport;
using HPD.Media.WebRTC;

namespace HPD.Media.WebRTC.Tests.AotSmoke;

public sealed class TurnChannelDataTests
{
    [Fact]
    public void ChannelData_RoundTripsPayloadAndPadding()
    {
        ReadOnlySpan<byte> payload = [0x80, 0x60, 0x00, 0x01, 0x11];
        Span<byte> packet = stackalloc byte[32];

        TurnChannelDataStatus writeStatus = TurnChannelDataMessage.TryWrite(
            0x4001,
            payload,
            packet,
            out int bytesWritten);
        TurnChannelDataStatus parseStatus = TurnChannelDataMessage.TryParse(
            packet[..bytesWritten],
            out TurnChannelDataView view);

        Assert.Equal(TurnChannelDataStatus.Success, writeStatus);
        Assert.Equal(12, bytesWritten);
        Assert.Equal(TurnChannelDataStatus.Success, parseStatus);
        Assert.Equal(0x4001, view.ChannelNumber);
        Assert.Equal(bytesWritten, view.EncodedLength);
        Assert.True(view.Payload.SequenceEqual(payload));
        Assert.Equal(0, packet[TurnChannelDataMessage.HeaderLength + payload.Length]);
        Assert.Equal(0, packet[TurnChannelDataMessage.HeaderLength + payload.Length + 1]);
        Assert.Equal(0, packet[TurnChannelDataMessage.HeaderLength + payload.Length + 2]);
    }

    [Fact]
    public void TryWrite_ReturnsDestinationTooSmall()
    {
        ReadOnlySpan<byte> payload = [1, 2, 3, 4];
        Span<byte> destination = stackalloc byte[7];

        TurnChannelDataStatus status = TurnChannelDataMessage.TryWrite(
            0x4000,
            payload,
            destination,
            out int bytesWritten);

        Assert.Equal(TurnChannelDataStatus.DestinationTooSmall, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryParse_RejectsInvalidChannelNumber()
    {
        Span<byte> packet = stackalloc byte[TurnChannelDataMessage.HeaderLength];
        packet[0] = 0x30;
        packet[1] = 0x00;

        TurnChannelDataStatus status = TurnChannelDataMessage.TryParse(packet, out _);

        Assert.Equal(TurnChannelDataStatus.InvalidChannelNumber, status);
    }

    [Fact]
    public void TryParse_RejectsTruncatedPayload()
    {
        Span<byte> packet = stackalloc byte[7];
        packet[0] = 0x40;
        packet[1] = 0x00;
        packet[2] = 0x00;
        packet[3] = 0x04;

        TurnChannelDataStatus status = TurnChannelDataMessage.TryParse(packet, out _);

        Assert.Equal(TurnChannelDataStatus.InvalidPacket, status);
    }

    [Fact]
    public void GetEncodedLength_AllowsMaximumPayloadLength()
    {
        int encodedLength = TurnChannelDataMessage.GetEncodedLength(ushort.MaxValue);

        Assert.Equal(TurnChannelDataMessage.HeaderLength + ushort.MaxValue + 1, encodedLength);
    }

    [Fact]
    public void GetEncodedLength_RejectsPayloadBeyondChannelDataLengthField()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TurnChannelDataMessage.GetEncodedLength(ushort.MaxValue + 1));
    }

    [Fact]
    public void ChannelData_ParseAndWriteDoNotAllocate()
    {
        ReadOnlySpan<byte> payload = [0x80, 0x61, 0x00, 0x02];
        Span<byte> packet = stackalloc byte[TurnChannelDataMessage.HeaderLength + 4];
        Assert.Equal(TurnChannelDataStatus.Success, TurnChannelDataMessage.TryWrite(
            0x4002,
            payload,
            packet,
            out int bytesWritten));

        for (int i = 0; i < 32; i++)
        {
            if (TurnChannelDataMessage.TryParse(packet[..bytesWritten], out TurnChannelDataView warmupView) != TurnChannelDataStatus.Success ||
                warmupView.ChannelNumber != 0x4002)
            {
                throw new InvalidOperationException("TURN ChannelData warmup failed.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            Span<byte> destination = stackalloc byte[TurnChannelDataMessage.HeaderLength + 4];
            if (TurnChannelDataMessage.TryWrite(0x4002, payload, destination, out int written) != TurnChannelDataStatus.Success ||
                TurnChannelDataMessage.TryParse(destination[..written], out TurnChannelDataView view) != TurnChannelDataStatus.Success ||
                view.Payload.Length != payload.Length)
            {
                throw new InvalidOperationException("TURN ChannelData parse/write failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task ChannelDataDatagramPath_WrapsOutboundPayload()
    {
        var inner = new InMemoryDatagramPath();
        await using var path = new TurnChannelDataDatagramPath(inner, 0x4003, maximumPayloadLength: 64);
        byte[] payload = [0x80, 0x60, 0x00, 0x03];

        await path.SendAsync(payload);

        Assert.Single(inner.SentDatagrams);
        byte[] sent = inner.SentDatagrams[0];
        Assert.Equal(TurnChannelDataStatus.Success, TurnChannelDataMessage.TryParse(sent, out TurnChannelDataView view));
        Assert.Equal(0x4003, view.ChannelNumber);
        Assert.True(view.Payload.SequenceEqual(payload));
    }

    [Fact]
    public async Task ChannelDataDatagramPath_UnwrapsInboundPayloadAndSkipsOtherChannels()
    {
        var inner = new InMemoryDatagramPath();
        byte[] wrongChannel = new byte[16];
        byte[] expected = [0x80, 0x61, 0x00, 0x04];
        byte[] encoded = new byte[16];
        Assert.Equal(TurnChannelDataStatus.Success, TurnChannelDataMessage.TryWrite(
            0x4004,
            [0x01, 0x02],
            wrongChannel,
            out int wrongBytes));
        Assert.Equal(TurnChannelDataStatus.Success, TurnChannelDataMessage.TryWrite(
            0x4005,
            expected,
            encoded,
            out int encodedBytes));
        inner.EnqueueReceive(wrongChannel.AsMemory(0, wrongBytes));
        inner.EnqueueReceive(encoded.AsMemory(0, encodedBytes));
        await using var path = new TurnChannelDataDatagramPath(inner, 0x4005, maximumPayloadLength: 64);
        byte[] destination = new byte[16];

        DatagramReceiveResult result = await path.ReceiveAsync(destination);

        Assert.True(result.HasDatagram);
        Assert.Equal(expected.Length, result.BytesWritten);
        Assert.Equal(expected, destination.AsSpan(0, result.BytesWritten).ToArray());
        Assert.Equal(DatagramProtocolHint.SrtpOrSrtcp, result.Hint);
        Assert.Equal(2, inner.ReceiveCount);
    }

    [Fact]
    public void AllocationRequest_RoundTripsUdpRequestedTransport()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x42);
        Span<byte> request = stackalloc byte[StunBindingMessage.HeaderLength + 8];

        TurnAllocationStatus writeStatus = TurnAllocationMessage.TryWriteUdpAllocateRequest(
            request,
            transactionId,
            out int bytesWritten);
        Span<byte> parsedTransactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        TurnAllocationStatus parseStatus = TurnAllocationMessage.TryParseUdpAllocateRequest(
            request[..bytesWritten],
            parsedTransactionId);

        Assert.Equal(TurnAllocationStatus.Success, writeStatus);
        Assert.Equal(StunBindingMessage.HeaderLength + 8, bytesWritten);
        Assert.Equal(TurnAllocationStatus.Success, parseStatus);
        Assert.True(parsedTransactionId.SequenceEqual(transactionId));
    }

    [Fact]
    public void LongTermCredentialKey_MatchesRfc5389Example()
    {
        byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "realm", "pass");

        Assert.Equal(Convert.FromHexString("8493FBC53BA582FB4C044C456BDC40EB"), key);
    }

    [Fact]
    public void AuthenticatedAllocationRequest_WritesAndVerifiesMessageIntegrity()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x24);
        byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "realm", "pass");
        Span<byte> request = stackalloc byte[256];

        TurnAllocationStatus writeStatus = TurnAllocationMessage.TryWriteAuthenticatedUdpAllocateRequest(
            request,
            transactionId,
            "user",
            "realm",
            "nonce-value",
            key,
            out int bytesWritten);
        Span<byte> parsedTransactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        TurnAllocationStatus parseStatus = TurnAllocationMessage.TryParseUdpAllocateRequest(
            request[..bytesWritten],
            parsedTransactionId);
        bool verified = TurnAllocationMessage.TryVerifyMessageIntegrity(request[..bytesWritten], key);

        Assert.Equal(TurnAllocationStatus.Success, writeStatus);
        Assert.Equal(TurnAllocationStatus.Success, parseStatus);
        Assert.True(parsedTransactionId.SequenceEqual(transactionId));
        Assert.True(verified);

        request[bytesWritten - 1] ^= 0x01;
        Assert.False(TurnAllocationMessage.TryVerifyMessageIntegrity(request[..bytesWritten], key));
    }

    [Fact]
    public void AllocationChallengeResponse_RoundTripsRealmNonceAndErrorCode()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x32);
        Span<byte> unauthorized = stackalloc byte[128];
        Span<byte> staleNonce = stackalloc byte[128];

        TurnAllocationStatus unauthorizedWriteStatus = TurnAllocationMessage.TryWriteAllocateChallengeResponse(
            unauthorized,
            transactionId,
            401,
            "example.org",
            "nonce-1",
            out int unauthorizedLength);
        TurnAllocationStatus unauthorizedParseStatus = TurnAllocationMessage.TryParseAllocateChallengeResponse(
            unauthorized[..unauthorizedLength],
            transactionId,
            out TurnAllocationChallenge unauthorizedChallenge);

        TurnAllocationStatus staleNonceWriteStatus = TurnAllocationMessage.TryWriteAllocateChallengeResponse(
            staleNonce,
            transactionId,
            438,
            "example.org",
            "nonce-2",
            out int staleNonceLength);
        TurnAllocationStatus staleNonceParseStatus = TurnAllocationMessage.TryParseAllocateChallengeResponse(
            staleNonce[..staleNonceLength],
            transactionId,
            out TurnAllocationChallenge staleNonceChallenge);

        Assert.Equal(TurnAllocationStatus.Success, unauthorizedWriteStatus);
        Assert.Equal(TurnAllocationStatus.Unauthorized, unauthorizedParseStatus);
        Assert.Equal(401, unauthorizedChallenge.ErrorCode);
        Assert.Equal("example.org", unauthorizedChallenge.Realm);
        Assert.Equal("nonce-1", unauthorizedChallenge.Nonce);
        Assert.Equal(TurnAllocationStatus.Success, staleNonceWriteStatus);
        Assert.Equal(TurnAllocationStatus.StaleNonce, staleNonceParseStatus);
        Assert.Equal(438, staleNonceChallenge.ErrorCode);
        Assert.Equal("nonce-2", staleNonceChallenge.Nonce);
    }

    [Fact]
    public void AllocationSuccessResponse_RoundTripsRelayedAddressAndLifetime()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x33);
        var relayedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.44"), 62000);
        Span<byte> response = stackalloc byte[64];

        TurnAllocationStatus writeStatus = TurnAllocationMessage.TryWriteAllocateSuccessResponse(
            response,
            transactionId,
            relayedEndPoint,
            TimeSpan.FromSeconds(600),
            out int bytesWritten);
        TurnAllocationStatus parseStatus = TurnAllocationMessage.TryParseAllocateSuccessResponse(
            response[..bytesWritten],
            transactionId,
            out IPEndPoint parsedEndPoint,
            out TimeSpan lifetime);

        Assert.Equal(TurnAllocationStatus.Success, writeStatus);
        Assert.Equal(TurnAllocationStatus.Success, parseStatus);
        Assert.Equal(relayedEndPoint, parsedEndPoint);
        Assert.Equal(TimeSpan.FromSeconds(600), lifetime);
    }

    [Fact]
    public void CreatePermissionRequest_RoundTripsPeerAddressAndSuccessResponse()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x44);
        var peerEndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.10"), 51345);
        byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "realm", "pass");
        Span<byte> request = stackalloc byte[256];
        Span<byte> parsedTransactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        Span<byte> response = stackalloc byte[StunBindingMessage.HeaderLength];

        TurnAllocationStatus writeStatus = TurnAllocationMessage.TryWriteAuthenticatedCreatePermissionRequest(
            request,
            transactionId,
            peerEndPoint,
            "user",
            "realm",
            "nonce-value",
            key,
            out int requestLength);
        TurnAllocationStatus parseStatus = TurnAllocationMessage.TryParseCreatePermissionRequest(
            request[..requestLength],
            parsedTransactionId,
            out IPEndPoint parsedPeerEndPoint);
        TurnAllocationStatus responseWriteStatus = TurnAllocationMessage.TryWriteCreatePermissionSuccessResponse(
            response,
            parsedTransactionId,
            out int responseLength);
        TurnAllocationStatus responseParseStatus = TurnAllocationMessage.TryParseCreatePermissionSuccessResponse(
            response[..responseLength],
            parsedTransactionId);

        Assert.Equal(TurnAllocationStatus.Success, writeStatus);
        Assert.Equal(TurnAllocationStatus.Success, parseStatus);
        Assert.True(parsedTransactionId.SequenceEqual(transactionId));
        Assert.Equal(peerEndPoint, parsedPeerEndPoint);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(request[..requestLength], key));
        Assert.Equal(TurnAllocationStatus.Success, responseWriteStatus);
        Assert.Equal(TurnAllocationStatus.Success, responseParseStatus);
    }

    [Fact]
    public void RefreshRequest_RoundTripsLifetimeAndSuccessResponse()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x45);
        byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "realm", "pass");
        Span<byte> request = stackalloc byte[256];
        Span<byte> parsedTransactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        Span<byte> response = stackalloc byte[StunBindingMessage.HeaderLength];

        TurnAllocationStatus writeStatus = TurnAllocationMessage.TryWriteAuthenticatedRefreshRequest(
            request,
            transactionId,
            TimeSpan.FromSeconds(300),
            "user",
            "realm",
            "nonce-value",
            key,
            out int requestLength);
        TurnAllocationStatus parseStatus = TurnAllocationMessage.TryParseRefreshRequest(
            request[..requestLength],
            parsedTransactionId,
            out TimeSpan lifetime);
        TurnAllocationStatus responseWriteStatus = TurnAllocationMessage.TryWriteRefreshSuccessResponse(
            response,
            parsedTransactionId,
            out int responseLength);
        TurnAllocationStatus responseParseStatus = TurnAllocationMessage.TryParseRefreshSuccessResponse(
            response[..responseLength],
            parsedTransactionId);

        Assert.Equal(TurnAllocationStatus.Success, writeStatus);
        Assert.Equal(TurnAllocationStatus.Success, parseStatus);
        Assert.True(parsedTransactionId.SequenceEqual(transactionId));
        Assert.Equal(TimeSpan.FromSeconds(300), lifetime);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(request[..requestLength], key));
        Assert.Equal(TurnAllocationStatus.Success, responseWriteStatus);
        Assert.Equal(TurnAllocationStatus.Success, responseParseStatus);
    }

    [Fact]
    public void ChannelBindRequest_RoundTripsChannelPeerAndSuccessResponse()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x46);
        var peerEndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.11"), 51346);
        byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "realm", "pass");
        Span<byte> request = stackalloc byte[256];
        Span<byte> parsedTransactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        Span<byte> response = stackalloc byte[StunBindingMessage.HeaderLength];

        TurnAllocationStatus writeStatus = TurnAllocationMessage.TryWriteAuthenticatedChannelBindRequest(
            request,
            transactionId,
            0x4007,
            peerEndPoint,
            "user",
            "realm",
            "nonce-value",
            key,
            out int requestLength);
        TurnAllocationStatus parseStatus = TurnAllocationMessage.TryParseChannelBindRequest(
            request[..requestLength],
            parsedTransactionId,
            out ushort channelNumber,
            out IPEndPoint parsedPeerEndPoint);
        TurnAllocationStatus responseWriteStatus = TurnAllocationMessage.TryWriteChannelBindSuccessResponse(
            response,
            parsedTransactionId,
            out int responseLength);
        TurnAllocationStatus responseParseStatus = TurnAllocationMessage.TryParseChannelBindSuccessResponse(
            response[..responseLength],
            parsedTransactionId);

        Assert.Equal(TurnAllocationStatus.Success, writeStatus);
        Assert.Equal(TurnAllocationStatus.Success, parseStatus);
        Assert.True(parsedTransactionId.SequenceEqual(transactionId));
        Assert.Equal(0x4007, channelNumber);
        Assert.Equal(peerEndPoint, parsedPeerEndPoint);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(request[..requestLength], key));
        Assert.Equal(TurnAllocationStatus.Success, responseWriteStatus);
        Assert.Equal(TurnAllocationStatus.Success, responseParseStatus);
    }

    [Fact]
    public void AllocationMessages_RequestParseAndWritesDoNotAllocate()
    {
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        Array.Fill<byte>(transactionId, 0x55);
        byte[] request = new byte[StunBindingMessage.HeaderLength + 8];
        byte[] parsedTransactionId = new byte[StunBindingMessage.TransactionIdLength];
        byte[] response = new byte[64];
        var relayedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.50"), 50123);

        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteUdpAllocateRequest(
            request,
            transactionId,
            out int requestLength));
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteAllocateSuccessResponse(
            response,
            transactionId,
            relayedEndPoint,
            TimeSpan.FromSeconds(300),
            out int responseLength));

        for (int i = 0; i < 32; i++)
        {
            Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseUdpAllocateRequest(
                request.AsSpan(0, requestLength),
                parsedTransactionId));
            Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseAllocateSuccessResponse(
                response.AsSpan(0, responseLength),
                transactionId,
                out _,
                out _));
        }

        byte[] requestDestination = new byte[StunBindingMessage.HeaderLength + 8];
        byte[] responseDestination = new byte[64];
        byte[] parsed = new byte[StunBindingMessage.TransactionIdLength];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            if (TurnAllocationMessage.TryWriteUdpAllocateRequest(requestDestination, transactionId, out int writtenRequest) != TurnAllocationStatus.Success ||
                TurnAllocationMessage.TryParseUdpAllocateRequest(requestDestination.AsSpan(0, writtenRequest), parsed) != TurnAllocationStatus.Success ||
                TurnAllocationMessage.TryWriteAllocateSuccessResponse(responseDestination, transactionId, relayedEndPoint, TimeSpan.FromSeconds(300), out _) != TurnAllocationStatus.Success)
            {
                throw new InvalidOperationException("TURN allocation parse/write failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task UdpTurnRelayCandidateAllocator_AllocatesRelayCandidate()
    {
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        var relayedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.77"), 61234);
        Task serverTask = RunTurnAllocateServerAsync(server, relayedEndPoint);
        var allocator = new UdpTurnRelayCandidateAllocator(TimeSpan.FromSeconds(2));
        var iceServer = new IceServerOptions
        {
            Uri = new Uri($"turn:127.0.0.1:{serverEndPoint.Port}")
        };

        IceCandidate? candidate = await allocator.AllocateAsync(
            iceServer,
            new IPEndPoint(IPAddress.Loopback, 0));
        await serverTask;

        Assert.NotNull(candidate);
        Assert.Equal(IceCandidateType.Relay, candidate.Value.CandidateType);
        Assert.Equal(relayedEndPoint, candidate.Value.EndPoint);
        Assert.Equal("UDP", candidate.Value.Transport);
        Assert.Contains(candidate.Value.ExtensionAttributes.ToArray(), attribute =>
            attribute.Name == "turn-lifetime" && attribute.Value == "600");
    }

    [Fact]
    public async Task UdpTurnRelayCandidateAllocator_SendsMessageIntegrityWhenCredentialsAreConfigured()
    {
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        var relayedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.78"), 61235);
        byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "example.org", "pass");
        Task serverTask = RunAuthenticatedTurnAllocateServerAsync(server, relayedEndPoint, key);
        var allocator = new UdpTurnRelayCandidateAllocator(TimeSpan.FromSeconds(2));
        var iceServer = new IceServerOptions
        {
            Uri = new Uri($"turn:127.0.0.1:{serverEndPoint.Port}"),
            Username = "user",
            Credential = "pass",
            Realm = "example.org",
            Nonce = "nonce-1"
        };

        IceCandidate? candidate = await allocator.AllocateAsync(
            iceServer,
            new IPEndPoint(IPAddress.Loopback, 0));
        await serverTask;

        Assert.NotNull(candidate);
        Assert.Equal(IceCandidateType.Relay, candidate.Value.CandidateType);
        Assert.Equal(relayedEndPoint, candidate.Value.EndPoint);
    }

    [Fact]
    public async Task UdpTurnRelayCandidateAllocator_RetriesWithMessageIntegrityAfterUnauthorizedChallenge()
    {
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        var relayedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.79"), 61236);
        byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "example.org", "pass");
        Task serverTask = RunChallengedTurnAllocateServerAsync(server, relayedEndPoint, key);
        var allocator = new UdpTurnRelayCandidateAllocator(TimeSpan.FromSeconds(2));
        var iceServer = new IceServerOptions
        {
            Uri = new Uri($"turn:127.0.0.1:{serverEndPoint.Port}"),
            Username = "user",
            Credential = "pass"
        };

        IceCandidate? candidate = await allocator.AllocateAsync(
            iceServer,
            new IPEndPoint(IPAddress.Loopback, 0));
        await serverTask;

        Assert.NotNull(candidate);
        Assert.Equal(IceCandidateType.Relay, candidate.Value.CandidateType);
        Assert.Equal(relayedEndPoint, candidate.Value.EndPoint);
    }

    [Fact]
    public async Task UdpTurnRelayAllocation_PerformsPermissionChannelBindRefreshAndDelete()
    {
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        var relayedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.80"), 61237);
        var peerEndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.80"), 51347);
        byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "example.org", "pass");
        Task serverTask = RunTurnRelayAllocationLifecycleServerAsync(server, relayedEndPoint, peerEndPoint, key);
        var iceServer = new IceServerOptions
        {
            Uri = new Uri($"turn:127.0.0.1:{serverEndPoint.Port}"),
            Username = "user",
            Credential = "pass"
        };

        await using UdpTurnRelayAllocation? allocation = await UdpTurnRelayAllocation.AllocateAsync(
            iceServer,
            new IPEndPoint(IPAddress.Loopback, 0),
            TimeSpan.FromSeconds(2));

        Assert.NotNull(allocation);
        Assert.Equal(relayedEndPoint, allocation.RelayedEndPoint);
        Assert.Equal(TimeSpan.FromSeconds(600), allocation.Lifetime);
        Assert.True(await allocation.CreatePermissionAsync(peerEndPoint));
        Assert.True(await allocation.BindChannelAsync(0x4009, peerEndPoint));
        Assert.True(await allocation.RefreshAsync(TimeSpan.FromSeconds(300)));
        Assert.Equal(TimeSpan.FromSeconds(300), allocation.Lifetime);

        await allocation.DisposeAsync();
        await serverTask;
    }

    [Fact]
    public async Task UdpTurnRelayAllocation_OpenChannelDataPath_MovesMediaThroughRelay()
    {
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        var relayedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.81"), 61238);
        var peerEndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.81"), 51348);
        byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "example.org", "pass");
        Task serverTask = RunTurnRelayChannelDataPathServerAsync(server, relayedEndPoint, peerEndPoint, 0x400A, key);
        var iceServer = new IceServerOptions
        {
            Uri = new Uri($"turn:127.0.0.1:{serverEndPoint.Port}"),
            Username = "user",
            Credential = "pass"
        };

        await using UdpTurnRelayAllocation? allocation = await UdpTurnRelayAllocation.AllocateAsync(
            iceServer,
            new IPEndPoint(IPAddress.Loopback, 0),
            TimeSpan.FromSeconds(2));
        Assert.NotNull(allocation);
        await using IDatagramPath? path = await allocation.OpenChannelDataPathAsync(0x400A, peerEndPoint);
        Assert.NotNull(path);

        byte[] outboundPayload = [0x80, 0x62, 0x00, 0x0A];
        await path.SendAsync(outboundPayload);
        byte[] receiveBuffer = new byte[32];
        DatagramReceiveResult inbound = await path.ReceiveAsync(receiveBuffer, CancellationToken.None);

        Assert.True(inbound.HasDatagram);
        Assert.Equal(outboundPayload.Length, inbound.BytesWritten);
        Assert.Equal(outboundPayload, receiveBuffer.AsSpan(0, inbound.BytesWritten).ToArray());

        await allocation.DisposeAsync();
        await serverTask;
    }

    private static async Task RunTurnAllocateServerAsync(Socket server, IPEndPoint relayedEndPoint)
    {
        byte[] request = new byte[StunBindingMessage.HeaderLength + 64];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        SocketReceiveFromResult result = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseUdpAllocateRequest(
            request.AsSpan(0, result.ReceivedBytes),
            transactionId));

        byte[] response = new byte[64];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteAllocateSuccessResponse(
            response,
            transactionId,
            relayedEndPoint,
            TimeSpan.FromSeconds(600),
            out int responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, result.RemoteEndPoint);
    }

    private static async Task RunAuthenticatedTurnAllocateServerAsync(
        Socket server,
        IPEndPoint relayedEndPoint,
        byte[] longTermCredentialKey)
    {
        byte[] request = new byte[512];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        SocketReceiveFromResult result = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseUdpAllocateRequest(
            request.AsSpan(0, result.ReceivedBytes),
            transactionId));
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, result.ReceivedBytes),
            longTermCredentialKey));

        byte[] response = new byte[64];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteAllocateSuccessResponse(
            response,
            transactionId,
            relayedEndPoint,
            TimeSpan.FromSeconds(600),
            out int responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, result.RemoteEndPoint);
    }

    private static async Task RunChallengedTurnAllocateServerAsync(
        Socket server,
        IPEndPoint relayedEndPoint,
        byte[] longTermCredentialKey)
    {
        byte[] firstRequest = new byte[512];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        SocketReceiveFromResult firstResult = await server.ReceiveFromAsync(firstRequest, SocketFlags.None, remote);
        byte[] firstTransactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseUdpAllocateRequest(
            firstRequest.AsSpan(0, firstResult.ReceivedBytes),
            firstTransactionId));

        byte[] challenge = new byte[128];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteAllocateChallengeResponse(
            challenge,
            firstTransactionId,
            401,
            "example.org",
            "nonce-2",
            out int challengeLength));
        _ = await server.SendToAsync(challenge.AsMemory(0, challengeLength), SocketFlags.None, firstResult.RemoteEndPoint);

        byte[] secondRequest = new byte[512];
        SocketReceiveFromResult secondResult = await server.ReceiveFromAsync(secondRequest, SocketFlags.None, remote);
        byte[] secondTransactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseUdpAllocateRequest(
            secondRequest.AsSpan(0, secondResult.ReceivedBytes),
            secondTransactionId));
        Assert.False(firstTransactionId.AsSpan().SequenceEqual(secondTransactionId));
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            secondRequest.AsSpan(0, secondResult.ReceivedBytes),
            longTermCredentialKey));

        byte[] response = new byte[64];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteAllocateSuccessResponse(
            response,
            secondTransactionId,
            relayedEndPoint,
            TimeSpan.FromSeconds(600),
            out int responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, secondResult.RemoteEndPoint);
    }

    private static async Task RunTurnRelayAllocationLifecycleServerAsync(
        Socket server,
        IPEndPoint relayedEndPoint,
        IPEndPoint peerEndPoint,
        byte[] longTermCredentialKey)
    {
        byte[] request = new byte[512];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        SocketReceiveFromResult firstResult = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        byte[] firstTransactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseUdpAllocateRequest(
            request.AsSpan(0, firstResult.ReceivedBytes),
            firstTransactionId));

        byte[] response = new byte[128];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteAllocateChallengeResponse(
            response,
            firstTransactionId,
            401,
            "example.org",
            "nonce-3",
            out int responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, firstResult.RemoteEndPoint);

        SocketReceiveFromResult allocateResult = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseUdpAllocateRequest(
            request.AsSpan(0, allocateResult.ReceivedBytes),
            transactionId));
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, allocateResult.ReceivedBytes),
            longTermCredentialKey));
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteAllocateSuccessResponse(
            response,
            transactionId,
            relayedEndPoint,
            TimeSpan.FromSeconds(600),
            out responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, allocateResult.RemoteEndPoint);

        SocketReceiveFromResult permissionResult = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseCreatePermissionRequest(
            request.AsSpan(0, permissionResult.ReceivedBytes),
            transactionId,
            out IPEndPoint parsedPermissionPeer));
        Assert.Equal(peerEndPoint, parsedPermissionPeer);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, permissionResult.ReceivedBytes),
            longTermCredentialKey));
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteCreatePermissionSuccessResponse(
            response,
            transactionId,
            out responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, permissionResult.RemoteEndPoint);

        SocketReceiveFromResult channelBindResult = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseChannelBindRequest(
            request.AsSpan(0, channelBindResult.ReceivedBytes),
            transactionId,
            out ushort channelNumber,
            out IPEndPoint parsedChannelPeer));
        Assert.Equal(0x4009, channelNumber);
        Assert.Equal(peerEndPoint, parsedChannelPeer);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, channelBindResult.ReceivedBytes),
            longTermCredentialKey));
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteChannelBindSuccessResponse(
            response,
            transactionId,
            out responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, channelBindResult.RemoteEndPoint);

        SocketReceiveFromResult refreshResult = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseRefreshRequest(
            request.AsSpan(0, refreshResult.ReceivedBytes),
            transactionId,
            out TimeSpan refreshedLifetime));
        Assert.Equal(TimeSpan.FromSeconds(300), refreshedLifetime);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, refreshResult.ReceivedBytes),
            longTermCredentialKey));
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteRefreshSuccessResponse(
            response,
            transactionId,
            out responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, refreshResult.RemoteEndPoint);

        SocketReceiveFromResult deleteResult = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseRefreshRequest(
            request.AsSpan(0, deleteResult.ReceivedBytes),
            transactionId,
            out TimeSpan deletedLifetime));
        Assert.Equal(TimeSpan.Zero, deletedLifetime);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, deleteResult.ReceivedBytes),
            longTermCredentialKey));
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteRefreshSuccessResponse(
            response,
            transactionId,
            out responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, deleteResult.RemoteEndPoint);
    }

    private static async Task RunTurnRelayChannelDataPathServerAsync(
        Socket server,
        IPEndPoint relayedEndPoint,
        IPEndPoint peerEndPoint,
        ushort channelNumber,
        byte[] longTermCredentialKey)
    {
        byte[] request = new byte[512];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        IPEndPoint clientEndPoint = await CompleteTurnAllocateHandshakeAsync(
            server,
            request,
            remote,
            relayedEndPoint,
            longTermCredentialKey);

        await ExpectCreatePermissionAsync(server, request, remote, peerEndPoint, longTermCredentialKey);
        await ExpectChannelBindAsync(server, request, remote, peerEndPoint, channelNumber, longTermCredentialKey);

        SocketReceiveFromResult mediaResult = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        Assert.Equal(clientEndPoint, mediaResult.RemoteEndPoint);
        Assert.Equal(TurnChannelDataStatus.Success, TurnChannelDataMessage.TryParse(
            request.AsSpan(0, mediaResult.ReceivedBytes),
            out TurnChannelDataView outbound));
        Assert.Equal(channelNumber, outbound.ChannelNumber);

        byte[] encodedInbound = new byte[32];
        Assert.Equal(TurnChannelDataStatus.Success, TurnChannelDataMessage.TryWrite(
            channelNumber,
            outbound.Payload,
            encodedInbound,
            out int encodedInboundBytes));
        _ = await server.SendToAsync(
            encodedInbound.AsMemory(0, encodedInboundBytes),
            SocketFlags.None,
            clientEndPoint);

        await ExpectRefreshAsync(server, request, remote, TimeSpan.Zero, longTermCredentialKey);
    }

    private static async Task<IPEndPoint> CompleteTurnAllocateHandshakeAsync(
        Socket server,
        byte[] request,
        EndPoint remote,
        IPEndPoint relayedEndPoint,
        byte[] longTermCredentialKey)
    {
        SocketReceiveFromResult firstResult = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        var clientEndPoint = (IPEndPoint)firstResult.RemoteEndPoint;
        byte[] firstTransactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseUdpAllocateRequest(
            request.AsSpan(0, firstResult.ReceivedBytes),
            firstTransactionId));

        byte[] response = new byte[128];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteAllocateChallengeResponse(
            response,
            firstTransactionId,
            401,
            "example.org",
            "nonce-4",
            out int responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, firstResult.RemoteEndPoint);

        SocketReceiveFromResult allocateResult = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseUdpAllocateRequest(
            request.AsSpan(0, allocateResult.ReceivedBytes),
            transactionId));
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, allocateResult.ReceivedBytes),
            longTermCredentialKey));
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteAllocateSuccessResponse(
            response,
            transactionId,
            relayedEndPoint,
            TimeSpan.FromSeconds(600),
            out responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, allocateResult.RemoteEndPoint);
        return clientEndPoint;
    }

    private static async Task ExpectCreatePermissionAsync(
        Socket server,
        byte[] request,
        EndPoint remote,
        IPEndPoint peerEndPoint,
        byte[] longTermCredentialKey)
    {
        SocketReceiveFromResult result = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseCreatePermissionRequest(
            request.AsSpan(0, result.ReceivedBytes),
            transactionId,
            out IPEndPoint parsedPeer));
        Assert.Equal(peerEndPoint, parsedPeer);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, result.ReceivedBytes),
            longTermCredentialKey));

        byte[] response = new byte[64];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteCreatePermissionSuccessResponse(
            response,
            transactionId,
            out int responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, result.RemoteEndPoint);
    }

    private static async Task ExpectChannelBindAsync(
        Socket server,
        byte[] request,
        EndPoint remote,
        IPEndPoint peerEndPoint,
        ushort channelNumber,
        byte[] longTermCredentialKey)
    {
        SocketReceiveFromResult result = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseChannelBindRequest(
            request.AsSpan(0, result.ReceivedBytes),
            transactionId,
            out ushort parsedChannelNumber,
            out IPEndPoint parsedPeer));
        Assert.Equal(channelNumber, parsedChannelNumber);
        Assert.Equal(peerEndPoint, parsedPeer);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, result.ReceivedBytes),
            longTermCredentialKey));

        byte[] response = new byte[64];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteChannelBindSuccessResponse(
            response,
            transactionId,
            out int responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, result.RemoteEndPoint);
    }

    private static async Task ExpectRefreshAsync(
        Socket server,
        byte[] request,
        EndPoint remote,
        TimeSpan lifetime,
        byte[] longTermCredentialKey)
    {
        SocketReceiveFromResult result = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryParseRefreshRequest(
            request.AsSpan(0, result.ReceivedBytes),
            transactionId,
            out TimeSpan parsedLifetime));
        Assert.Equal(lifetime, parsedLifetime);
        Assert.True(TurnAllocationMessage.TryVerifyMessageIntegrity(
            request.AsSpan(0, result.ReceivedBytes),
            longTermCredentialKey));

        byte[] response = new byte[64];
        Assert.Equal(TurnAllocationStatus.Success, TurnAllocationMessage.TryWriteRefreshSuccessResponse(
            response,
            transactionId,
            out int responseLength));
        _ = await server.SendToAsync(response.AsMemory(0, responseLength), SocketFlags.None, result.RemoteEndPoint);
    }

    private sealed class InMemoryDatagramPath : IDatagramPath
    {
        private readonly Queue<byte[]> receives = new();

        public List<byte[]> SentDatagrams { get; } = [];

        public int ReceiveCount { get; private set; }

        public IPEndPoint LocalEndPoint { get; } = new(IPAddress.Loopback, 50000);

        public IPEndPoint RemoteEndPoint { get; } = new(IPAddress.Loopback, 50001);

        public PathState State => PathState.Ready;

        public void EnqueueReceive(ReadOnlyMemory<byte> datagram)
        {
            receives.Enqueue(datagram.ToArray());
        }

        public ValueTask<PathStateChange?> ReadStateChangeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<PathStateChange?>((PathStateChange?)null);
        }

        public ValueTask<DatagramReceiveResult> ReceiveAsync(
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (receives.Count == 0)
            {
                return new ValueTask<DatagramReceiveResult>(new DatagramReceiveResult { HasDatagram = false });
            }

            byte[] datagram = receives.Dequeue();
            datagram.CopyTo(destination.Span);
            ReceiveCount++;
            return new ValueTask<DatagramReceiveResult>(new DatagramReceiveResult
            {
                HasDatagram = true,
                BytesWritten = datagram.Length,
                LocalEndPoint = LocalEndPoint,
                RemoteEndPoint = RemoteEndPoint,
                ReceivedAt = DateTimeOffset.UtcNow,
                Hint = DatagramProtocolHint.Unknown
            });
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SentDatagrams.Add(payload.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
