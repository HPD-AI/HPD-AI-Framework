#nullable enable

using System.Net;
using System.Net.Sockets;
using HPD.Media.Transport;

namespace HPD.Media.Transport.Tests.Datagrams;

public sealed class UdpDatagramPathTests
{
    [Fact]
    public async Task SendAsyncAndReceiveAsync_MoveDatagramThroughCallerBuffer()
    {
        using Socket leftSocket = CreateBoundSocket();
        using Socket rightSocket = CreateBoundSocket();
        var leftEndPoint = (IPEndPoint)leftSocket.LocalEndPoint!;
        var rightEndPoint = (IPEndPoint)rightSocket.LocalEndPoint!;
        await using var left = new UdpDatagramPath(leftSocket, rightEndPoint);
        await using var right = new UdpDatagramPath(rightSocket, leftEndPoint);
        byte[] payload = [0x80, 0x60, 0x00, 0x01];
        byte[] destination = new byte[64];

        await left.SendAsync(payload);
        DatagramReceiveResult result = await ReceiveWithTimeout(right, destination);

        Assert.True(result.HasDatagram);
        Assert.Equal(payload.Length, result.BytesWritten);
        Assert.Equal(payload, destination.AsSpan(0, result.BytesWritten).ToArray());
        Assert.Equal(rightEndPoint, result.LocalEndPoint);
        Assert.Equal(leftEndPoint, result.RemoteEndPoint);
        Assert.Equal(DatagramProtocolHint.SrtpOrSrtcp, result.Hint);
    }

    [Fact]
    public async Task ReceiveAsync_ClassifiesStunDatagram()
    {
        using Socket leftSocket = CreateBoundSocket();
        using Socket rightSocket = CreateBoundSocket();
        var leftEndPoint = (IPEndPoint)leftSocket.LocalEndPoint!;
        var rightEndPoint = (IPEndPoint)rightSocket.LocalEndPoint!;
        await using var left = new UdpDatagramPath(leftSocket, rightEndPoint);
        await using var right = new UdpDatagramPath(rightSocket, leftEndPoint);
        byte[] stunBindingRequest =
        [
            0x00, 0x01, 0x00, 0x00,
            0x21, 0x12, 0xA4, 0x42,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0x0C
        ];
        byte[] destination = new byte[64];

        await left.SendAsync(stunBindingRequest);
        DatagramReceiveResult result = await ReceiveWithTimeout(right, destination);

        Assert.True(result.HasDatagram);
        Assert.Equal(DatagramProtocolHint.Stun, result.Hint);
    }

    [Fact]
    public async Task ReceiveAsync_DoesNotClassifyMalformedStunLengthAsStun()
    {
        using Socket leftSocket = CreateBoundSocket();
        using Socket rightSocket = CreateBoundSocket();
        var leftEndPoint = (IPEndPoint)leftSocket.LocalEndPoint!;
        var rightEndPoint = (IPEndPoint)rightSocket.LocalEndPoint!;
        await using var left = new UdpDatagramPath(leftSocket, rightEndPoint);
        await using var right = new UdpDatagramPath(rightSocket, leftEndPoint);
        byte[] malformedStun =
        [
            0x00, 0x01, 0x00, 0x04,
            0x21, 0x12, 0xA4, 0x42,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0x0C
        ];
        byte[] destination = new byte[64];

        await left.SendAsync(malformedStun);
        DatagramReceiveResult result = await ReceiveWithTimeout(right, destination);

        Assert.True(result.HasDatagram);
        Assert.Equal(DatagramProtocolHint.Unknown, result.Hint);
    }

    [Fact]
    public async Task ReceiveAsync_ClassifiesDtlsDatagram()
    {
        using Socket leftSocket = CreateBoundSocket();
        using Socket rightSocket = CreateBoundSocket();
        var leftEndPoint = (IPEndPoint)leftSocket.LocalEndPoint!;
        var rightEndPoint = (IPEndPoint)rightSocket.LocalEndPoint!;
        await using var left = new UdpDatagramPath(leftSocket, rightEndPoint);
        await using var right = new UdpDatagramPath(rightSocket, leftEndPoint);
        byte[] dtlsRecord = [0x16, 0xFE, 0xFD, 0x00, 0x00];
        byte[] destination = new byte[64];

        await left.SendAsync(dtlsRecord);
        DatagramReceiveResult result = await ReceiveWithTimeout(right, destination);

        Assert.True(result.HasDatagram);
        Assert.Equal(DatagramProtocolHint.Dtls, result.Hint);
    }

