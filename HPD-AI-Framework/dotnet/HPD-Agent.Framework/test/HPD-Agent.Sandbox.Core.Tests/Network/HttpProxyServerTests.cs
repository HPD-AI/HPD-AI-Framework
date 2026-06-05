using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using HPD.Execution.Contracts;
using HPD.Agent.Sandbox.Network;
using Xunit;

namespace HPD.Agent.Sandbox.Tests.Network;

public sealed class HttpProxyServerTests
{
    [Fact]
    public async Task PlainHttp_ToBlockedDomain_ReturnsForbidden()
    {
        var events = new List<ProcessIsolationProxyEvent>();
        await using var proxy = new HttpProxyServer(
            ["allowed.example"],
            [],
            eventSink: events.Add);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "GET http://blocked.example/ HTTP/1.1\r\nHost: blocked.example\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("403 Forbidden");
        response.Should().Contain("X-Proxy-Error: sandbox_denied");
        events.Should().ContainSingle();
        events[0].Protocol.Should().Be(ProcessIsolationProxyProtocol.Http);
        events[0].Kind.Should().Be(ProcessIsolationProxyEventKind.NetworkPolicyDenied);
        events[0].Host.Should().Be("blocked.example");
        events[0].Method.Should().Be("GET");
        events[0].Uri.Should().Be(new Uri("http://blocked.example/"));
    }

    [Fact]
    public async Task PlainHttp_ToAllowedDomain_ForwardsOriginFormRequest()
    {
        await using var origin = await FakeHttpEndpoint.StartAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK");
        await using var proxy = new HttpProxyServer(["127.0.0.1"], []);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET http://127.0.0.1:{origin.Port}/path?q=1 HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\nProxy-Connection: keep-alive\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("200 OK");
        origin.Request.Should().StartWith("GET /path?q=1 HTTP/1.1");
        origin.Request.Should().NotContain("Proxy-Connection");
    }

    [Fact]
    public async Task PlainHttp_StripsHopByHopHeadersBeforeForwarding()
    {
        await using var origin = await FakeHttpEndpoint.StartAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK");
        await using var proxy = new HttpProxyServer(["127.0.0.1"], []);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET http://127.0.0.1:{origin.Port}/headers HTTP/1.1\r\n" +
            $"Host: 127.0.0.1:{origin.Port}\r\n" +
            "Connection: keep-alive\r\n" +
            "Proxy-Connection: keep-alive\r\n" +
            "Proxy-Authorization: Basic dGVzdA==\r\n" +
            "Keep-Alive: timeout=5\r\n" +
            "TE: trailers\r\n" +
            "Trailer: X-Trailer\r\n" +
            "Upgrade: websocket\r\n" +
            "X-Keep: yes\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("200 OK");
        origin.Request.Should().Contain("X-Keep: yes");
        origin.Request.Should().NotContain("Connection:");
        origin.Request.Should().NotContain("Proxy-Connection:");
        origin.Request.Should().NotContain("Proxy-Authorization:");
        origin.Request.Should().NotContain("Keep-Alive:");
        origin.Request.Should().NotContain("TE:");
        origin.Request.Should().NotContain("Trailer:");
        origin.Request.Should().NotContain("Upgrade:");
    }

