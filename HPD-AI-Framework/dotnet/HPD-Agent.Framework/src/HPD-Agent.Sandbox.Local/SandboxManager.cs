using System.Collections.Concurrent;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Network;
using HPD.Sandbox.Local.Platforms;
using HPD.Sandbox.Local.Platforms.Linux;
using HPD.Sandbox.Local.Platforms.MacOS;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local;

/// <summary>
/// Manages OS-level sandboxing for process execution.
/// Used internally by MCPClientManager - not directly exposed to consumers.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>This class is thread-safe for concurrent WrapCommandAsync calls.</para>
/// <para>Uses lock-based initialization for platform sandbox and ConcurrentDictionary for proxies.</para>
/// </remarks>
public sealed class SandboxManager : ISandbox
{
    private readonly ILogger? _logger;
    private readonly object _initLock = new();
    private IPlatformSandbox? _platformSandbox;
    private readonly ConcurrentDictionary<string, IHttpProxyServer> _proxies = new();
    private bool _disposed;

    public SandboxManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// The isolation tier this sandbox provides.
    /// </summary>
    public SandboxTier Tier => SandboxTier.Local;

    /// <summary>
    /// Wraps a command with sandbox restrictions based on the provided config.
    /// </summary>
    /// <param name="command">The command to wrap (e.g., "npx")</param>
    /// <param name="args">Command arguments</param>
    /// <param name="config">Sandbox configuration for this specific server</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Wrapped command compatible with StdioClientTransport</returns>
    public async Task<SandboxedCommand> WrapCommandAsync(
        string command,
        IEnumerable<string> args,
        SandboxConfig config,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Thread-safe lazy initialization of platform sandbox
        await EnsurePlatformSandboxInitializedAsync(config, cancellationToken);

        // Start HTTP proxy if network filtering is configured
        IHttpProxyServer? proxy = null;
        if (config.AllowedDomains != null && config.AllowedDomains.Length > 0)
        {
            var proxyKey = string.Join(",", config.AllowedDomains.OrderBy(d => d));
            proxy = await _proxies.GetOrAddAsync<string, IHttpProxyServer>(proxyKey, async _ =>
            {
                var p = new HttpProxyServer(config.AllowedDomains, config.DeniedDomains, _logger);
                await p.StartAsync(cancellationToken);
                return p;
            });
        }

        // Build full command string with args
        var fullCommand = $"{command} {string.Join(" ", args)}";

        // Generate wrapped command via platform sandbox
        var wrappedCommand = await _platformSandbox!.WrapCommandAsync(fullCommand, cancellationToken);

        // Parse the wrapped command back into filename and args
        var parts = ParseCommand(wrappedCommand);

        // Build environment variables (for proxy)
        Dictionary<string, string>? environment = null;
        if (proxy != null)
        {
            var proxyUrl = $"http://127.0.0.1:{proxy.Port}";
            environment = new Dictionary<string, string>
            {
                ["HTTP_PROXY"] = proxyUrl,
                ["HTTPS_PROXY"] = proxyUrl,
                ["http_proxy"] = proxyUrl,
                ["https_proxy"] = proxyUrl
            };
        }

        return new SandboxedCommand(parts.FileName, parts.Args, environment);
    }

    /// <summary>
    /// Thread-safe initialization of platform sandbox.
    /// Uses double-checked locking pattern for efficiency.
    /// </summary>
    private async Task EnsurePlatformSandboxInitializedAsync(SandboxConfig config, CancellationToken cancellationToken)
    {
        if (_platformSandbox != null) return;

        lock (_initLock)
        {
            if (_platformSandbox != null) return;

            // Start HTTP proxy for network filtering if needed
            IHttpProxyServer? httpProxy = null;
            ISocks5ProxyServer? socksProxy = null;
            if (config.AllowedDomains != null && config.AllowedDomains.Length > 0)
            {
                httpProxy = new HttpProxyServer(config.AllowedDomains, config.DeniedDomains, _logger);
                // Note: StartAsync will be called later
                
                // Linux sandbox also uses SOCKS5 for better compatibility
                socksProxy = new Socks5ProxyServer(config.AllowedDomains, config.DeniedDomains, _logger);
            }

            _platformSandbox = PlatformDetector.Current switch
            {
                PlatformType.Linux => new LinuxSandbox(config, httpProxy, socksProxy, _logger),
                PlatformType.MacOS => new MacOSSandbox(config, httpProxy, socksProxy, _logger),
                _ => throw new PlatformNotSupportedException(
                    $"Sandboxing not supported on {PlatformDetector.Current}")
            };

            _logger?.LogInformation(
                "Initialized {Platform} sandbox",
                PlatformDetector.Current);
        }

        // Check dependencies
        if (!await _platformSandbox.CheckDependenciesAsync(cancellationToken))
        {
            var platform = PlatformDetector.Current;
            var tool = platform == PlatformType.Linux ? "bubblewrap (bwrap)" : "sandbox-exec";
            throw new InvalidOperationException(
                $"{tool} is required for sandboxing on {platform}. Please install the required tools.");
        }
    }

    private static (string FileName, IReadOnlyList<string> Args) ParseCommand(string command)
    {
        // Simple parsing - the first part is the executable
        var trimmed = command.Trim();
        var firstSpaceIndex = trimmed.IndexOf(' ');

        if (firstSpaceIndex == -1)
            return (trimmed, []);

        var fileName = trimmed[..firstSpaceIndex];
        var argsString = trimmed[(firstSpaceIndex + 1)..];

        // Parse args respecting quotes
        var args = ParseArgs(argsString);
        return (fileName, args);
    }

    private static List<string> ParseArgs(string argsString)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < argsString.Length; i++)
        {
            var c = argsString[i];

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (c == '\\' && i + 1 < argsString.Length)
            {
                // Handle escaped characters
                current.Append(argsString[++i]);
            }
            else if (c == ' ' && !inSingleQuote && !inDoubleQuote)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var proxy in _proxies.Values)
        {
            try
            {
                await proxy.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing proxy");
            }
        }

        if (_platformSandbox != null)
        {
            await _platformSandbox.DisposeAsync();
        }
    }
}

/// <summary>
/// Extension method for async GetOrAdd on ConcurrentDictionary.
/// </summary>
internal static class ConcurrentDictionaryExtensions
{
    public static async Task<TValue> GetOrAddAsync<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dictionary,
        TKey key,
        Func<TKey, Task<TValue>> valueFactory)
        where TKey : notnull
    {
        if (dictionary.TryGetValue(key, out var existingValue))
            return existingValue;

        var newValue = await valueFactory(key);
        return dictionary.GetOrAdd(key, newValue);
    }
}
