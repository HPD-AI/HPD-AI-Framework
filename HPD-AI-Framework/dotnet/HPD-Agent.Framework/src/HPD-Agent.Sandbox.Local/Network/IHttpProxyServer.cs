namespace HPD.Sandbox.Local.Network;

/// <summary>
/// HTTP/HTTPS proxy server interface for domain filtering.
/// </summary>
internal interface IHttpProxyServer : IAsyncDisposable
{
    /// <summary>
    /// The port the proxy is listening on.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Starts the proxy server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The port the proxy is listening on</returns>
    Task<int> StartAsync(CancellationToken cancellationToken = default);
}
