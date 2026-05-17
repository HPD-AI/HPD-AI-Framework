using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Network;
using HPD.Sandbox.Local.Platforms;
using HPD.Sandbox.Local.Platforms.Linux;
using HPD.Sandbox.Local.Platforms.MacOS;
using HPD.Sandbox.Local.State;
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
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPlatformSandbox? _platformSandbox;
    private IHttpProxyServer? _httpProxy;
    private ISocks5ProxyServer? _socksProxy;
    private MitmCertificateAuthority? _ephemeralCertificateAuthority;
    private TlsTerminationConfig? _effectiveTlsTermination;
    private readonly SandboxViolationStore _violationStore = new();
    private CancellationTokenSource? _violationDrainCts;
    private Task? _violationDrainTask;
    private bool _disposed;

    public SandboxManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// The isolation tier this sandbox provides.
    /// </summary>
    public SandboxTier Tier => SandboxTier.Local;

    internal SandboxViolationStore ViolationStore => _violationStore;

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

        await EnsurePlatformSandboxInitializedAsync(config, cancellationToken);

        // Generate wrapped command via platform sandbox
        var invocation = CommandInvocation.From(command, args);
        var wrappedCommand = await _platformSandbox!.WrapCommandAsync(invocation, cancellationToken);

        // Build environment variables (for proxy)
        Dictionary<string, string>? environment = wrappedCommand.Environment is { Count: > 0 }
            ? new Dictionary<string, string>(wrappedCommand.Environment)
            : null;

        if (_httpProxy is not null)
        {
            var proxyUrl = $"http://127.0.0.1:{_httpProxy.Port}";
            environment ??= [];
            environment["HTTP_PROXY"] = proxyUrl;
            environment["HTTPS_PROXY"] = proxyUrl;
            environment["http_proxy"] = proxyUrl;
            environment["https_proxy"] = proxyUrl;
        }

        if (config.TlsTermination is not null)
        {
            environment ??= [];
            TlsTrustEnvironment.Apply(environment, _effectiveTlsTermination ?? config.TlsTermination);
        }

        return wrappedCommand with { Environment = environment };
    }

    /// <summary>
    /// Thread-safe initialization of platform sandbox.
    /// Uses double-checked locking pattern for efficiency.
    /// </summary>
    private async Task EnsurePlatformSandboxInitializedAsync(SandboxConfig config, CancellationToken cancellationToken)
    {
        if (_platformSandbox != null) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_platformSandbox != null) return;

            var effectiveConfig = await ResolveTlsTerminationAsync(config, cancellationToken);

            // Start proxies before platform creation so Linux bridges and macOS
            // profile/env generation see real bound ports rather than port 0.
            if (effectiveConfig.IsNetworkFiltered)
            {
                _httpProxy = new HttpProxyServer(
                    effectiveConfig.AllowedDomains,
                    effectiveConfig.DeniedDomains,
                    effectiveConfig.ParentProxy,
                    effectiveConfig.RequestFilter,
                    _logger,
                    eventSink: RecordProxyViolation,
                    tlsIssuerCertificate: _ephemeralCertificateAuthority?.Certificate,
                    externalMitmUnixSocketPath: effectiveConfig.MitmProxy?.UnixSocketPath);
                await _httpProxy.StartAsync(cancellationToken);

                if (PlatformDetector.Current == PlatformType.Linux)
                {
                    _socksProxy = new Socks5ProxyServer(
                        effectiveConfig.AllowedDomains,
                        effectiveConfig.DeniedDomains,
                        effectiveConfig.ParentProxy,
                        _logger,
                        eventSink: RecordProxyViolation);
                    await _socksProxy.StartAsync(cancellationToken);
                }
            }

            _platformSandbox = PlatformDetector.Current switch
            {
                PlatformType.Linux => new LinuxSandbox(effectiveConfig, _httpProxy, _socksProxy, _logger),
                PlatformType.MacOS => new MacOSSandbox(effectiveConfig, _httpProxy, _socksProxy, _logger),
                _ => throw new PlatformNotSupportedException(
                    $"Sandboxing not supported on {PlatformDetector.Current}")
            };

            _logger?.LogInformation(
                "Initialized {Platform} sandbox",
                PlatformDetector.Current);

            StartPlatformViolationDrain(_platformSandbox);

            // Check dependencies after the platform has its real proxy instances.
            var dependencyCheck = await _platformSandbox.GetDependencyCheckAsync(cancellationToken);
            foreach (var warning in dependencyCheck.Warnings)
                _logger?.LogWarning("Sandbox dependency warning: {Warning}", warning);

            if (!dependencyCheck.IsAvailable)
            {
                var platform = PlatformDetector.Current;
                var message = string.Join("; ", dependencyCheck.Errors);
                throw new InvalidOperationException(
                    $"Sandbox dependencies are missing for {platform}: {message}");
            }
        }
        catch
        {
            await DisposeStartedInfrastructureAsync();
            _platformSandbox = null;
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void RecordProxyViolation(SandboxProxyEvent proxyEvent)
    {
        _violationStore.Add(proxyEvent.ToSandboxViolation());
    }

    private void StartPlatformViolationDrain(IPlatformSandbox platformSandbox)
    {
        if (platformSandbox.Violations is null)
            return;

        _violationDrainCts = new CancellationTokenSource();
        _violationDrainTask = DrainPlatformViolationsAsync(
            platformSandbox.Violations,
            _violationDrainCts.Token);
    }

    private async Task DrainPlatformViolationsAsync(
        System.Threading.Channels.ChannelReader<SandboxViolation> violations,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var violation in violations.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                _violationStore.Add(violation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Sandbox platform violation drain failed");
        }
    }

    private async Task<SandboxConfig> ResolveTlsTerminationAsync(
        SandboxConfig config,
        CancellationToken cancellationToken)
    {
        if (config.TlsTermination is null)
        {
            _effectiveTlsTermination = null;
            return config;
        }

        var (tlsConfig, authority) = await MitmCertificateAuthority.ResolveAsync(
            config.TlsTermination,
            cancellationToken);
        _ephemeralCertificateAuthority = authority;
        _effectiveTlsTermination = tlsConfig;
        return config with { TlsTermination = tlsConfig };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await DisposeStartedInfrastructureAsync();
    }

    private async Task DisposeStartedInfrastructureAsync()
    {
        if (_violationDrainCts != null)
        {
            try
            {
                _violationDrainCts.Cancel();
                if (_violationDrainTask is not null)
                    await _violationDrainTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _violationDrainCts.Dispose();
                _violationDrainCts = null;
                _violationDrainTask = null;
            }
        }

        if (_socksProxy != null)
        {
            try
            {
                await _socksProxy.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing SOCKS proxy");
            }
            finally
            {
                _socksProxy = null;
            }
        }

        if (_httpProxy != null)
        {
            try
            {
                await _httpProxy.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing HTTP proxy");
            }
            finally
            {
                _httpProxy = null;
            }
        }

        if (_platformSandbox != null)
        {
            await _platformSandbox.DisposeAsync();
            _platformSandbox = null;
        }

        if (_ephemeralCertificateAuthority != null)
        {
            await _ephemeralCertificateAuthority.DisposeAsync();
            _ephemeralCertificateAuthority = null;
        }

        _effectiveTlsTermination = null;
    }
}
