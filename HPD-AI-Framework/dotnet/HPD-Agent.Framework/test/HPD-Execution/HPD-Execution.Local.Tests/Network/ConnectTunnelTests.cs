using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using HPD.Execution.Local.Network;
using Xunit;

namespace HPD.Execution.Local.Tests.Network;

public sealed class ConnectTunnelTests
{
    [Fact]
    public async Task OpenAsync_SendsConnectRequestAndReturnsTunnel()
    {
        await using var proxy = await FakeConnectProxy.StartAsync("HTTP/1.1 200 Connection Established\r\n\r\n");

        await using var tunnel = await ConnectTunnel.OpenAsync(
            proxy.DialAsync,
            "example.com",
            443,
            null,
            CancellationToken.None);

        proxy.Request.Should().StartWith("CONNECT example.com:443 HTTP/1.1\r\n");
        proxy.Request.Should().Contain("Host: example.com:443\r\n");
    }

    [Fact]
    public async Task OpenAsync_IncludesProxyAuthorization()
    {
        await using var proxy = await FakeConnectProxy.StartAsync("HTTP/1.1 200 OK\r\n\r\n");

        await using var tunnel = await ConnectTunnel.OpenAsync(
            proxy.DialAsync,
            "example.com",
            443,
            "Basic token",
            CancellationToken.None);

        proxy.Request.Should().Contain("Proxy-Authorization: Basic token\r\n");
    }

    [Fact]
    public async Task OpenAsync_PreservesBytesAfterConnectHeaders()
    {
        await using var proxy = await FakeConnectProxy.StartAsync("HTTP/1.1 200 OK\r\n\r\nhello");

        await using var tunnel = await ConnectTunnel.OpenAsync(
            proxy.DialAsync,
            "example.com",
            443,
            null,
            CancellationToken.None);

        var buffer = new byte[5];
        var read = await tunnel.ReadAsync(buffer);

        read.Should().Be(5);
        Encoding.ASCII.GetString(buffer).Should().Be("hello");
    }

    [Fact]
    public async Task OpenAsync_Non2xxResponse_Throws()
    {
        await using var proxy = await FakeConnectProxy.StartAsync("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n");

        var act = async () => await ConnectTunnel.OpenAsync(
            proxy.DialAsync,
            "example.com",
            443,
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*407*");
    }

    [Fact]
    public async Task OpenAsync_ResponseHeadersExceedLimit_Throws()
    {
        var oversized = "HTTP/1.1 200 OK\r\nX-Big: " + new string('a', 17 * 1024);
        await using var proxy = await FakeConnectProxy.StartAsync(oversized);

        var act = async () => await ConnectTunnel.OpenAsync(
            proxy.DialAsync,
            "example.com",
            443,
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*headers exceeded*");
    }

    [Fact]
    public async Task OpenAsync_HandshakeTimeout_Cancels()
    {
        await using var proxy = await FakeConnectProxy.StartAsync(
            "HTTP/1.1 200 OK\r\n\r\n",
            responseDelay: TimeSpan.FromMilliseconds(250));

        var act = async () => await ConnectTunnel.OpenAsync(
            proxy.DialAsync,
            "example.com",
            443,
            null,
            CancellationToken.None,
            handshakeTimeout: TimeSpan.FromMilliseconds(25));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task OpenAsync_RejectsCrlfDestination()
    {
        await using var proxy = await FakeConnectProxy.StartAsync("HTTP/1.1 200 OK\r\n\r\n");

        var act = async () => await ConnectTunnel.OpenAsync(
            proxy.DialAsync,
            "example.com\r\nInjected: yes",
            443,
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void BuildProxyAuthorization_UsesBasicAuthForUserInfo()
    {
        var proxyUri = new Uri("http://user:secret@proxy.corp:8080");

        var header = ConnectTunnel.BuildProxyAuthorization(proxyUri);

        header.Should().Be($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("user:secret"))}");
    }

    private sealed class FakeConnectProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _response;
        private readonly TimeSpan _responseDelay;
        private readonly Task _acceptTask;

        private FakeConnectProxy(TcpListener listener, string response, TimeSpan responseDelay)
        {
            _listener = listener;
            _response = response;
            _responseDelay = responseDelay;
            _acceptTask = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Request { get; private set; } = string.Empty;

        public static Task<FakeConnectProxy> StartAsync(string response, TimeSpan? responseDelay = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new FakeConnectProxy(
                listener,
                response,
                responseDelay ?? TimeSpan.Zero));
        }

        public async Task<Stream> DialAsync(CancellationToken cancellationToken)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, Port, cancellationToken);
            return client.GetStream();
        }

        private async Task AcceptAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            var buffer = new byte[4096];
            var read = 0;
            while (!Request.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read));
                if (n == 0)
                    break;
                read += n;
                Request = Encoding.ASCII.GetString(buffer, 0, read);
            }

            if (_responseDelay > TimeSpan.Zero)
                await Task.Delay(_responseDelay);

            var responseBytes = Encoding.ASCII.GetBytes(_response);
            await stream.WriteAsync(responseBytes);
            await stream.FlushAsync();

            // Keep the proxy side alive briefly so the client can read any
            // extra bytes after headers before disposal closes the socket.
            await Task.Delay(100);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _acceptTask;
            }
            catch
            {
                // Listener may be stopped before a test dials it.
            }
        }
    }
}