    [Fact]
    public async Task ReadStateChangeAsync_ReportsReadyAndClosed()
    {
        using Socket leftSocket = CreateBoundSocket();
        using Socket rightSocket = CreateBoundSocket();
        var rightEndPoint = (IPEndPoint)rightSocket.LocalEndPoint!;
        var path = new UdpDatagramPath(leftSocket, rightEndPoint);

        PathStateChange? ready = await path.ReadStateChangeAsync();
        await path.DisposeAsync();
        PathStateChange? closed = await path.ReadStateChangeAsync();

        Assert.Equal(PathState.Ready, ready?.State);
        Assert.Equal(PathState.Closed, closed?.State);
        Assert.Equal(PathState.Closed, path.State);
    }

    [Fact]
    public async Task ReadStateChangeAsync_WaitsUntilNextStateChange()
    {
        using Socket leftSocket = CreateBoundSocket();
        using Socket rightSocket = CreateBoundSocket();
        var rightEndPoint = (IPEndPoint)rightSocket.LocalEndPoint!;
        var path = new UdpDatagramPath(leftSocket, rightEndPoint);

        _ = await path.ReadStateChangeAsync();
        ValueTask<PathStateChange?> pending = path.ReadStateChangeAsync();

        Assert.False(pending.IsCompleted);
        await path.DisposeAsync();
        PathStateChange? closed = await pending;

        Assert.NotNull(closed);
        Assert.Equal(PathState.Closed, closed.Value.State);
    }

    [Fact]
    public async Task ReadStateChangeAsync_CanceledReadDoesNotConsumeClosedState()
    {
        using Socket leftSocket = CreateBoundSocket();
        using Socket rightSocket = CreateBoundSocket();
        var rightEndPoint = (IPEndPoint)rightSocket.LocalEndPoint!;
        var path = new UdpDatagramPath(leftSocket, rightEndPoint);
        using var cancellation = new CancellationTokenSource();

        _ = await path.ReadStateChangeAsync();
        ValueTask<PathStateChange?> canceled = path.ReadStateChangeAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);

