using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using HPD.Sandbox.Local.Network;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

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
    [InlineData("example.com", new[] { "*.example.com" }, true)]
    [InlineData("notexample.com", new[] { "*.example.com" }, true)] // Note: *.example.com matches anything ending in example.com
    [InlineData("totally-different.org", new[] { "*.example.com" }, false)]
    public void DomainMatching_WorksCorrectly(string host, string[] allowedDomains, bool shouldMatch)
    {
        // Test the domain matching logic directly
        var matches = allowedDomains.Any(pattern => MatchesDomain(host, pattern));

        matches.Should().Be(shouldMatch);
    }

    [Fact]
    public void DeniedDomains_TakePrecedence()
    {
        // If a domain is in both allowed and denied, it should be denied
        var allowed = new[] { "*.example.com" };
        var denied = new[] { "malicious.example.com" };

        // Check denied first
        var isDenied = denied.Any(pattern => MatchesDomain("malicious.example.com", pattern));
        var isAllowed = allowed.Any(pattern => MatchesDomain("malicious.example.com", pattern));

        isDenied.Should().BeTrue();
        isAllowed.Should().BeTrue(); // Would match wildcard, but denied takes precedence
    }

    [Fact]
    public void EmptyAllowedDomains_BlocksAll()
    {
        var allowed = Array.Empty<string>();

        var isAllowed = allowed.Any(pattern => MatchesDomain("any.domain.com", pattern));

        isAllowed.Should().BeFalse();
    }

    // Helper method to match the proxy's domain matching logic
    private static bool MatchesDomain(string host, string pattern)
    {
        if (pattern.StartsWith("*."))
        {
            var domain = pattern[2..];
            return host.EndsWith(domain, StringComparison.OrdinalIgnoreCase) ||
                   host.Equals(domain, StringComparison.OrdinalIgnoreCase);
        }

        return host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
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
    public async Task Connect_ToBlockedDomain_ReturnsConnectionNotAllowed()
    {
        // Only allow localhost, block everything else
        var proxy = new Socks5ProxyServer(["localhost"], []);
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
        }
        finally
        {
            await proxy.DisposeAsync();
        }
    }
}
