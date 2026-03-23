namespace HPD.Sandbox.Local.Network;

/// <summary>
/// SOCKS5 proxy server interface for non-HTTP traffic filtering.
/// </summary>
/// <remarks>
/// <para>SOCKS5 is needed for:</para>
/// <list type="bullet">
/// <item>Database connections (PostgreSQL, MySQL, Redis)</item>
/// <item>SSH tunnels</item>
/// <item>Custom TCP protocols</item>
/// <item>Any non-HTTP traffic that needs domain filtering</item>
/// </list>
/// </remarks>
internal interface ISocks5ProxyServer : IAsyncDisposable
{
    /// <summary>
    /// The port the proxy is listening on.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Starts the SOCKS5 proxy server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The port the proxy is listening on</returns>
    Task<int> StartAsync(CancellationToken cancellationToken = default);
}