        ValueTask<PathStateChange?> pending = path.ReadStateChangeAsync();
        await path.DisposeAsync();
        PathStateChange? closed = await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(closed);
        Assert.Equal(PathState.Closed, closed.Value.State);
    }

    [Fact]
    public async Task ReceiveAsync_DiscardsUnexpectedRemoteWithoutCompletingPath()
    {
        using Socket expectedSocket = CreateBoundSocket();
        using Socket unexpectedSocket = CreateBoundSocket();
        using Socket receiverSocket = CreateBoundSocket();
        var expectedEndPoint = (IPEndPoint)expectedSocket.LocalEndPoint!;
        var receiverEndPoint = (IPEndPoint)receiverSocket.LocalEndPoint!;
        await using var receiver = new UdpDatagramPath(receiverSocket, expectedEndPoint);
        byte[] expectedPayload = [0x80, 0x60, 0x00, 0x02];

        _ = await unexpectedSocket.SendToAsync(new byte[] { 1, 2, 3 }, SocketFlags.None, receiverEndPoint);
        _ = await expectedSocket.SendToAsync(expectedPayload, SocketFlags.None, receiverEndPoint);
        byte[] destination = new byte[16];
        DatagramReceiveResult result = await ReceiveWithTimeout(receiver, destination);

        Assert.True(result.HasDatagram);
        Assert.False(result.IsCompleted);
        Assert.Equal(expectedPayload.Length, result.BytesWritten);
        Assert.Equal(expectedPayload, destination.AsSpan(0, result.BytesWritten).ToArray());
        Assert.Equal(expectedEndPoint, result.RemoteEndPoint);
    }

    [Fact]
    public async Task ReceiveAsync_PendingReceiveCompletesWhenPathIsDisposed()
    {
        using Socket expectedSocket = CreateBoundSocket();
        using Socket receiverSocket = CreateBoundSocket();
        var expectedEndPoint = (IPEndPoint)expectedSocket.LocalEndPoint!;
        var receiver = new UdpDatagramPath(receiverSocket, expectedEndPoint, ownsSocket: false);
        byte[] destination = new byte[16];

        ValueTask<DatagramReceiveResult> pending = receiver.ReceiveAsync(destination);

        Assert.False(pending.IsCompleted);
        await receiver.DisposeAsync();
        DatagramReceiveResult result = await pending;

        Assert.False(result.HasDatagram);
        Assert.True(result.IsCompleted);
        Assert.Equal(PathState.Closed, receiver.State);
    }

    [Fact]
    public async Task ReceiveAsync_PendingReceiveWithCallerTokenCompletesWhenPathIsDisposed()
    {
        using Socket expectedSocket = CreateBoundSocket();
        using Socket receiverSocket = CreateBoundSocket();
        var expectedEndPoint = (IPEndPoint)expectedSocket.LocalEndPoint!;
        var receiver = new UdpDatagramPath(receiverSocket, expectedEndPoint, ownsSocket: false);
        using var cancellation = new CancellationTokenSource();
        byte[] destination = new byte[16];

        ValueTask<DatagramReceiveResult> pending = receiver.ReceiveAsync(destination, cancellation.Token);

        Assert.False(pending.IsCompleted);
        await receiver.DisposeAsync();
        DatagramReceiveResult result = await pending;

        Assert.False(result.HasDatagram);
        Assert.True(result.IsCompleted);
        Assert.False(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task ReceiveAsync_ExternalCancellationStillPropagatesCancellation()
    {
        using Socket expectedSocket = CreateBoundSocket();
        using Socket receiverSocket = CreateBoundSocket();
        var expectedEndPoint = (IPEndPoint)expectedSocket.LocalEndPoint!;
        await using var receiver = new UdpDatagramPath(receiverSocket, expectedEndPoint);
        using var cancellation = new CancellationTokenSource();
        byte[] destination = new byte[16];

        ValueTask<DatagramReceiveResult> pending = receiver.ReceiveAsync(destination, cancellation.Token);

        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task ReceiveAsync_RejectsEmptyDestinationBeforePostingReceive()
    {
        using Socket expectedSocket = CreateBoundSocket();
        using Socket receiverSocket = CreateBoundSocket();
        var expectedEndPoint = (IPEndPoint)expectedSocket.LocalEndPoint!;
        await using var receiver = new UdpDatagramPath(receiverSocket, expectedEndPoint);

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await receiver.ReceiveAsync(Memory<byte>.Empty));

        Assert.Equal("destination", exception.ParamName);
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenPathIsDisposed()
    {
        using Socket leftSocket = CreateBoundSocket();
        using Socket rightSocket = CreateBoundSocket();
        var rightEndPoint = (IPEndPoint)rightSocket.LocalEndPoint!;
        var path = new UdpDatagramPath(leftSocket, rightEndPoint, ownsSocket: false);

        await path.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await path.SendAsync(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Constructor_RejectsRemoteEndPointWithMismatchedAddressFamily()
    {
        using Socket socket = CreateBoundSocket();
        var remoteEndPoint = new IPEndPoint(IPAddress.IPv6Loopback, 12345);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new UdpDatagramPath(socket, remoteEndPoint, ownsSocket: false));

        Assert.Equal("remoteEndPoint", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNonUdpDatagramSocket()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 12345);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new UdpDatagramPath(socket, remoteEndPoint, ownsSocket: false));

        Assert.Equal("socket", exception.ParamName);
    }

    private static Socket CreateBoundSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket;
    }

    private static async ValueTask<DatagramReceiveResult> ReceiveWithTimeout(
        IDatagramPath path,
        Memory<byte> destination)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await path.ReceiveAsync(destination, timeout.Token);
    }
}
