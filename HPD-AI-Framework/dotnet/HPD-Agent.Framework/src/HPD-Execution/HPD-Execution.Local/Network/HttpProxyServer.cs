using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using HPD.Execution.Contracts;
using HPD.Execution.Local.Policy;
using Microsoft.Extensions.Logging;

namespace HPD.Execution.Local.Network;

/// <summary>
/// Small HTTP/CONNECT proxy with sandbox domain filtering.
/// </summary>
internal sealed class HttpProxyServer : IHttpProxyServer
{
    private const int MaxHeaderBytes = 16 * 1024;
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UpstreamConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(2);

    private readonly NetworkPolicyEvaluator _policyEvaluator;
    private readonly ParentProxyPolicy? _parentProxy;
    private readonly ILogger? _logger;
    private readonly Action<ProcessIsolationProxyEvent>? _eventSink;
    private readonly MitmLeafCertificateCache? _leafCertificates;
    private readonly RemoteCertificateValidationCallback? _upstreamCertificateValidationCallback;
    private readonly string? _externalMitmUnixSocketPath;
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = [];
    private readonly ConcurrentDictionary<Task, byte> _clientTasks = [];
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _port;

    public HttpProxyServer(
        string[] allowedDomains,
        string[] deniedDomains,
        ParentProxyPolicy? parentProxy = null,
        RequestFilterPolicy? requestFilter = null,
        ILogger? logger = null,
        Action<ProcessIsolationProxyEvent>? eventSink = null,
        X509Certificate2? tlsIssuerCertificate = null,
        RemoteCertificateValidationCallback? upstreamCertificateValidationCallback = null,
        string? externalMitmUnixSocketPath = null)
    {
        _policyEvaluator = new NetworkPolicyEvaluator(
            LocalProcessIsolationPolicyBuilder.BuildNetworkPolicy(
                NetworkEgressMode.Filtered,
                allowedDomains ?? [],
                deniedDomains ?? []));
        _parentProxy = parentProxy;
        _logger = logger;
        _eventSink = eventSink;
        _leafCertificates = tlsIssuerCertificate is null
            ? null
            : new MitmLeafCertificateCache(tlsIssuerCertificate);
        _upstreamCertificateValidationCallback = upstreamCertificateValidationCallback;
        _externalMitmUnixSocketPath = externalMitmUnixSocketPath;
    }

    public int Port => _port;

    public Task<int> StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _ = AcceptConnectionsAsync(_cts.Token);
        _logger?.LogInformation("HTTP proxy started on localhost:{Port}", _port);

