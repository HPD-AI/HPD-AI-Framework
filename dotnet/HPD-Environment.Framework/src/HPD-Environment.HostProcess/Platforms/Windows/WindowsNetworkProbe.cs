namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

/// <summary>Checks direct TCP and UDP loopback enforcement under the supplied token.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsNetworkProbe
{
    public static async Task VerifyAsync(SafeAccessTokenHandle launchToken, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var ipv4 = new TcpListener(IPAddress.Loopback, 0);
        var ipv6 = new TcpListener(IPAddress.IPv6Loopback, 0);
        try
        {
            ipv4.Start();
            ipv6.Start();
            await VerifyListenerAsync(launchToken, ipv4, deadline.Token).ConfigureAwait(false);
            await VerifyListenerAsync(launchToken, ipv6, deadline.Token).ConfigureAwait(false);
            await VerifyDatagramAsync(launchToken, IPAddress.Loopback, deadline.Token).ConfigureAwait(false);
            await VerifyDatagramAsync(launchToken, IPAddress.IPv6Loopback, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Windows sandbox network verification timed out.");
        }
        finally
        {
            ipv4.Stop();
            ipv6.Stop();
        }
    }

    internal static async Task VerifyDatagramAsync(
        SafeAccessTokenHandle launchToken, IPAddress address, CancellationToken cancellationToken)
    {
        using var listener = new UdpClient(new IPEndPoint(address, 0));
        var endpoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        byte[] payload = [1];
        using (var control = new UdpClient(address.AddressFamily))
        {
            await control.SendAsync(payload.AsMemory(), endpoint, cancellationToken).ConfigureAwait(false);
            await listener.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await WindowsIdentity.RunImpersonatedAsync(launchToken, async () =>
            {
                using var sender = new UdpClient(address.AddressFamily);
                await sender.SendAsync(payload.AsMemory(), endpoint, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.AccessDenied)
        {
            return;
        }

        // A successful send is insufficient evidence of either delivery or enforcement.
        throw new InvalidOperationException($"The Windows sandbox {address.AddressFamily} UDP send was not explicitly denied.");
    }

    private static async Task VerifyListenerAsync(
        SafeAccessTokenHandle launchToken, TcpListener listener, CancellationToken cancellationToken)
    {
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using (var control = new TcpClient(endpoint.AddressFamily))
        {
            await control.ConnectAsync(endpoint.Address, endpoint.Port, cancellationToken).ConfigureAwait(false);
            using var accepted = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await WindowsIdentity.RunImpersonatedAsync(launchToken, async () =>
            {
                // WFP can attribute the connection to the token active at socket creation.
                using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.AccessDenied)
        {
            return;
        }

        throw new InvalidOperationException($"The Windows sandbox token connected to the {endpoint.AddressFamily} probe listener.");
    }
}
