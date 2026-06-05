using System.Net;
using System.Net.Sockets;
using System.Text;
using HPD.Execution.Contracts;
using FluentAssertions;
using HPD.Agent.Sandbox.Network;
using HPD.Agent.Sandbox.Policy;
using Xunit;

namespace HPD.Agent.Sandbox.Tests;

public class Socks5ProxyServerTests
{
    [Fact]
    public async Task StartAsync_ReturnsPort()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);

        var port = await proxy.StartAsync();

        try
        {
            port.Should().BeGreaterThan(0);
            port.Should().BeLessThan(65536);
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task StartAsync_PortPropertyMatchesReturnedPort()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);

        var returnedPort = await proxy.StartAsync();

        try
        {
            proxy.Port.Should().Be(returnedPort);
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task StartAsync_ListensOnLocalhost()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);
        await proxy.StartAsync();

        try
        {
            // Should be able to connect to the proxy
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(1000));

            completed.Should().Be(connectTask, "should connect within timeout");
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_StopsListening()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);
        var port = await proxy.StartAsync();

        await proxy.DisposeAsync();

        // Connection should fail after dispose
        using var client = new TcpClient();
        var act = async () => await client.ConnectAsync(IPAddress.Loopback, port);

        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);
        await proxy.StartAsync();

        await proxy.DisposeAsync();
        var act = async () => await proxy.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Port_IsZeroBeforeStart()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);

        proxy.Port.Should().Be(0);
    }
}

public class Socks5DomainFilteringTests
{
    [Theory]
    [InlineData("example.com", new[] { "example.com" }, true)]
    [InlineData("example.com", new[] { "other.com" }, false)]
    [InlineData("api.example.com", new[] { "*.example.com" }, true)]
    [InlineData("deep.api.example.com", new[] { "*.example.com" }, true)]
    [InlineData("example.com", new[] { "*.example.com" }, false)]
    [InlineData("notexample.com", new[] { "*.example.com" }, false)]
    [InlineData("totally-different.org", new[] { "*.example.com" }, false)]
    public void DomainMatching_WorksCorrectly(string host, string[] allowedDomains, bool shouldMatch)
    {
        var policy = NetworkPolicy.Filtered(
            allowedDomains.Select(DomainPattern.Parse).ToArray());
        var evaluator = new NetworkPolicyEvaluator(policy);

        evaluator.Evaluate(host).Kind.Should().Be(
            shouldMatch ? NetworkPolicyDecisionKind.Allow : NetworkPolicyDecisionKind.Deny);
    }

    [Fact]
    public void DeniedDomains_TakePrecedence()
    {
        // If a domain is in both allowed and denied, it should be denied
        var allowed = new[] { "*.example.com" };
        var denied = new[] { "malicious.example.com" };

        var policy = NetworkPolicy.Filtered(
            allowed.Select(DomainPattern.Parse).ToArray(),
            denied.Select(DomainPattern.Parse).ToArray());
        var evaluator = new NetworkPolicyEvaluator(policy);

        evaluator.Evaluate("malicious.example.com").Kind.Should().Be(NetworkPolicyDecisionKind.Deny);
    }

    [Fact]
    public void EmptyAllowedDomains_BlocksAll()
    {
        var evaluator = new NetworkPolicyEvaluator(NetworkPolicy.Blocked);

        evaluator.Evaluate("any.domain.com").Kind.Should().Be(NetworkPolicyDecisionKind.Deny);
    }
}

public class Socks5ProtocolTests
{
    [Fact]
    public async Task Server_RespondsToAuthenticationNegotiation()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);
        await proxy.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);

            await using var stream = client.GetStream();

            // Send SOCKS5 greeting: VER=5, NMETHODS=1, METHOD=0 (no auth)
            var greeting = new byte[] { 0x05, 0x01, 0x00 };
            await stream.WriteAsync(greeting);

            // Read response
            var response = new byte[2];
            var bytesRead = await stream.ReadAsync(response);

            bytesRead.Should().Be(2);
            response[0].Should().Be(0x05, "should be SOCKS5 version");
            response[1].Should().Be(0x00, "should accept no-auth method");
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task Server_RejectsUnsupportedAuthMethods()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);
        await proxy.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);

            await using var stream = client.GetStream();

            // Send SOCKS5 greeting with only username/password auth (method 0x02)
            var greeting = new byte[] { 0x05, 0x01, 0x02 };
            await stream.WriteAsync(greeting);

            // Read response
            var response = new byte[2];
            var bytesRead = await stream.ReadAsync(response);

            bytesRead.Should().Be(2);
            response[0].Should().Be(0x05, "should be SOCKS5 version");
            response[1].Should().Be(0xFF, "should reject with 0xFF (no acceptable methods)");
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task Server_RejectsInvalidVersion()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);
        await proxy.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);

            await using var stream = client.GetStream();

            // Send SOCKS4 greeting (wrong version)
            var greeting = new byte[] { 0x04, 0x01, 0x00 };
            await stream.WriteAsync(greeting);

            // Server should close connection or not respond properly
            var response = new byte[2];
            var readTask = stream.ReadAsync(response).AsTask();
            var completed = await Task.WhenAny(readTask, Task.Delay(500));

            // Either no response or connection closed
            if (completed == readTask)
            {
                var bytesRead = await readTask;
                // If we got a response, it should indicate rejection
                if (bytesRead == 2)
                {
                    response[0].Should().NotBe(0x04, "should not accept SOCKS4");
                }
            }
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }
}