        return Task.FromResult(_port);
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _clients.TryAdd(client, 0);
                var task = HandleClientAsync(client, cancellationToken);
                _clientTasks.TryAdd(task, 0);
                _ = task.ContinueWith(
                    completed => _clientTasks.TryRemove(completed, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error accepting HTTP proxy connection");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            await using (var clientStream = client.GetStream())
            {
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestCts.CancelAfter(RequestReadTimeout);

                HttpProxyRequest? request;
                try
                {
                    request = await ReadHttpRequestAsync(clientStream, requestCts.Token);
                }
                catch (HttpProxyRequestException ex)
                {
                    EmitEvent(ProcessIsolationProxyEventKind.MalformedRequest, ex.Message);
                    await WriteBadRequestAsync(clientStream, ex.StatusCode, ex.Message, cancellationToken);
                    return;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    EmitEvent(ProcessIsolationProxyEventKind.MalformedRequest, "Request headers timed out");
                    await WriteBadRequestAsync(clientStream, 408, "Request headers timed out", cancellationToken);
                    return;
                }

                if (request is null)
                    return;

                if (request.Method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleConnectAsync(clientStream, request, cancellationToken);
                }
                else
                {
                    await HandlePlainHttpAsync(clientStream, request, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "HTTP proxy client error");
        }
        finally
        {
            _clients.TryRemove(client, out _);
        }
    }

    private async Task HandleConnectAsync(
        Stream clientStream,
        HttpProxyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseHostPort(request.Target, defaultPort: 443, out var host, out var port))
        {
            EmitEvent(
                ProcessIsolationProxyEventKind.MalformedRequest,
                "Malformed CONNECT target",
                method: request.Method);
            await WriteDeniedAsync(clientStream, "Malformed CONNECT target", cancellationToken);
            return;
        }

        var decision = _policyEvaluator.Evaluate(host);
        if (decision.Kind == NetworkPolicyDecisionKind.Deny)
        {
            _logger?.LogWarning("Blocked CONNECT request to: {Host} ({Reason})", host, decision.Reason);
            EmitEvent(
                ProcessIsolationProxyEventKind.NetworkPolicyDenied,
                decision.Reason,
                host,
                port,
                request.Method);
            await WriteDeniedAsync(clientStream, decision.Reason, cancellationToken);
            return;
        }

        if (_leafCertificates is not null)
        {
            await HandleTerminatedConnectAsync(clientStream, host, port, cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_externalMitmUnixSocketPath))
        {
            await HandleExternalMitmConnectAsync(clientStream, host, port, cancellationToken);
            return;
        }

        Stream upstream;
        try
        {
            upstream = await OpenDestinationStreamAsync(
                Uri.UriSchemeHttps,
                host,
                port,
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or OperationCanceledException)
        {
            _logger?.LogWarning(ex, "CONNECT upstream failed for {Host}:{Port}", host, port);
            EmitEvent(
                ProcessIsolationProxyEventKind.UpstreamFailure,
                ex.Message,
                host,
                port,
                request.Method);
            await WriteUpstreamFailureAsync(clientStream, ex.Message, cancellationToken);
            return;
        }

        await using (upstream)
        {
            await clientStream.WriteAsync(
                "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(),
                cancellationToken);
            await RelayDataAsync(clientStream, upstream, cancellationToken);
        }
    }

    private async Task HandleExternalMitmConnectAsync(
        Stream clientStream,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        Stream mitmStream;
        try
        {
            mitmStream = await ConnectTunnel.OpenAsync(
                async ct =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(_externalMitmUnixSocketPath!), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
                host,
                port,
                proxyAuthorization: null,
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or OperationCanceledException)
        {
            _logger?.LogWarning(ex, "External MITM proxy CONNECT failed for {Host}:{Port}", host, port);
            EmitEvent(ProcessIsolationProxyEventKind.UpstreamFailure, ex.Message, host, port, "CONNECT");
            await WriteUpstreamFailureAsync(clientStream, ex.Message, cancellationToken);
            return;
        }

        await using (mitmStream)
        {
            await clientStream.WriteAsync(
                "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(),
                cancellationToken);
            await RelayDataAsync(clientStream, mitmStream, cancellationToken);
        }
    }

    private async Task HandleTerminatedConnectAsync(
        Stream clientStream,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        await clientStream.WriteAsync(
            "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(),
            cancellationToken);

        await using var tlsClient = new SslStream(clientStream, leaveInnerStreamOpen: true);
        try
        {
            var leafCertificate = _leafCertificates!.GetOrCreate(host);
            await tlsClient.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = leafCertificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                },
                cancellationToken);

            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCts.CancelAfter(RequestReadTimeout);
            var request = await ReadHttpRequestAsync(tlsClient, requestCts.Token);
            if (request is null)
                return;

            var target = request.Target.StartsWith("/", StringComparison.Ordinal)
                ? request.Target
                : $"/{request.Target}";
            var uri = new Uri($"https://{FormatUriHost(host)}:{port}{target}");

            if (HasHeader(request.Headers, "Transfer-Encoding"))
            {
                EmitEvent(
                    ProcessIsolationProxyEventKind.MalformedRequest,
                    "Transfer-Encoding is not supported by the sandbox proxy",
                    host,
                    port,
                    request.Method,
                    uri);
                await WriteBadRequestAsync(
                    tlsClient,
                    400,
                    "Transfer-Encoding is not supported by the sandbox proxy",
                    cancellationToken);
                return;
            }

            try
            {
                _ = GetContentLength(request.Headers);
            }
            catch (HttpProxyRequestException ex)
            {
                EmitEvent(
                    ProcessIsolationProxyEventKind.MalformedRequest,
                    ex.Message,
                    host,
                    port,
                    request.Method,
                    uri);
                await WriteBadRequestAsync(tlsClient, ex.StatusCode, ex.Message, cancellationToken);
                return;
            }

            await using var upstream = await OpenDestinationStreamAsync(
                Uri.UriSchemeHttps,
                host,
                port,
                cancellationToken);
            await using var tlsUpstream = new SslStream(upstream, leaveInnerStreamOpen: false);
            await tlsUpstream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    RemoteCertificateValidationCallback = _upstreamCertificateValidationCallback
                },
                cancellationToken);
            await ForwardDirectHttpRequestAsync(tlsUpstream, tlsClient, request, uri, cancellationToken);
            await RelayOneWayAsync(tlsUpstream, tlsClient, cancellationToken);
        }
        catch (HttpProxyRequestException ex)
        {
            EmitEvent(ProcessIsolationProxyEventKind.MalformedRequest, ex.Message, host, port);
            await WriteBadRequestAsync(tlsClient, ex.StatusCode, ex.Message, cancellationToken);
        }
        catch (AuthenticationException ex)
        {
            _logger?.LogWarning(ex, "TLS termination failed for {Host}:{Port}", host, port);
            EmitEvent(ProcessIsolationProxyEventKind.UpstreamFailure, ex.Message, host, port);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Terminated CONNECT upstream failed for {Host}:{Port}", host, port);
            EmitEvent(ProcessIsolationProxyEventKind.UpstreamFailure, ex.Message, host, port);
            if (!cancellationToken.IsCancellationRequested)
                await WriteUpstreamFailureAsync(tlsClient, ex.Message, cancellationToken);
        }
    }

    private async Task HandlePlainHttpAsync(
        Stream clientStream,
        HttpProxyRequest request,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Target, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            EmitEvent(
                ProcessIsolationProxyEventKind.MalformedRequest,
                "Malformed HTTP proxy target",
                method: request.Method);
            await WriteDeniedAsync(clientStream, "Malformed HTTP proxy target", cancellationToken);
            return;
        }

        var decision = _policyEvaluator.Evaluate(uri.Host);
        if (decision.Kind == NetworkPolicyDecisionKind.Deny)
        {
            _logger?.LogWarning("Blocked HTTP request to: {Host} ({Reason})", uri.Host, decision.Reason);
            EmitEvent(
                ProcessIsolationProxyEventKind.NetworkPolicyDenied,
                decision.Reason,
                uri.Host,
                EffectivePort(uri),
                request.Method,
                uri);
            await WriteDeniedAsync(clientStream, decision.Reason, cancellationToken);
            return;
        }

        if (HasHeader(request.Headers, "Transfer-Encoding"))
        {
            EmitEvent(
                ProcessIsolationProxyEventKind.MalformedRequest,
                "Transfer-Encoding is not supported by the sandbox proxy",
                uri.Host,
                EffectivePort(uri),
                request.Method,
                uri);
            await WriteBadRequestAsync(
                clientStream,
                400,
                "Transfer-Encoding is not supported by the sandbox proxy",
                cancellationToken);
            return;
        }

        try
        {
            _ = GetContentLength(request.Headers);
        }
        catch (HttpProxyRequestException ex)
        {
            EmitEvent(
                ProcessIsolationProxyEventKind.MalformedRequest,
                ex.Message,
                uri.Host,
                EffectivePort(uri),
                request.Method,
                uri);
            await WriteBadRequestAsync(clientStream, ex.StatusCode, ex.Message, cancellationToken);
            return;
        }

        var parentProxy = ParentProxyResolver.Resolve(uri, _parentProxy);
        try
        {
            if (parentProxy.IsBypassed)
            {
                await using var upstream = await OpenDirectTcpStreamAsync(uri.Host, EffectivePort(uri), cancellationToken);
                await ForwardDirectHttpRequestAsync(upstream, clientStream, request, uri, cancellationToken);
                await RelayOneWayAsync(upstream, clientStream, cancellationToken);
            }
            else
            {
                var proxyUri = parentProxy.ProxyUri!;
                await using var upstream = await OpenDirectTcpStreamAsync(proxyUri.Host, proxyUri.Port, cancellationToken);
                await ForwardParentProxyHttpRequestAsync(
                    upstream,
                    clientStream,
                    request,
                    ConnectTunnel.BuildProxyAuthorization(proxyUri),
                    cancellationToken);
                await RelayOneWayAsync(upstream, clientStream, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or OperationCanceledException)
        {
            _logger?.LogWarning(ex, "HTTP upstream failed for {Uri}", uri);
            EmitEvent(
                ProcessIsolationProxyEventKind.UpstreamFailure,
                ex.Message,
                uri.Host,
                EffectivePort(uri),
                request.Method,
                uri);
            await WriteUpstreamFailureAsync(clientStream, ex.Message, cancellationToken);
        }
    }

    private void EmitEvent(
        ProcessIsolationProxyEventKind kind,
        string? reason,
        string? host = null,
        int? port = null,
        string? method = null,
        Uri? uri = null)
    {
        if (_eventSink is null)
            return;

        try
        {
            _eventSink(new ProcessIsolationProxyEvent
            {
                Protocol = ProcessIsolationProxyProtocol.Http,
                Kind = kind,
                Reason = string.IsNullOrWhiteSpace(reason) ? kind.ToString() : reason,
                Timestamp = DateTimeOffset.UtcNow,
                Host = host,
                Port = port,
                Method = method,
                Uri = uri
            });
        }
        catch
        {
            // Observability must not alter proxy enforcement.
        }
    }

    private async Task<Stream> OpenDestinationStreamAsync(
        string scheme,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"{scheme}://{FormatUriHost(host)}:{port}/");
        var parentProxy = ParentProxyResolver.Resolve(uri, _parentProxy);
        if (parentProxy.IsBypassed)
            return await OpenDirectTcpStreamAsync(host, port, cancellationToken);

        var proxyUri = parentProxy.ProxyUri!;
        var proxyClient = new TcpClient();
        try
        {
            return await ConnectTunnel.OpenAsync(
                async ct =>
                {
                    await proxyClient.ConnectAsync(proxyUri.Host, proxyUri.Port, ct);
                    return new TcpClientOwnedStream(proxyClient);
                },
                host,
                port,
                ConnectTunnel.BuildProxyAuthorization(proxyUri),
                cancellationToken);
        }
        catch
        {
            proxyClient.Dispose();
            throw;
        }
    }

    private static async Task<Stream> OpenDirectTcpStreamAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(UpstreamConnectTimeout);
            await client.ConnectAsync(host, port, timeoutCts.Token);
            return new TcpClientOwnedStream(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task ForwardDirectHttpRequestAsync(
        Stream upstream,
        Stream clientStream,
        HttpProxyRequest request,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var target = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
        var builder = new StringBuilder()
            .Append(request.Method)
            .Append(' ')
            .Append(target)
            .Append(' ')
            .Append(request.Version)
            .Append("\r\n");
        AppendForwardHeaders(builder, request.Headers, null);
        builder.Append("\r\n");
        await upstream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), cancellationToken);
        await ForwardRequestBodyAsync(upstream, clientStream, request, cancellationToken);
    }

    private static async Task ForwardParentProxyHttpRequestAsync(
        Stream upstream,
        Stream clientStream,
        HttpProxyRequest request,
        string? proxyAuthorization,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .Append(request.Method)
            .Append(' ')
            .Append(request.Target)
            .Append(' ')
            .Append(request.Version)
            .Append("\r\n");
        AppendForwardHeaders(builder, request.Headers, proxyAuthorization);
        builder.Append("\r\n");
        await upstream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), cancellationToken);
        await ForwardRequestBodyAsync(upstream, clientStream, request, cancellationToken);
    }

    private static async Task ForwardRequestBodyAsync(
        Stream upstream,
        Stream clientStream,
        HttpProxyRequest request,
        CancellationToken cancellationToken)
    {
        var contentLength = GetContentLength(request.Headers);
        if (contentLength is null or 0)
            return;

        if (request.BodyPrefix.Length > contentLength.Value)
            throw new HttpProxyRequestException(400, "Request body prefix exceeded Content-Length.");

        if (request.BodyPrefix.Length > 0)
            await upstream.WriteAsync(request.BodyPrefix, cancellationToken);

        var remaining = contentLength.Value - request.BodyPrefix.Length;
        if (remaining > 0)
            await CopyExactBytesAsync(clientStream, upstream, remaining, cancellationToken);
    }

    private static async Task CopyExactBytesAsync(
        Stream source,
        Stream destination,
        long bytesToCopy,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var remaining = bytesToCopy;
        while (remaining > 0)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (read == 0)
                throw new IOException("Client closed before request body completed.");

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }

    private static void AppendForwardHeaders(
        StringBuilder builder,
        IReadOnlyList<string> headers,
        string? proxyAuthorization)
    {
        foreach (var header in headers)
        {
            var colon = header.IndexOf(':');
            var name = colon >= 0 ? header[..colon] : header;
            if (IsHopByHopHeader(name))
                continue;
            builder.Append(header).Append("\r\n");
        }

        if (!string.IsNullOrWhiteSpace(proxyAuthorization))
            builder.Append("Proxy-Authorization: ").Append(proxyAuthorization).Append("\r\n");
    }

    private static IReadOnlyDictionary<string, string> ParseHeaders(IReadOnlyList<string> headers)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            var colon = header.IndexOf(':');
            if (colon <= 0)
                continue;

            var name = header[..colon].Trim();
            var value = header[(colon + 1)..].Trim();
            parsed[name] = parsed.TryGetValue(name, out var existing)
                ? $"{existing}, {value}"
                : value;
        }

        return parsed;
    }

    private static bool IsHopByHopHeader(string name) =>
        name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);

    private static bool HasHeader(IReadOnlyList<string> headers, string headerName) =>
        headers.Any(header =>
        {
            var colon = header.IndexOf(':');
            var name = colon >= 0 ? header[..colon] : header;
            return name.Trim().Equals(headerName, StringComparison.OrdinalIgnoreCase);
        });

    private static long? GetContentLength(IReadOnlyList<string> headers)
    {
        long? contentLength = null;
        foreach (var header in headers)
        {
            var colon = header.IndexOf(':');
            if (colon <= 0)
                continue;

            var name = header[..colon].Trim();
            if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = header[(colon + 1)..].Trim();
            if (!long.TryParse(value, out var parsed) || parsed < 0)
                throw new HttpProxyRequestException(400, "Invalid Content-Length.");

            if (contentLength is not null && contentLength.Value != parsed)
                throw new HttpProxyRequestException(400, "Conflicting Content-Length headers.");

            contentLength = parsed;
        }

        return contentLength;
    }

    private static async Task<HttpProxyRequest?> ReadHttpRequestAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxHeaderBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length, 1), cancellationToken);
            if (read == 0)
                return null;

            length += read;
            var headerEnd = IndexOfHeaderEnd(buffer.AsSpan(0, length));
            if (headerEnd >= 0)
            {
                var headerLength = headerEnd + 4;
                var headerText = Encoding.ASCII.GetString(buffer, 0, headerLength);
                var extra = length == headerLength
                    ? []
                    : buffer.AsSpan(headerLength, length - headerLength).ToArray();
                return ParseRequest(headerText, extra);
            }
        }

        throw new HttpProxyRequestException(431, "HTTP proxy request headers exceeded the size limit.");
    }

    private static HttpProxyRequest ParseRequest(string headerText, byte[] bodyPrefix)
    {
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3)
            throw new HttpProxyRequestException(400, "Malformed HTTP request line.");

        if (!requestLine[2].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            throw new HttpProxyRequestException(400, "Malformed HTTP version.");

        var headers = lines.Skip(1).Where(line => line.Length > 0).ToArray();
        foreach (var header in headers)
        {
            var colon = header.IndexOf(':');
            if (colon <= 0)
                throw new HttpProxyRequestException(400, "Malformed HTTP header.");
        }

        return new HttpProxyRequest(
            requestLine[0],
            requestLine[1],
            requestLine[2],
            headers,
            bodyPrefix);
    }

    private static int IndexOfHeaderEnd(ReadOnlySpan<byte> bytes)
    {
        for (var i = 3; i < bytes.Length; i++)
        {
            if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
                return i - 3;
        }

        return -1;
    }

    private static async Task WriteDeniedAsync(
        Stream stream,
        string? reason,
        CancellationToken cancellationToken)
    {
        var body = reason is { Length: > 0 }
            ? $"Access denied by sandbox: {reason}"
            : "Access denied by sandbox";
        var response = "HTTP/1.1 403 Forbidden\r\n" +
            "Connection: close\r\n" +
            "Content-Type: text/plain\r\n" +
            "X-Proxy-Error: sandbox_denied\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n" +
            body;
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken);
    }

    private static async Task WriteUpstreamFailureAsync(
        Stream stream,
        string? reason,
        CancellationToken cancellationToken)
    {
        var body = reason is { Length: > 0 }
            ? $"Sandbox proxy upstream failure: {reason}"
            : "Sandbox proxy upstream failure";
        var response = "HTTP/1.1 502 Bad Gateway\r\n" +
            "Connection: close\r\n" +
            "Content-Type: text/plain\r\n" +
            "X-Proxy-Error: upstream_failure\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n" +
            body;
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken);
    }

    private static async Task WriteBadRequestAsync(
        Stream stream,
        int statusCode,
        string reason,
        CancellationToken cancellationToken)
    {
        var statusText = statusCode switch
        {
            400 => "Bad Request",
            408 => "Request Timeout",
            431 => "Request Header Fields Too Large",
            _ => "Bad Request",
        };
        var body = reason;
        var response = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
            "Connection: close\r\n" +
            "Content-Type: text/plain\r\n" +
            "X-Proxy-Error: malformed_request\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n" +
            body;
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken);
    }

    private static async Task RelayDataAsync(
        Stream clientStream,
        Stream remoteStream,
        CancellationToken cancellationToken)
    {
        var clientToRemote = RelayOneWayAsync(clientStream, remoteStream, cancellationToken);
        var remoteToClient = RelayOneWayAsync(remoteStream, clientStream, cancellationToken);

        await Task.WhenAny(clientToRemote, remoteToClient);
    }

    private static async Task RelayOneWayAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch (IOException)
        {
            // Connection closed.
        }
        catch (OperationCanceledException)
        {
            // Cancelled.
        }
    }

    private static bool TryParseHostPort(
        string target,
        int defaultPort,
        out string host,
        out int port)
    {
        host = string.Empty;
        port = defaultPort;

        if (target.StartsWith("[", StringComparison.Ordinal))
        {
            var end = target.IndexOf(']');
            if (end <= 0)
                return false;

            host = target[1..end];
            if (target.Length > end + 1 && (!target[(end + 1)..].StartsWith(":", StringComparison.Ordinal) ||
                !int.TryParse(target[(end + 2)..], out port)))
                return false;
        }
        else
        {
            var colon = target.LastIndexOf(':');
            if (colon > 0 && int.TryParse(target[(colon + 1)..], out var parsedPort))
            {
                host = target[..colon];
                port = parsedPort;
            }
            else
            {
                host = target;
            }
        }

        return !string.IsNullOrWhiteSpace(host) && port is > 0 and <= 65535;
    }

    private static int EffectivePort(Uri uri)
    {
        if (!uri.IsDefaultPort)
            return uri.Port;
        return uri.Scheme == Uri.UriSchemeHttps ? 443 : 80;
    }

    private static string FormatUriHost(string host) =>
        host.Contains(':', StringComparison.Ordinal) && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]"
            : host;

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        foreach (var client in _clients.Keys)
        {
            try
            {
                client.Close();
            }
            catch
            {
                // Best effort shutdown.
            }
        }

        _cts?.Dispose();
        _cts = null;
        _listener = null;

        var activeTasks = _clientTasks.Keys.ToArray();
        if (activeTasks.Length > 0)
        {
            var allCompleted = Task.WhenAll(activeTasks);
            var timeout = Task.Delay(ShutdownWaitTimeout);
            await Task.WhenAny(allCompleted, timeout);
        }

        _logger?.LogInformation("HTTP proxy stopped");
        _leafCertificates?.Dispose();
    }

    private sealed record HttpProxyRequest(
        string Method,
        string Target,
        string Version,
        IReadOnlyList<string> Headers,
        byte[] BodyPrefix);

    private sealed class HttpProxyRequestException(int statusCode, string message) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }

    private sealed class TcpClientOwnedStream : Stream
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        public TcpClientOwnedStream(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => _stream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _stream.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _stream.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _stream.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _client.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
            _client.Dispose();
            await base.DisposeAsync();
        }
    }
}
