using HPD.Environment.Contracts;
using HPD.Agent.Sandbox.Network;
using HPD.Agent.Sandbox.Platforms;
using HPD.Agent.Sandbox.Platforms.Linux;
using HPD.Agent.Sandbox.Platforms.MacOS;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Agent.Sandbox.State;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Sandbox;

/// <summary>
/// Manages local OS process-isolation helpers for command execution.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>This class is thread-safe for concurrent WrapCommandAsync calls.</para>
/// <para>Uses lock-based initialization for platform backends and proxy helpers.</para>
/// </remarks>
public sealed class SandboxIsolationManager : IAsyncDisposable
{
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private ISandboxBackend? _platformBackend;
    private IHttpProxyServer? _httpProxy;
    private ISocks5ProxyServer? _socksProxy;
    private MitmCertificateAuthority? _ephemeralCertificateAuthority;
    private readonly ProcessIsolationViolationStore _violationStore = new();
    private CancellationTokenSource? _violationDrainCts;
    private Task? _violationDrainTask;
    private string? _activationKey;
    private bool _disposed;

    public SandboxIsolationManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    internal ProcessIsolationViolationStore ViolationStore => _violationStore;

    internal async Task<PreparedSandboxCommand> WrapCommandAsync(
        CommandInvocation invocation,
        SandboxIsolationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(plan);

        await EnsurePlatformIsolationInitializedAsync(
            plan,
            plan.CreateActivationKey(),
            cancellationToken);

        var wrappedCommand = await _platformBackend!.WrapCommandAsync(
            invocation,
            plan,
            cancellationToken);

        Dictionary<string, string>? environment = wrappedCommand.Environment is { Count: > 0 }
            ? new Dictionary<string, string>(wrappedCommand.Environment)
            : null;

        foreach (var (key, value) in plan.Environment.InjectedVariables)
        {
            environment ??= [];
            environment[key] = value;
        }

        if (_httpProxy is not null)
        {
            var proxyUrl = $"http://127.0.0.1:{_httpProxy.Port}";
            environment ??= [];
            environment["HTTP_PROXY"] = proxyUrl;
            environment["HTTPS_PROXY"] = proxyUrl;
            environment["http_proxy"] = proxyUrl;
            environment["https_proxy"] = proxyUrl;
        }

        return wrappedCommand with { Environment = environment };
    }

    /// <summary>
    /// Thread-safe initialization of the platform backend.
    /// Uses double-checked locking pattern for efficiency.
    /// </summary>
    private async Task EnsurePlatformIsolationInitializedAsync(
        SandboxIsolationPlan plan,
        string activationKey,
        CancellationToken cancellationToken)
    {
        if (_platformBackend != null && _activationKey == activationKey) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_platformBackend != null && _activationKey == activationKey) return;

            if (_platformBackend != null)
                await DisposeStartedInfrastructureAsync();

            // Start proxies before platform creation so Linux bridges and macOS
            // profile/env generation see real bound ports rather than port 0.
            if (plan.Network.Mode == NetworkEgressMode.Filtered)
            {
                _httpProxy = new HttpProxyServer(
                    plan.Network.AllowedDomainPatterns().ToArray(),
                    plan.Network.DeniedDomainPatterns().ToArray(),
                    plan.Network.ParentProxy,
                    plan.Network.RequestFilter,
                    _logger,
                    eventSink: RecordProxyViolation,
                    tlsIssuerCertificate: _ephemeralCertificateAuthority?.Certificate,
                    externalMitmUnixSocketPath: null);
                await _httpProxy.StartAsync(cancellationToken);

                if (PlatformDetector.Current == PlatformType.Linux)
                {
                    _socksProxy = new Socks5ProxyServer(
                        plan.Network.AllowedDomainPatterns().ToArray(),
                        plan.Network.DeniedDomainPatterns().ToArray(),
                        plan.Network.ParentProxy,
                        _logger,
                        eventSink: RecordProxyViolation);
                    await _socksProxy.StartAsync(cancellationToken);
                }
            }

            _platformBackend = PlatformDetector.Current switch
            {
                PlatformType.Linux => new LinuxProcessIsolationBackend(plan, _httpProxy, _socksProxy, _logger),
                PlatformType.MacOS => new MacOSProcessIsolationBackend(plan, _httpProxy, _socksProxy, _logger),
                PlatformType.Windows => new WindowsProcessIsolationBackend(plan, _logger),
                _ => throw new PlatformNotSupportedException(
                    $"Process isolation is not supported on {PlatformDetector.Current}")
            };

            _logger?.LogInformation(
                "Initialized {Platform} process isolation",
                PlatformDetector.Current);

            StartPlatformViolationDrain(_platformBackend);
            _activationKey = activationKey;

            // Check dependencies after the platform has its real proxy instances.
            var dependencyCheck = await _platformBackend.GetDependencyCheckAsync(cancellationToken);
            foreach (var warning in dependencyCheck.Warnings)
                _logger?.LogWarning("Sandbox dependency warning: {Warning}", warning);

            if (!dependencyCheck.IsAvailable)
            {
                var platform = PlatformDetector.Current;
                var message = string.Join("; ", dependencyCheck.Errors);
                throw new InvalidOperationException(
                    $"Process isolation dependencies are missing for {platform}: {message}");
            }
        }
        catch
        {
            await DisposeStartedInfrastructureAsync();
            _platformBackend = null;
            _activationKey = null;
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void RecordProxyViolation(ProcessIsolationProxyEvent proxyEvent)
    {
        _violationStore.Add(proxyEvent.ToProcessIsolationViolation());
    }

    private void StartPlatformViolationDrain(ISandboxBackend platformBackend)
    {
        if (platformBackend.Violations is null)
            return;

        _violationDrainCts = new CancellationTokenSource();
        _violationDrainTask = DrainPlatformViolationsAsync(
            platformBackend.Violations,
            _violationDrainCts.Token);
    }

    private async Task DrainPlatformViolationsAsync(
        System.Threading.Channels.ChannelReader<ProcessIsolationViolation> violations,
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
            _logger?.LogWarning(ex, "Process isolation platform violation drain failed");
        }
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

        if (_platformBackend != null)
        {
            await _platformBackend.DisposeAsync();
            _platformBackend = null;
        }

        if (_ephemeralCertificateAuthority != null)
        {
            await _ephemeralCertificateAuthority.DisposeAsync();
            _ephemeralCertificateAuthority = null;
        }

        _activationKey = null;
    }
}