public class Socks5ConnectionTests
{
    [Fact]
    public async Task Connect_AllowedDomain_UsesParentProxyConnectTunnel()
    {
        await using var parentProxy = await FakeParentProxy.StartAsync();
        var proxy = new Socks5ProxyServer(
            ["example.com"],
            [],
            new ParentProxyPolicy { ProxyUri = new Uri($"http://127.0.0.1:{parentProxy.Port}") });
        await proxy.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            await using var stream = client.GetStream();

            await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 });
            var authResponse = new byte[2];
            await stream.ReadAsync(authResponse);

            var domain = "example.com"u8.ToArray();
            var request = new byte[4 + 1 + domain.Length + 2];
            request[0] = 0x05;
            request[1] = 0x01;
            request[2] = 0x00;
            request[3] = 0x03;
            request[4] = (byte)domain.Length;
            Array.Copy(domain, 0, request, 5, domain.Length);
            request[^2] = 0x01;
            request[^1] = 0xbb; // 443

            await stream.WriteAsync(request);

            var response = new byte[10];
            var bytesRead = await stream.ReadAsync(response);

            bytesRead.Should().BeGreaterThanOrEqualTo(2);
            response[0].Should().Be(0x05);
            response[1].Should().Be(0x00);
            parentProxy.Request.Should().StartWith("CONNECT example.com:443 HTTP/1.1");
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task Connect_ToBlockedDomain_ReturnsConnectionNotAllowed()
    {
        // Only allow localhost, block everything else
        var events = new List<ProcessIsolationProxyEvent>();
        var proxy = new Socks5ProxyServer(
            ["localhost"],
            [],
            eventSink: events.Add);
        await proxy.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);

            await using var stream = client.GetStream();

            // Auth negotiation
            await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 });
            var authResponse = new byte[2];
            await stream.ReadAsync(authResponse);

            // Connect request to blocked domain
            // VER=5, CMD=1 (CONNECT), RSV=0, ATYP=3 (domain), len=11, "example.com", port=80
            var domain = "example.com"u8.ToArray();
            var request = new byte[4 + 1 + domain.Length + 2];
            request[0] = 0x05; // VER
            request[1] = 0x01; // CMD CONNECT
            request[2] = 0x00; // RSV
            request[3] = 0x03; // ATYP domain
            request[4] = (byte)domain.Length;
            Array.Copy(domain, 0, request, 5, domain.Length);
            request[^2] = 0x00; // Port high byte
            request[^1] = 0x50; // Port low byte (80)

            await stream.WriteAsync(request);

            // Read response
            var response = new byte[10];
            var bytesRead = await stream.ReadAsync(response);

            bytesRead.Should().BeGreaterThanOrEqualTo(2);
            response[0].Should().Be(0x05, "should be SOCKS5 version");
            response[1].Should().Be(0x02, "should be connection not allowed (0x02)");
            events.Should().ContainSingle();
            events[0].Protocol.Should().Be(ProcessIsolationProxyProtocol.Socks5);
            events[0].Kind.Should().Be(ProcessIsolationProxyEventKind.NetworkPolicyDenied);
            events[0].Host.Should().Be("example.com");
            events[0].Port.Should().Be(80);
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task Connect_ToMalformedDomain_ReturnsConnectionNotAllowedBeforeDialing()
    {
        var proxy = new Socks5ProxyServer(["*.example.com"], []);
        await proxy.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);

            await using var stream = client.GetStream();

            await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 });
            var authResponse = new byte[2];
            await stream.ReadAsync(authResponse);

            var domain = "evil.com\0.example.com"u8.ToArray();
            var request = new byte[4 + 1 + domain.Length + 2];
            request[0] = 0x05;
            request[1] = 0x01;
            request[2] = 0x00;
            request[3] = 0x03;
            request[4] = (byte)domain.Length;
            Array.Copy(domain, 0, request, 5, domain.Length);
            request[^2] = 0x00;
            request[^1] = 0x50;

            await stream.WriteAsync(request);

            var response = new byte[10];
            var bytesRead = await stream.ReadAsync(response);

            bytesRead.Should().BeGreaterThanOrEqualTo(2);
            response[0].Should().Be(0x05);
            response[1].Should().Be(0x02);
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task Connect_RequestCanArriveOneByteAtATime()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);
        await proxy.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);

            await using var stream = client.GetStream();

            foreach (var b in new byte[] { 0x05, 0x01, 0x00 })
            {
                await stream.WriteAsync(new[] { b });
            }

            var authResponse = new byte[2];
            await stream.ReadAsync(authResponse);
            authResponse.Should().Equal([0x05, 0x00]);

            var domain = "example.com"u8.ToArray();
            var request = new byte[4 + 1 + domain.Length + 2];
            request[0] = 0x05;
            request[1] = 0x01;
            request[2] = 0x00;
            request[3] = 0x03;
            request[4] = (byte)domain.Length;
            Array.Copy(domain, 0, request, 5, domain.Length);
            request[^2] = 0x00;
            request[^1] = 0x50;

            foreach (var b in request)
            {
                await stream.WriteAsync(new[] { b });
            }

            var response = new byte[10];
            var bytesRead = await stream.ReadAsync(response);

            bytesRead.Should().BeGreaterThanOrEqualTo(2);
            response[0].Should().Be(0x05);
            response[1].Should().Be(0x02);
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task Connect_UnsupportedCommand_ReturnsCommandNotSupported()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);
        await proxy.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            await using var stream = client.GetStream();

            await SendNoAuthGreetingAsync(stream);

            var domain = "localhost"u8.ToArray();
            var request = new byte[4 + 1 + domain.Length + 2];
            request[0] = 0x05;
            request[1] = 0x02; // BIND is unsupported.
            request[2] = 0x00;
            request[3] = 0x03;
            request[4] = (byte)domain.Length;
            Array.Copy(domain, 0, request, 5, domain.Length);
            request[^2] = 0x00;
            request[^1] = 0x50;

            await stream.WriteAsync(request);

            var response = new byte[10];
            var bytesRead = await stream.ReadAsync(response);

            bytesRead.Should().BeGreaterThanOrEqualTo(2);
            response[0].Should().Be(0x05);
            response[1].Should().Be(0x07, "unsupported SOCKS5 commands should return command not supported");
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    [Fact]
    public async Task Connect_UnsupportedAddressType_ReturnsAddressTypeNotSupported()
    {
        var proxy = new Socks5ProxyServer(["localhost"], []);
        await proxy.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            await using var stream = client.GetStream();

            await SendNoAuthGreetingAsync(stream);

            await stream.WriteAsync(new byte[]
            {
                0x05,
                0x01,
                0x00,
                0x09 // Invalid ATYP.
            });

            var response = new byte[10];
            var bytesRead = await stream.ReadAsync(response);

            bytesRead.Should().BeGreaterThanOrEqualTo(2);
            response[0].Should().Be(0x05);
            response[1].Should().Be(0x08, "unsupported SOCKS5 address types should return address type not supported");
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }

    private static async Task SendNoAuthGreetingAsync(NetworkStream stream)
    {
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 });
        var authResponse = new byte[2];
        var bytesRead = await stream.ReadAsync(authResponse);
        bytesRead.Should().Be(2);
        authResponse.Should().Equal([0x05, 0x00]);
    }

    private sealed class FakeParentProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _acceptTask;

        private FakeParentProxy(TcpListener listener)
        {
            _listener = listener;
            _acceptTask = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Request { get; private set; } = string.Empty;

        public static Task<FakeParentProxy> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new FakeParentProxy(listener));
        }

        private async Task AcceptAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            var buffer = new byte[2048];
            var read = 0;
            while (!Request.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read));
                if (n == 0)
                    break;
                read += n;
                Request = Encoding.ASCII.GetString(buffer, 0, read);
            }

            await stream.WriteAsync("HTTP/1.1 200 OK\r\n\r\n"u8.ToArray());
            await stream.FlushAsync();
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
                // Listener may be stopped before a test connects.
            }
        }
    }
}
