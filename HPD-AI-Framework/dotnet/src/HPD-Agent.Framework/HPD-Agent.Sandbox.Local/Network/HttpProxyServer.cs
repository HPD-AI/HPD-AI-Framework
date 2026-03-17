using System.Net;
using Microsoft.Extensions.Logging;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace HPD.Sandbox.Local.Network;

/// <summary>
/// HTTP/HTTPS proxy using Titanium.Web.Proxy with domain filtering.
/// </summary>
internal sealed class HttpProxyServer : IHttpProxyServer
{
    private readonly string[] _allowedDomains;
    private readonly string[] _deniedDomains;
    private readonly ILogger? _logger;
    private ProxyServer? _proxy;
    private int _port;

    public HttpProxyServer(
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
        _proxy = new ProxyServer();

        // Listen on random port
        var endpoint = new ExplicitProxyEndPoint(IPAddress.Loopback, 0, false);
        _proxy.AddEndPoint(endpoint);

        // Filter requests
        _proxy.BeforeRequest += OnBeforeRequest;

        _proxy.Start();

        _port = endpoint.Port;
        _logger?.LogInformation("HTTP proxy started on localhost:{Port}", _port);

        return Task.FromResult(_port);
    }

    private Task OnBeforeRequest(object sender, SessionEventArgs e)
    {
        var host = e.HttpClient.Request.RequestUri.Host;

        if (!IsAllowed(host))
        {
            _logger?.LogWarning("Blocked HTTP request to: {Host}", host);
            e.GenericResponse("Access denied by sandbox", HttpStatusCode.Forbidden);
        }

        return Task.CompletedTask;
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

    public ValueTask DisposeAsync()
    {
        if (_proxy != null)
        {
            _proxy.Stop();
            _proxy.Dispose();
            _logger?.LogInformation("HTTP proxy stopped");
        }
        return ValueTask.CompletedTask;
    }
}
