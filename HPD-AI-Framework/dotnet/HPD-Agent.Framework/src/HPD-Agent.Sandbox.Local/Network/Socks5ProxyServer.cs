using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local.Network;

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
    private readonly string[] _allowedDomains;
    private readonly string[] _deniedDomains;
    private readonly ILogger? _logger;
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
        ILogger? logger = null)
    {
        _allowedDomains = allowedDomains ?? [];
        _deniedDomains = deniedDomains ?? [];
        _logger = logger;
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
        var read = await stream.ReadAsync(buffer.AsMemory(0, 2), cancellationToken);
        if (read < 2 || buffer[0] != Socks5Version)
            return false;

        var nmethods = buffer[1];
        read = await stream.ReadAsync(buffer.AsMemory(0, nmethods), cancellationToken);
        if (read < nmethods)
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
        var read = await stream.ReadAsync(buffer.AsMemory(0, 4), cancellationToken);
        if (read < 4)
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
            await SendReplyAsync(stream, ReplyCommandNotSupported, cancellationToken);
            return;
        }

        // Parse destination address
        string host;
        int port;

        switch (addressType)
        {
            case AddressTypeIPv4:
                read = await stream.ReadAsync(buffer.AsMemory(0, 6), cancellationToken);
                if (read < 6) return;

                host = new IPAddress(buffer.AsSpan(0, 4)).ToString();
                port = (buffer[4] << 8) | buffer[5];
                break;

            case AddressTypeDomain:
                read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
                if (read < 1) return;

                var domainLength = buffer[0];
                read = await stream.ReadAsync(buffer.AsMemory(0, domainLength + 2), cancellationToken);
                if (read < domainLength + 2) return;

                host = Encoding.ASCII.GetString(buffer, 0, domainLength);
                port = (buffer[domainLength] << 8) | buffer[domainLength + 1];
                break;

            case AddressTypeIPv6:
                read = await stream.ReadAsync(buffer.AsMemory(0, 18), cancellationToken);
                if (read < 18) return;

                host = new IPAddress(buffer.AsSpan(0, 16)).ToString();
                port = (buffer[16] << 8) | buffer[17];
                break;

            default:
                await SendReplyAsync(stream, ReplyAddressTypeNotSupported, cancellationToken);
                return;
        }

        // Check if connection is allowed
        if (!IsAllowed(host))
        {
            _logger?.LogWarning("SOCKS5: Blocked connection to {Host}:{Port}", host, port);
            await SendReplyAsync(stream, ReplyConnectionNotAllowed, cancellationToken);
            return;
        }

        // Connect to destination
        TcpClient? remote = null;
        try
        {
            remote = new TcpClient();
            await remote.ConnectAsync(host, port, cancellationToken);

            _logger?.LogDebug("SOCKS5: Connected to {Host}:{Port}", host, port);

            // Send success reply
            await SendReplyAsync(stream, ReplySucceeded, cancellationToken);

            // Relay data bidirectionally
            await using var remoteStream = remote.GetStream();
            await RelayDataAsync(stream, remoteStream, cancellationToken);
        }
        catch (SocketException ex)
        {
            _logger?.LogDebug("SOCKS5: Connection to {Host}:{Port} failed: {Message}", host, port, ex.Message);

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
            remote?.Dispose();
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
        NetworkStream clientStream,
        NetworkStream remoteStream,
        CancellationToken cancellationToken)
    {
        var clientToRemote = RelayOneWayAsync(clientStream, remoteStream, cancellationToken);
        var remoteToClient = RelayOneWayAsync(remoteStream, clientStream, cancellationToken);

        await Task.WhenAny(clientToRemote, remoteToClient);
    }

    private static async Task RelayOneWayAsync(
        NetworkStream source,
        NetworkStream destination,
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

    private bool IsAllowed(string host)
    {
        // Check denied domains first (takes precedence)
        if (_deniedDomains.Any(pattern => MatchesDomain(host, pattern)))
            return false;

        // Check allowed domains
        return _allowedDomains.Any(pattern => MatchesDomain(host, pattern));
    }

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