    [Fact]
    public async Task PlainHttp_PostContentLength_StreamsFullBodyAfterHeaders()
    {
        await using var origin = await FakeHttpEndpoint.StartAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK");
        await using var proxy = new HttpProxyServer(["127.0.0.1"], []);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        var headers = Encoding.ASCII.GetBytes(
            $"POST http://127.0.0.1:{origin.Port}/submit HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\nContent-Length: 11\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.FlushAsync();
        await Task.Delay(50);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("hello world"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("200 OK");
        origin.Request.Should().Contain("\r\n\r\nhello world");
    }

    [Fact]
    public async Task PlainHttp_WithTransferEncoding_ReturnsBadRequest()
    {
        await using var proxy = new HttpProxyServer(["example.com"], []);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "POST http://example.com/upload HTTP/1.1\r\nHost: example.com\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("400 Bad Request");
        response.Should().Contain("Transfer-Encoding is not supported");
        response.Should().Contain("X-Proxy-Error: malformed_request");
    }

    [Fact]
    public async Task MalformedRequestLine_ReturnsBadRequest()
    {
        await using var proxy = new HttpProxyServer(["example.com"], []);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET\r\nHost: example.com\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("400 Bad Request");
        response.Should().Contain("Malformed HTTP request line");
        response.Should().Contain("X-Proxy-Error: malformed_request");
    }

    [Fact]
    public async Task MalformedHeader_ReturnsBadRequest()
    {
        await using var proxy = new HttpProxyServer(["example.com"], []);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "GET http://example.com/ HTTP/1.1\r\nBrokenHeader\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("400 Bad Request");
        response.Should().Contain("Malformed HTTP header");
    }

    [Fact]
    public async Task OversizedHeaders_ReturnsHeaderTooLarge()
    {
        await using var proxy = new HttpProxyServer(["example.com"], []);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        var oversizedHeader = new string('a', 17 * 1024);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET http://example.com/ HTTP/1.1\r\nX-Big: {oversizedHeader}\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("431 Request Header Fields Too Large");
        response.Should().Contain("X-Proxy-Error: malformed_request");
    }

    [Fact]
    public async Task PlainHttp_WithParentProxy_ForwardsAbsoluteFormToParent()
    {
        await using var parent = await FakeHttpEndpoint.StartAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK");
        await using var proxy = new HttpProxyServer(
            ["example.com"],
            [],
            new ParentProxyPolicy { ProxyUri = new Uri($"http://127.0.0.1:{parent.Port}") });
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "GET http://example.com/path HTTP/1.1\r\nHost: example.com\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("200 OK");
        parent.Request.Should().StartWith("GET http://example.com/path HTTP/1.1");
    }

    [Fact]
    public async Task PlainHttp_RequestFilterAllow_ForwardsRequest()
    {
        await using var origin = await FakeHttpEndpoint.StartAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK");
        await using var proxy = new HttpProxyServer(
            ["127.0.0.1"],
            []);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET http://127.0.0.1:{origin.Port}/allowed HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("200 OK");
        origin.Request.Should().StartWith("GET /allowed HTTP/1.1");
    }

    [Fact]
    public async Task MalformedRequestLine_EmitsProxyEvent()
    {
        var events = new List<ProcessIsolationProxyEvent>();
        await using var proxy = new HttpProxyServer(
            ["example.com"],
            [],
            eventSink: events.Add);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET\r\nHost: example.com\r\n\r\n"));

        _ = await ReadAvailableResponseAsync(stream);

        events.Should().ContainSingle();
        events[0].Protocol.Should().Be(ProcessIsolationProxyProtocol.Http);
        events[0].Kind.Should().Be(ProcessIsolationProxyEventKind.MalformedRequest);
        events[0].Reason.Should().Be("Malformed HTTP request line.");
    }

    [Fact]
    public async Task Connect_WithParentProxy_UsesConnectTunnel()
    {
        await using var parent = await FakeHttpEndpoint.StartAsync("HTTP/1.1 200 OK\r\n\r\n");
        await using var proxy = new HttpProxyServer(
            ["example.com"],
            [],
            new ParentProxyPolicy { ProxyUri = new Uri($"http://127.0.0.1:{parent.Port}") });
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("200 Connection Established");
        parent.Request.Should().StartWith("CONNECT example.com:443 HTTP/1.1");
    }

    [Fact]
    public async Task Connect_WithParentProxyNonSuccess_ReturnsBadGateway()
    {
        await using var parent = await FakeHttpEndpoint.StartAsync("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n");
        await using var proxy = new HttpProxyServer(
            ["example.com"],
            [],
            new ParentProxyPolicy { ProxyUri = new Uri($"http://127.0.0.1:{parent.Port}") });
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(stream);

        response.Should().Contain("502 Bad Gateway");
        response.Should().Contain("X-Proxy-Error: upstream_failure");
        response.Should().Contain("407 Proxy Authentication Required");
    }

    [Fact]
    public async Task Connect_WithTlsTermination_PostContentLength_StreamsBodyToTlsOrigin()
    {
        await using var origin = await FakeTlsEndpoint.StartAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
        await using var authority = await MitmCertificateAuthority.CreateEphemeralAsync();
        await using var proxy = new HttpProxyServer(
            ["127.0.0.1"],
            [],
            tlsIssuerCertificate: authority.Certificate,
            upstreamCertificateValidationCallback: (_, _, _, _) => true);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"CONNECT 127.0.0.1:{origin.Port} HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n"));
        _ = await ReadAvailableResponseAsync(stream);

        await using var tls = new SslStream(
            stream,
            leaveInnerStreamOpen: true,
            (_, _, _, _) => true);
        await tls.AuthenticateAsClientAsync("127.0.0.1");

        var headers = Encoding.ASCII.GetBytes(
            "POST /submit HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 11\r\n\r\n");
        await tls.WriteAsync(headers);
        await tls.FlushAsync();
        await Task.Delay(50);
        await tls.WriteAsync(Encoding.ASCII.GetBytes("hello world"));

        var response = await ReadAvailableResponseAsync(tls);

        response.Should().Contain("200 OK");
        origin.Request.Should().StartWith("POST /submit HTTP/1.1");
        origin.Request.Should().Contain("\r\n\r\nhello world");
    }

