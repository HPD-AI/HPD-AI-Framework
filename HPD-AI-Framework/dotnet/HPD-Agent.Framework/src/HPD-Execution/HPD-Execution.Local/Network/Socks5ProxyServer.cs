using System.Net;
using System.Net.Sockets;
using System.Text;
using HPD.Execution.Contracts;
using HPD.Execution.Local.Policy;
using Microsoft.Extensions.Logging;

namespace HPD.Execution.Local.Network;

/// <summary>
/// SOCKS5 proxy server with domain filtering for non-HTTP traffic.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b></para>
/// <para>Filters non-HTTP traffic (database connections, SSH, etc.) by domain.</para>
/// <para>Implements RFC 1928 (SOCKS Protocol Version 5).</para>
///
/// <para><b>Supported SOCKS5 Features:</b></para>
/// <list type="bullet">
/// <item>No authentication (method 0x00)</item>
/// <item>CONNECT command (0x01)</item>
/// <item>IPv4 addresses (ATYP 0x01)</item>
/// <item>Domain names (ATYP 0x03)</item>
/// <item>IPv6 addresses (ATYP 0x04)</item>
/// </list>
///
/// <para><b>Domain Filtering:</b></para>
/// <para>
/// Only connections to allowed domains are permitted.
/// IPv4/IPv6 addresses are resolved to hostnames when possible.
/// </para>
/// </remarks>
internal sealed class Socks5ProxyServer : ISocks5ProxyServer
{
    private readonly NetworkPolicyEvaluator _policyEvaluator;
    private readonly ParentProxyPolicy? _parentProxy;
    private readonly ILogger? _logger;
    private readonly Action<ProcessIsolationProxyEvent>? _eventSink;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _port;

    // SOCKS5 constants
    private const byte Socks5Version = 0x05;
    private const byte NoAuthentication = 0x00;
    private const byte ConnectCommand = 0x01;
    private const byte AddressTypeIPv4 = 0x01;
    private const byte AddressTypeDomain = 0x03;
    private const byte AddressTypeIPv6 = 0x04;

    // Reply codes
    private const byte ReplySucceeded = 0x00;
    private const byte ReplyGeneralFailure = 0x01;
    private const byte ReplyConnectionNotAllowed = 0x02;
    private const byte ReplyNetworkUnreachable = 0x03;
    private const byte ReplyHostUnreachable = 0x04;
    private const byte ReplyConnectionRefused = 0x05;
    private const byte ReplyCommandNotSupported = 0x07;
    private const byte ReplyAddressTypeNotSupported = 0x08;

    public Socks5ProxyServer(
        string[] allowedDomains,
        string[] deniedDomains,
        ParentProxyPolicy? parentProxy = null,
        ILogger? logger = null,
        Action<ProcessIsolationProxyEvent>? eventSink = null)
    {
        _policyEvaluator = new NetworkPolicyEvaluator(
            LocalProcessIsolationPolicyBuilder.BuildNetworkPolicy(
                NetworkEgressMode.Filtered,
                allowedDomains ?? [],
                deniedDomains ?? []));
        _parentProxy = parentProxy;
        _logger = logger;
        _eventSink = eventSink;
    }

    public int Port => _port;

    public Task<int> StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Listen on random port
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _logger?.LogInformation("SOCKS5 proxy started on localhost:{Port}", _port);

        // Start accepting connections in background
        _ = AcceptConnectionsAsync(_cts.Token);

