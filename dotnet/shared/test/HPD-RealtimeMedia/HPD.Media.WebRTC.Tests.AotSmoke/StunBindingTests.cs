#nullable enable

using System.Net;
using System.Net.Sockets;
using HPD.Media.WebRTC;

namespace HPD.Media.WebRTC.Tests.AotSmoke;

public sealed class StunBindingTests
{
    [Fact]
    public void BindingRequest_RoundTripsTransactionId()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        for (int i = 0; i < transactionId.Length; i++)
        {
            transactionId[i] = (byte)(0xA0 + i);
        }

        Span<byte> request = stackalloc byte[StunBindingMessage.HeaderLength];
        Span<byte> parsedTransactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];

        bool written = StunBindingMessage.TryWriteBindingRequest(request, transactionId, out int bytesWritten);
        bool parsed = StunBindingMessage.TryParseBindingRequest(request[..bytesWritten], parsedTransactionId);

        Assert.True(written);
        Assert.True(parsed);
        Assert.True(transactionId.SequenceEqual(parsedTransactionId));
    }

    [Fact]
    public void BindingRequest_RejectsMalformedMessageLength()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x11);
        Span<byte> request = stackalloc byte[StunBindingMessage.HeaderLength];
        Assert.True(StunBindingMessage.TryWriteBindingRequest(request, transactionId, out int bytesWritten));
        request[3] = 4;
        Span<byte> parsedTransactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];

        bool parsed = StunBindingMessage.TryParseBindingRequest(request[..bytesWritten], parsedTransactionId);

        Assert.False(parsed);
    }

    [Fact]
    public void BindingSuccessResponseForRequest_RoundTripsMappedAddress()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x12);
        Span<byte> request = stackalloc byte[StunBindingMessage.HeaderLength];
        Assert.True(StunBindingMessage.TryWriteBindingRequest(request, transactionId, out int requestLength));
        var mappedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.20"), 60000);
        Span<byte> response = stackalloc byte[64];

        bool written = StunBindingMessage.TryWriteBindingSuccessResponseForRequest(
            response,
            request[..requestLength],
            mappedEndPoint,
            out int responseLength);
        bool parsed = StunBindingMessage.TryParseBindingSuccessResponse(
            response[..responseLength],
            transactionId,
            out IPEndPoint reparsed);

        Assert.True(written);
        Assert.True(parsed);
        Assert.Equal(mappedEndPoint, reparsed);
    }

    [Fact]
    public void BindingSuccessResponseForRequest_RejectsNonRequestPacket()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x13);
        Span<byte> successResponse = stackalloc byte[64];
        Assert.True(StunBindingMessage.TryWriteBindingSuccessResponse(
            successResponse,
            transactionId,
            new IPEndPoint(IPAddress.Loopback, 50000),
            out int responseLength));
        Span<byte> destination = stackalloc byte[64];

        bool written = StunBindingMessage.TryWriteBindingSuccessResponseForRequest(
            destination,
            successResponse[..responseLength],
            new IPEndPoint(IPAddress.Loopback, 50001),
            out int bytesWritten);

        Assert.False(written);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void BindingRequestParseAndResponseWrite_AreAllocationFreeAfterWarmup()
    {
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        Array.Fill(transactionId, (byte)0x14);
        byte[] request = new byte[StunBindingMessage.HeaderLength];
        byte[] parsedTransactionId = new byte[StunBindingMessage.TransactionIdLength];
        byte[] response = new byte[64];
        var mappedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.21"), 60001);
        Assert.True(StunBindingMessage.TryWriteBindingRequest(request, transactionId, out int requestLength));

        Assert.True(StunBindingMessage.TryParseBindingRequest(
            request.AsSpan(0, requestLength),
            parsedTransactionId));
        Assert.True(StunBindingMessage.TryWriteBindingSuccessResponseForRequest(
            response,
            request.AsSpan(0, requestLength),
            mappedEndPoint,
            out int responseLength));
        Assert.Equal(32, responseLength);

        bool succeeded = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            succeeded &= StunBindingMessage.TryParseBindingRequest(
                request.AsSpan(0, requestLength),
                parsedTransactionId);
            succeeded &= StunBindingMessage.TryWriteBindingSuccessResponseForRequest(
                response,
                request.AsSpan(0, requestLength),
                mappedEndPoint,
                out responseLength);
            succeeded &= responseLength == 32;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(succeeded);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void BindingSuccessResponse_RoundTripsXorMappedAddress()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        for (int i = 0; i < transactionId.Length; i++)
        {
            transactionId[i] = (byte)(i + 1);
        }

        Span<byte> response = stackalloc byte[64];
        var mappedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 54321);

        bool written = StunBindingMessage.TryWriteBindingSuccessResponse(
            response,
            transactionId,
            mappedEndPoint,
            out int bytesWritten);
        bool parsed = StunBindingMessage.TryParseBindingSuccessResponse(
            response[..bytesWritten],
            transactionId,
            out IPEndPoint reparsed);

        Assert.True(written);
        Assert.True(parsed);
        Assert.Equal(mappedEndPoint, reparsed);
    }

    [Fact]
    public void BindingSuccessResponse_RejectsMismatchedTransactionId()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x22);
        Span<byte> response = stackalloc byte[64];
        Assert.True(StunBindingMessage.TryWriteBindingSuccessResponse(
            response,
            transactionId,
            new IPEndPoint(IPAddress.Loopback, 50000),
            out int bytesWritten));
        Span<byte> otherTransactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        otherTransactionId.Fill(0x33);

        bool parsed = StunBindingMessage.TryParseBindingSuccessResponse(
            response[..bytesWritten],
            otherTransactionId,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void BindingSuccessResponse_RejectsMalformedMessageLength()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x44);
        Span<byte> response = stackalloc byte[64];
        Assert.True(StunBindingMessage.TryWriteBindingSuccessResponse(
            response,
            transactionId,
            new IPEndPoint(IPAddress.Loopback, 50000),
            out int bytesWritten));
        response[3] = 13;

        bool parsed = StunBindingMessage.TryParseBindingSuccessResponse(
            response[..(StunBindingMessage.HeaderLength + 13)],
            transactionId,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void BindingSuccessResponse_RejectsTrailingBytesAfterMessage()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x55);
        Span<byte> response = stackalloc byte[64];
        Assert.True(StunBindingMessage.TryWriteBindingSuccessResponse(
            response,
            transactionId,
            new IPEndPoint(IPAddress.Loopback, 50000),
            out int bytesWritten));
        response[bytesWritten] = 0x99;

        bool parsed = StunBindingMessage.TryParseBindingSuccessResponse(
            response[..(bytesWritten + 1)],
            transactionId,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void BindingSuccessResponse_RejectsMappedAddressWithExtraValueBytes()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x66);
        Span<byte> response = stackalloc byte[36];
        Assert.True(StunBindingMessage.TryWriteBindingSuccessResponse(
            response,
            transactionId,
            new IPEndPoint(IPAddress.Loopback, 50000),
            out int bytesWritten));

        response[3] = 12;
        response[23] = 9;
        response[32] = 0xEE;

        bool parsed = StunBindingMessage.TryParseBindingSuccessResponse(
            response[..(StunBindingMessage.HeaderLength + 12)],
            transactionId,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void BindingSuccessResponse_RejectsMappedAddressWithNonZeroReservedByte()
    {
        Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
        transactionId.Fill(0x67);
        Span<byte> response = stackalloc byte[64];
        Assert.True(StunBindingMessage.TryWriteBindingSuccessResponse(
            response,
            transactionId,
            new IPEndPoint(IPAddress.Loopback, 50000),
            out int bytesWritten));

        response[StunBindingMessage.HeaderLength + 4] = 0x7F;

        bool parsed = StunBindingMessage.TryParseBindingSuccessResponse(
            response[..bytesWritten],
            transactionId,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public async Task UdpStunServerReflexiveCandidateGatherer_GathersMappedEndpoint()
    {
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        Task serverTask = RunStunServerAsync(server);
        var gatherer = new UdpStunServerReflexiveCandidateGatherer(TimeSpan.FromSeconds(2));
        var iceServer = new IceServerOptions
        {
            Uri = new Uri($"stun:{serverEndPoint.Address}:{serverEndPoint.Port}")
        };
        var localEndPoint = new IPEndPoint(IPAddress.Loopback, 5555);

        IceCandidate? candidate = await gatherer.GatherAsync(iceServer, localEndPoint);
        await serverTask;

        Assert.NotNull(candidate);
        Assert.Equal(IceCandidateType.ServerReflexive, candidate.Value.CandidateType);
        Assert.NotNull(candidate.Value.EndPoint);
        Assert.Equal(IPAddress.Loopback, candidate.Value.EndPoint.Address);
        Assert.InRange(candidate.Value.EndPoint.Port, IPEndPoint.MinPort, IPEndPoint.MaxPort);
        Assert.Contains(candidate.Value.ExtensionAttributes.ToArray(), value => value.Name == "raddr" && value.Value == "127.0.0.1");
        Assert.Contains(candidate.Value.ExtensionAttributes.ToArray(), value => value.Name == "rport" && value.Value == "5555");
    }

    [Fact]
    public async Task UdpIceConnectivityChecker_ReturnsTrueForBindingSuccessResponse()
    {
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var clientEndPoint = (IPEndPoint)client.LocalEndPoint!;
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        Task serverTask = RunStunServerAsync(server);
        var checker = new UdpIceConnectivityChecker();

        bool succeeded = await checker.CheckAsync(
            client,
            CreateCredentials("local"),
            CreateCredentials("remote"),
            CreateCandidate(clientEndPoint),
            CreateCandidate(serverEndPoint),
            TimeSpan.FromSeconds(2));
        await serverTask;

        Assert.True(succeeded);
    }

    [Fact]
    public async Task UdpIceConnectivityChecker_ReturnsFalseForInvalidResponse()
    {
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var clientEndPoint = (IPEndPoint)client.LocalEndPoint!;
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        Task serverTask = RunInvalidStunServerAsync(server);
        var checker = new UdpIceConnectivityChecker();

        bool succeeded = await checker.CheckAsync(
            client,
            CreateCredentials("local"),
            CreateCredentials("remote"),
            CreateCandidate(clientEndPoint),
            CreateCandidate(serverEndPoint),
            TimeSpan.FromSeconds(2));
        await serverTask;

        Assert.False(succeeded);
    }

    [Fact]
    public async Task UdpIceConnectivityChecker_ReturnsFalseWhenResponseTimesOut()
    {
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var clientEndPoint = (IPEndPoint)client.LocalEndPoint!;
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        var checker = new UdpIceConnectivityChecker();

        bool succeeded = await checker.CheckAsync(
            client,
            CreateCredentials("local"),
            CreateCredentials("remote"),
            CreateCandidate(clientEndPoint),
            CreateCandidate(serverEndPoint),
            TimeSpan.FromMilliseconds(25));

        Assert.False(succeeded);
    }

    [Fact]
    public async Task UdpIceConnectivityChecker_PropagatesCallerCancellation()
    {
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var clientEndPoint = (IPEndPoint)client.LocalEndPoint!;
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var checker = new UdpIceConnectivityChecker();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await checker.CheckAsync(
            client,
            CreateCredentials("local"),
            CreateCredentials("remote"),
            CreateCandidate(clientEndPoint),
            CreateCandidate(serverEndPoint),
            TimeSpan.FromSeconds(2),
            cancellation.Token));
    }

    private static async Task RunStunServerAsync(Socket server)
    {
        byte[] request = new byte[StunBindingMessage.HeaderLength];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        SocketReceiveFromResult received = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        var remoteEndPoint = (IPEndPoint)received.RemoteEndPoint;
        byte[] response = new byte[64];
        Assert.True(StunBindingMessage.TryWriteBindingSuccessResponseForRequest(
            response,
            request.AsSpan(0, received.ReceivedBytes),
            remoteEndPoint,
            out int bytesWritten));
        _ = await server.SendToAsync(response.AsMemory(0, bytesWritten), SocketFlags.None, remoteEndPoint);
    }

    private static async Task RunInvalidStunServerAsync(Socket server)
    {
        byte[] request = new byte[StunBindingMessage.HeaderLength];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        SocketReceiveFromResult received = await server.ReceiveFromAsync(request, SocketFlags.None, remote);
        var remoteEndPoint = (IPEndPoint)received.RemoteEndPoint;
        byte[] response = new byte[StunBindingMessage.HeaderLength];
        response[0] = 0x01;
        response[1] = 0x01;
        response[4] = 0x21;
        response[5] = 0x12;
        response[6] = 0xA4;
        response[7] = 0x42;
        response.AsSpan(8, StunBindingMessage.TransactionIdLength).Fill(0x77);
        _ = await server.SendToAsync(response, SocketFlags.None, remoteEndPoint);
    }

    private static IceCredentials CreateCredentials(string prefix)
    {
        return new IceCredentials
        {
            UsernameFragment = $"{prefix}-ufrag",
            Password = $"{prefix}-password"
        };
    }

    private static IceCandidate CreateCandidate(IPEndPoint endPoint)
    {
        return new IceCandidate
        {
            Foundation = "1",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            EndPoint = endPoint,
            CandidateType = IceCandidateType.Host
        };
    }
}