    [Fact]
    public async Task Connect_WithTlsTerminationAndParentProxy_TunnelsUpstreamThroughParent()
    {
        await using var origin = await FakeTlsEndpoint.StartAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
        await using var parent = await FakeTunnelingParentProxy.StartAsync();
        await using var authority = await MitmCertificateAuthority.CreateEphemeralAsync();
        await using var proxy = new HttpProxyServer(
            ["upstream.test"],
            [],
            new ParentProxyPolicy { ProxyUri = new Uri($"http://127.0.0.1:{parent.Port}") },
            tlsIssuerCertificate: authority.Certificate,
            upstreamCertificateValidationCallback: (_, _, _, _) => true);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"CONNECT upstream.test:{origin.Port} HTTP/1.1\r\nHost: upstream.test:{origin.Port}\r\n\r\n"));
        var connectResponse = await ReadAvailableResponseAsync(stream);
        connectResponse.Should().Contain("200 Connection Established");

        await using var tls = new SslStream(
            stream,
            leaveInnerStreamOpen: true,
            (_, _, _, _) => true);
        await tls.AuthenticateAsClientAsync("upstream.test");
        await tls.WriteAsync(Encoding.ASCII.GetBytes(
            "GET /via-parent HTTP/1.1\r\nHost: upstream.test\r\n\r\n"));

        var response = await ReadAvailableResponseAsync(tls);

        response.Should().Contain("200 OK");
        parent.Request.Should().StartWith($"CONNECT upstream.test:{origin.Port} HTTP/1.1");
        origin.Request.Should().StartWith("GET /via-parent HTTP/1.1");
    }

    [Fact]
    public async Task Connect_WithExternalMitmUnixSocket_TunnelsThroughExternalProxy()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var externalMitm = await FakeExternalMitmUnixProxy.StartAsync();
        await using var proxy = new HttpProxyServer(
            ["example.com"],
            [],
            externalMitmUnixSocketPath: externalMitm.SocketPath);
        await proxy.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n"));
        var connectResponse = await ReadAvailableResponseAsync(stream);
        connectResponse.Should().Contain("200 Connection Established");

        await stream.WriteAsync("hello external mitm"u8.ToArray());
        var response = await ReadAvailableResponseAsync(stream);

        externalMitm.Request.Should().StartWith("CONNECT example.com:443 HTTP/1.1");
        externalMitm.Payload.Should().Be("hello external mitm");
        response.Should().Be("external mitm ok");
    }

    private static async Task<string> ReadAvailableResponseAsync(Stream stream)
    {
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var read = await stream.ReadAsync(buffer, cts.Token);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    private sealed class FakeHttpEndpoint : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _response;
        private readonly Task _acceptTask;

        private FakeHttpEndpoint(TcpListener listener, string response)
        {
            _listener = listener;
            _response = response;
            _acceptTask = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Request { get; private set; } = string.Empty;

        public static Task<FakeHttpEndpoint> StartAsync(string response)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new FakeHttpEndpoint(listener, response));
        }

        private async Task AcceptAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = 0;
            while (!Request.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read));
                if (n == 0)
                    break;
                read += n;
                Request = Encoding.ASCII.GetString(buffer, 0, read);
            }

            var contentLength = TryGetContentLength(Request);
            if (contentLength > 0)
            {
                var headerEnd = Request.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
                var bodyBytesRead = read - headerEnd;
                while (bodyBytesRead < contentLength)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(read));
                    if (n == 0)
                        break;
                    read += n;
                    bodyBytesRead += n;
                    Request = Encoding.ASCII.GetString(buffer, 0, read);
                }
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(_response));
            await stream.FlushAsync();
            await Task.Delay(100);
        }

        public static int TryGetContentLength(string request)
        {
            var headerEnd = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0)
                return 0;

            var headers = request[..headerEnd].Split("\r\n");
            foreach (var header in headers)
            {
                if (!header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    continue;

                return int.TryParse(header["Content-Length:".Length..].Trim(), out var contentLength)
                    ? contentLength
                    : 0;
            }

            return 0;
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

    private sealed class FakeTlsEndpoint : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _response;
        private readonly X509Certificate2 _certificate;
        private readonly Task _acceptTask;

        private FakeTlsEndpoint(TcpListener listener, string response, X509Certificate2 certificate)
        {
            _listener = listener;
            _response = response;
            _certificate = certificate;
            _acceptTask = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Request { get; private set; } = string.Empty;

        public static Task<FakeTlsEndpoint> StartAsync(string response)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new FakeTlsEndpoint(
                listener,
                response,
                CreateCertificate()));
        }

        private async Task AcceptAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var network = client.GetStream();
            await using var tls = new SslStream(network, leaveInnerStreamOpen: false);
            await tls.AuthenticateAsServerAsync(_certificate);

            var buffer = new byte[8192];
            var read = 0;
            while (!Request.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var n = await tls.ReadAsync(buffer.AsMemory(read));
                if (n == 0)
                    break;
                read += n;
                Request = Encoding.ASCII.GetString(buffer, 0, read);
            }

            var contentLength = FakeHttpEndpoint.TryGetContentLength(Request);
            if (contentLength > 0)
            {
                var headerEnd = Request.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
                var bodyBytesRead = read - headerEnd;
                while (bodyBytesRead < contentLength)
                {
                    var n = await tls.ReadAsync(buffer.AsMemory(read));
                    if (n == 0)
                        break;
                    read += n;
                    bodyBytesRead += n;
                    Request = Encoding.ASCII.GetString(buffer, 0, read);
                }
            }

            await tls.WriteAsync(Encoding.ASCII.GetBytes(_response));
            await tls.FlushAsync();
        }

        private static X509Certificate2 CreateCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=127.0.0.1",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));
            var sans = new SubjectAlternativeNameBuilder();
            sans.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(sans.Build());
            var now = DateTimeOffset.UtcNow;
            using var certificate = request.CreateSelfSigned(
                now.AddMinutes(-5),
                now.AddDays(1));
            return new X509Certificate2(
                certificate.Export(X509ContentType.Pfx),
                (string?)null,
                X509KeyStorageFlags.Exportable);
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

            _certificate.Dispose();
        }
    }

    private sealed class FakeTunnelingParentProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _acceptTask;

        private FakeTunnelingParentProxy(TcpListener listener)
        {
            _listener = listener;
            _acceptTask = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Request { get; private set; } = string.Empty;

        public static Task<FakeTunnelingParentProxy> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new FakeTunnelingParentProxy(listener));
        }

        private async Task AcceptAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var clientStream = client.GetStream();
            var headers = await ReadHeadersAsync(clientStream);
            Request = headers;

            var firstLine = headers.Split("\r\n", 2, StringSplitOptions.None)[0];
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !TryParseHostPort(parts[1], out var host, out var port))
            {
                await clientStream.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\n"u8.ToArray());
                return;
            }

            using var upstream = new TcpClient();
            await upstream.ConnectAsync(
                host.Equals("upstream.test", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : host,
                port);
            await using var upstreamStream = upstream.GetStream();
            await clientStream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray());

            var clientToUpstream = clientStream.CopyToAsync(upstreamStream);
            var upstreamToClient = upstreamStream.CopyToAsync(clientStream);
            await Task.WhenAny(clientToUpstream, upstreamToClient);
        }

        public static async Task<string> ReadHeadersAsync(Stream stream)
        {
            var buffer = new byte[4096];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length, 1));
                if (read == 0)
                    break;
                length += read;
                if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
                    break;
            }

            return Encoding.ASCII.GetString(buffer, 0, length);
        }

        private static bool TryParseHostPort(string target, out string host, out int port)
        {
            host = string.Empty;
            port = 0;
            var colon = target.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(target[(colon + 1)..], out port))
                return false;

            host = target[..colon];
            return !string.IsNullOrWhiteSpace(host);
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

    private sealed class FakeExternalMitmUnixProxy : IAsyncDisposable
    {
        private readonly Socket _listener;
        private readonly Task _acceptTask;

        private FakeExternalMitmUnixProxy(Socket listener, string socketPath)
        {
            _listener = listener;
            SocketPath = socketPath;
            _acceptTask = AcceptAsync();
        }

        public string SocketPath { get; }

        public string Request { get; private set; } = string.Empty;

        public string Payload { get; private set; } = string.Empty;

        public static Task<FakeExternalMitmUnixProxy> StartAsync()
        {
            var socketPath = $"/tmp/hpdmitm{Guid.NewGuid():N}"[..30];
            if (File.Exists(socketPath))
                File.Delete(socketPath);

            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);
            return Task.FromResult(new FakeExternalMitmUnixProxy(listener, socketPath));
        }

        private async Task AcceptAsync()
        {
            using var socket = await _listener.AcceptAsync();
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            Request = await FakeTunnelingParentProxy.ReadHeadersAsync(stream);
            await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray());

            var buffer = new byte[128];
            var read = await stream.ReadAsync(buffer);
            Payload = Encoding.ASCII.GetString(buffer, 0, read);
            await stream.WriteAsync("external mitm ok"u8.ToArray());
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _listener.Close();
                await _acceptTask;
            }
            catch
            {
                // Listener may be stopped before a test connects.
            }
            finally
            {
                _listener.Dispose();
                try
                {
                    if (File.Exists(SocketPath))
                        File.Delete(SocketPath);
                }
                catch
                {
                    // Best effort test cleanup.
                }
            }
        }
    }
}