        return Task.FromResult(_port);
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error accepting SOCKS5 connection");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            try
            {
                // Step 1: Authentication negotiation
                if (!await HandleAuthenticationAsync(stream, cancellationToken))
                    return;

                // Step 2: Handle request
                await HandleRequestAsync(stream, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogDebug(ex, "SOCKS5 client error");
            }
        }
    }

    private async Task<bool> HandleAuthenticationAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[258];

        // Read version and number of methods
        if (!await ReadExactAsync(stream, buffer.AsMemory(0, 2), cancellationToken) ||
            buffer[0] != Socks5Version)
            return false;

        var nmethods = buffer[1];
        if (!await ReadExactAsync(stream, buffer.AsMemory(0, nmethods), cancellationToken))
            return false;

        // Check for no-auth method
        var hasNoAuth = false;
        for (var i = 0; i < nmethods; i++)
        {
            if (buffer[i] == NoAuthentication)
            {
                hasNoAuth = true;
                break;
            }
        }

        // Reply with selected method
        var reply = new byte[] { Socks5Version, hasNoAuth ? NoAuthentication : (byte)0xFF };
        await stream.WriteAsync(reply, cancellationToken);

        return hasNoAuth;
    }

    private async Task HandleRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[263];

        // Read request header: VER CMD RSV ATYP
        if (!await ReadExactAsync(stream, buffer.AsMemory(0, 4), cancellationToken))
            return;

        var version = buffer[0];
        var command = buffer[1];
        // buffer[2] is reserved
        var addressType = buffer[3];

        if (version != Socks5Version)
            return;

        // Only support CONNECT command
        if (command != ConnectCommand)
        {
            EmitEvent(ProcessIsolationProxyEventKind.MalformedRequest, "Unsupported SOCKS5 command");
            await SendReplyAsync(stream, ReplyCommandNotSupported, cancellationToken);
            return;
        }

        // Parse destination address
        string host;
        int port;

        switch (addressType)
        {
            case AddressTypeIPv4:
                if (!await ReadExactAsync(stream, buffer.AsMemory(0, 6), cancellationToken))
                    return;

                host = new IPAddress(buffer.AsSpan(0, 4)).ToString();
                port = (buffer[4] << 8) | buffer[5];
                break;

            case AddressTypeDomain:
                if (!await ReadExactAsync(stream, buffer.AsMemory(0, 1), cancellationToken))
                    return;

                var domainLength = buffer[0];
                if (!await ReadExactAsync(stream, buffer.AsMemory(0, domainLength + 2), cancellationToken))
                    return;

                host = Encoding.ASCII.GetString(buffer, 0, domainLength);
                port = (buffer[domainLength] << 8) | buffer[domainLength + 1];
                break;

            case AddressTypeIPv6:
                if (!await ReadExactAsync(stream, buffer.AsMemory(0, 18), cancellationToken))
                    return;

                host = new IPAddress(buffer.AsSpan(0, 16)).ToString();
                port = (buffer[16] << 8) | buffer[17];
                break;

            default:
                EmitEvent(ProcessIsolationProxyEventKind.MalformedRequest, "Unsupported SOCKS5 address type");
                await SendReplyAsync(stream, ReplyAddressTypeNotSupported, cancellationToken);
                return;
        }

        // Check if connection is allowed
        var decision = _policyEvaluator.Evaluate(host);
        if (decision.Kind == NetworkPolicyDecisionKind.Deny)
        {
            _logger?.LogWarning(
                "SOCKS5: Blocked connection to {Host}:{Port} ({Reason})",
                host,
                port,
                decision.Reason);
            EmitEvent(
                ProcessIsolationProxyEventKind.NetworkPolicyDenied,
                decision.Reason,
                host,
                port);
            await SendReplyAsync(stream, ReplyConnectionNotAllowed, cancellationToken);
            return;
        }

        // Connect to destination directly or through a configured parent proxy.
        TcpClient? remote = null;
        Stream? remoteStream = null;
        try
        {
            var parentProxy = ParentProxyResolver.Resolve(
                new Uri($"http://{FormatUriHost(host)}:{port}/"),
                _parentProxy);

            if (parentProxy.IsBypassed)
            {
                remote = new TcpClient();
                await remote.ConnectAsync(host, port, cancellationToken);
                remoteStream = remote.GetStream();
                _logger?.LogDebug("SOCKS5: Connected directly to {Host}:{Port}", host, port);
            }
            else
            {
                var proxyUri = parentProxy.ProxyUri!;
                remote = new TcpClient();
                remoteStream = await ConnectTunnel.OpenAsync(
                    async ct =>
                    {
                        await remote.ConnectAsync(proxyUri.Host, proxyUri.Port, ct);
                        return remote.GetStream();
                    },
                    host,
                    port,
                    ConnectTunnel.BuildProxyAuthorization(proxyUri),
                    cancellationToken);
                _logger?.LogDebug(
                    "SOCKS5: Connected to {Host}:{Port} via parent proxy {Proxy}",
                    host,
                    port,
                    parentProxy.RedactedProxyUri);
            }

            // Send success reply
            await SendReplyAsync(stream, ReplySucceeded, cancellationToken);

            // Relay data bidirectionally
            await RelayDataAsync(stream, remoteStream, cancellationToken);
        }
        catch (SocketException ex)
        {
            _logger?.LogDebug("SOCKS5: Connection to {Host}:{Port} failed: {Message}", host, port, ex.Message);
            EmitEvent(
                ProcessIsolationProxyEventKind.UpstreamFailure,
                ex.Message,
                host,
                port);

            var reply = ex.SocketErrorCode switch
            {
                SocketError.NetworkUnreachable => ReplyNetworkUnreachable,
                SocketError.HostUnreachable => ReplyHostUnreachable,
                SocketError.ConnectionRefused => ReplyConnectionRefused,
                _ => ReplyGeneralFailure
            };

            await SendReplyAsync(stream, reply, cancellationToken);
        }
        finally
        {
            if (remoteStream != null)
                await remoteStream.DisposeAsync();
            remote?.Dispose();
        }
    }

    private void EmitEvent(
        ProcessIsolationProxyEventKind kind,
        string? reason,
        string? host = null,
        int? port = null)
    {
        if (_eventSink is null)
            return;

        try
        {
            _eventSink(new ProcessIsolationProxyEvent
            {
                Protocol = ProcessIsolationProxyProtocol.Socks5,
                Kind = kind,
                Reason = string.IsNullOrWhiteSpace(reason) ? kind.ToString() : reason,
                Timestamp = DateTimeOffset.UtcNow,
                Host = host,
                Port = port
            });
        }
        catch
        {
            // Observability must not alter proxy enforcement.
        }
    }

    private async Task SendReplyAsync(NetworkStream stream, byte replyCode, CancellationToken cancellationToken)
    {
        // Reply format: VER REP RSV ATYP BND.ADDR BND.PORT
        // Using 0.0.0.0:0 as bound address (we don't expose our binding)
        var reply = new byte[]
        {
            Socks5Version,
            replyCode,
            0x00, // Reserved
            AddressTypeIPv4,
            0, 0, 0, 0, // BND.ADDR = 0.0.0.0
            0, 0 // BND.PORT = 0
        };

        await stream.WriteAsync(reply, cancellationToken);
    }

    private async Task RelayDataAsync(
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
            // Connection closed
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
    }

    private static async Task<bool> ReadExactAsync(
        NetworkStream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], cancellationToken);
            if (read == 0)
                return false;

            totalRead += read;
        }

        return true;
    }

    private static string FormatUriHost(string host) =>
        host.Contains(':', StringComparison.Ordinal) && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]"
            : host;

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        if (_cts != null)
        {
            _cts.Dispose();
            _cts = null;
        }

        _logger?.LogInformation("SOCKS5 proxy stopped");

        await Task.CompletedTask;
    }
}
