using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Events;
using HPD.Sandbox.Local.Network;
using HPD.Sandbox.Local.Platforms;
using HPD.Sandbox.Local.Platforms.Linux;
using HPD.Sandbox.Local.Platforms.MacOS;
using HPD.Sandbox.Local.State;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local;

/// <summary>
/// Middleware that applies OS-level sandboxing to function execution.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b></para>
/// <para>Automatically wraps shell/process execution with filesystem and network restrictions.</para>
/// <para>Integrates with the HPD-Agent middleware pipeline for seamless sandboxing.</para>
///
/// <para><b>Hooks Used:</b></para>
/// <list type="bullet">
/// <item><c>BeforeMessageTurnAsync</c> - Lazy initialization of sandbox infrastructure</item>
/// <item><c>BeforeFunctionAsync</c> - Validate function is allowed to execute</item>
/// <item><c>WrapFunctionCallAsync</c> - Wrap command execution with sandbox</item>
/// <item><c>AfterFunctionAsync</c> - Report violations, cleanup</item>
/// </list>
///
/// <para><b>Thread Safety:</b></para>
/// <para>Safe for concurrent agent runs after initialization.</para>
/// </remarks>
/// <example>
/// <code>
/// // Global sandboxing
/// var agent = new AgentBuilder()
///     .WithMiddleware(new SandboxMiddleware(config))
///     .Build();
/// </code>
/// </example>
public sealed class SandboxMiddleware : IAgentMiddleware, IAsyncDisposable
{
    private readonly SandboxConfig _config;
    private readonly ILogger<SandboxMiddleware>? _logger;
    private IPlatformSandbox? _platformSandbox;
    private IHttpProxyServer? _httpProxy;
    private ISocks5ProxyServer? _socksProxy;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Creates a new sandbox middleware with the specified configuration.
    /// </summary>
    /// <param name="config">Sandbox configuration (filesystem and network restrictions).</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">If config is null.</exception>
    public SandboxMiddleware(SandboxConfig config, ILogger<SandboxMiddleware>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.Validate();
        _logger = logger;
    }

    /// <summary>
    /// Current sandbox configuration (immutable after construction).
    /// </summary>
    public SandboxConfig Configuration => _config;

    /// <summary>
    /// Whether the sandbox infrastructure is initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Current platform (Linux, macOS, Windows).
    /// </summary>
    public PlatformType Platform => PlatformDetector.Current;

    /// <summary>
    /// Custom predicate to determine which functions should be sandboxed.
    /// </summary>
    /// <remarks>
    /// <para>By default, sandboxes functions that execute shell commands or processes:</para>
    /// <list type="bullet">
    /// <item>Functions with "Execute", "Run", "Shell", "Bash", "Command" in name</item>
    /// <item>Functions with <c>[Sandboxable]</c> attribute</item>
    /// <item>Functions explicitly listed in <c>SandboxConfig.SandboxableFunctions</c></item>
    /// </list>
    /// <para>Override this for custom logic.</para>
    /// </remarks>
    public Func<AIFunction, bool>? ShouldSandbox { get; set; }

    /// <summary>
    /// Lazy initialization of sandbox infrastructure.
    /// </summary>
    public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            // Detect platform
            var platform = PlatformDetector.Current;

            // Handle Windows early - no HTTP proxy needed
            if (platform == PlatformType.Windows)
            {
                _platformSandbox = new WindowsSandbox(_config, _logger);
                HandleInitializationFailure(context,
                    "OS-level sandboxing is not supported on Windows. " +
                    "Consider using HPD.Sandbox.Container with Docker Desktop, or WSL2 with the Linux sandbox.");
                return;
            }

            // Start HTTP proxy if network filtering enabled
            if (_config.AllowedDomains != null && _config.AllowedDomains.Length > 0)
            {
                _httpProxy = new HttpProxyServer(
                    _config.AllowedDomains,
                    _config.DeniedDomains,
                    _logger);
                await _httpProxy.StartAsync(cancellationToken);

                // Linux sandbox uses SOCKS5 for better compatibility
                if (platform == PlatformType.Linux)
                {
                    _socksProxy = new Socks5ProxyServer(
                        _config.AllowedDomains,
                        _config.DeniedDomains,
                        _logger);
                    await _socksProxy.StartAsync(cancellationToken);
                }
            }

            // Create platform-specific sandbox
            _platformSandbox = CreatePlatformSandbox(platform);

            // Verify dependencies
            if (!await _platformSandbox.CheckDependenciesAsync(cancellationToken))
            {
                HandleInitializationFailure(context,
                    $"Missing sandbox dependencies for {platform}");
                return;
            }

            _initialized = true;
            _logger?.LogInformation("Sandbox initialized for {Platform}", platform);

            // Emit initialization event
            context.TryEmit(new SandboxInitializedEvent
            {
                Tier = SandboxTier.Local,
                Platform = platform.ToString(),
                HttpProxyPort = _httpProxy?.Port
            });
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Validates function is allowed to execute.
    /// </summary>
    public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        if (context.Function == null) return Task.CompletedTask;

        // Check if function is blocked due to previous violations
        var state = GetSandboxState(context);
        if (state.BlockedFunctions.Contains(context.Function.Name))
        {
            context.BlockExecution = true;
            context.OverrideResult = "Function blocked due to sandbox policy violation";
            context.TryEmit(new SandboxBlockedEvent(
                context.Function.Name,
                "Function blocked due to previous sandbox violations"));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Wraps function execution with sandbox restrictions.
    /// </summary>
    public async Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
    {
        if (!ShouldSandboxFunction(request.Function))
        {
            return await handler(request);
        }

        if (!_initialized || _platformSandbox == null)
        {
            return await HandleUninitializedExecution(request, handler);
        }

        // Extract command from arguments
        var command = ExtractCommand(request.Arguments);
        if (command == null)
        {
            // No command to sandbox, pass through
            return await handler(request);
        }

        // Wrap command with sandbox
        var wrappedCommand = await _platformSandbox.WrapCommandAsync(command, cancellationToken);

        _logger?.LogDebug("Sandboxing command: {Original} -> {Wrapped}",
            command, wrappedCommand);

        // Replace command argument with sandboxed version
        var modifiedArgs = new Dictionary<string, object?>(request.Arguments);
        var commandKey = FindCommandKey(request.Arguments);
        if (commandKey != null)
        {
            modifiedArgs[commandKey] = wrappedCommand;
        }

        var sandboxedRequest = request.Override(arguments: modifiedArgs);

        // Execute with sandbox
        return await handler(sandboxedRequest);
    }

    /// <summary>
    /// Reports violations and updates state after function execution.
    /// </summary>
    public async Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
    {
        if (_platformSandbox?.Violations == null) return;
        if (context.Function == null) return;

        var violations = new List<SandboxViolation>();
        while (_platformSandbox.Violations.TryRead(out var violation))
        {
            violations.Add(violation);
        }

        if (violations.Count == 0) return;

        // Emit violation events
        foreach (var violation in violations)
        {
            context.TryEmit(new SandboxViolationEvent(
                context.Function.Name,
                violation.Type,
                violation.Message,
                violation.Path));
        }

        // Update state if configured to block on violations
        if (_config.OnViolation == SandboxViolationBehavior.BlockAndEmit)
        {
            UpdateSandboxState(context, state =>
                state.WithBlockedFunction(context.Function.Name));
        }

        await Task.CompletedTask;
    }

    // State management helpers

    private const string SandboxStateKey = "HPD.Sandbox.Local.State.SandboxStateData";

    private static SandboxStateData GetSandboxState(HookContext context)
    {
        return context.Analyze(s =>
            s.MiddlewareState.GetState<SandboxStateData>(SandboxStateKey))
            ?? new SandboxStateData();
    }

    private static void UpdateSandboxState(
        HookContext context,
        Func<SandboxStateData, SandboxStateData> transform)
    {
        context.UpdateState(s =>
        {
            var current = s.MiddlewareState.GetState<SandboxStateData>(SandboxStateKey)
                ?? new SandboxStateData();
            var updated = transform(current);
            return s with
            {
                MiddlewareState = s.MiddlewareState.SetState(SandboxStateKey, updated)
            };
        });
    }

    // Helper methods

    private bool ShouldSandboxFunction(AIFunction function)
    {
        // Check custom predicate first
        if (ShouldSandbox != null)
            return ShouldSandbox(function);

        var name = function.Name;

        // Check exclusions
        if (_config.ExcludedFunctions.Any(pattern => MatchesPattern(name, pattern)))
            return false;

        // Check explicit inclusions
        if (_config.SandboxableFunctions.Any(pattern => MatchesPattern(name, pattern)))
            return true;

        // Check for [Sandboxable] attribute via AdditionalProperties (AOT-compatible)
        // The source generator populates this at compile-time
        if (function.AdditionalProperties.TryGetValue("IsSandboxable", out var isSandboxable) &&
            isSandboxable is true)
        {
            return true;
        }

        // Auto-detect by name patterns
        var sandboxablePatterns = new[] { "Execute", "Run", "Shell", "Bash", "Command", "Process" };
        return sandboxablePatterns.Any(p =>
            name.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        if (pattern.EndsWith("*"))
            return name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith("*"))
            return name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindCommandKey(IReadOnlyDictionary<string, object?> arguments)
    {
        var commandKeys = new[] { "command", "cmd", "shell", "script", "bash" };
        foreach (var key in commandKeys)
        {
            if (arguments.ContainsKey(key))
                return key;
        }
        return null;
    }

    private static string? ExtractCommand(IReadOnlyDictionary<string, object?> arguments)
    {
        var key = FindCommandKey(arguments);
        if (key == null) return null;

        var value = arguments[key];
        if (value is string cmd)
            return cmd;

        if (value is JsonElement elem && elem.ValueKind == JsonValueKind.String)
            return elem.GetString();

        return null;
    }

    private void HandleInitializationFailure(HookContext context, string message)
    {
        switch (_config.OnInitializationFailure)
        {
            case SandboxFailureBehavior.Block:
                context.TryEmit(new SandboxErrorEvent(message));
                _logger?.LogError("Sandbox initialization failed: {Message}", message);
                break;

            case SandboxFailureBehavior.Warn:
                context.TryEmit(new SandboxWarningEvent(message));
                _logger?.LogWarning("Sandbox initialization failed: {Message}", message);
                break;

            case SandboxFailureBehavior.Ignore:
                _logger?.LogDebug("Sandbox initialization failed (ignored): {Message}", message);
                break;
        }
    }

    private async Task<object?> HandleUninitializedExecution(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler)
    {
        switch (_config.OnInitializationFailure)
        {
            case SandboxFailureBehavior.Block:
                return "Sandbox not initialized - function blocked";

            default:
                return await handler(request);
        }
    }

    /// <summary>
    /// Creates the appropriate platform-specific sandbox based on configuration.
    /// </summary>
    private IPlatformSandbox CreatePlatformSandbox(PlatformType platform)
    {
        _logger?.LogInformation("Creating sandbox for {Platform}", platform);
        return platform switch
        {
            PlatformType.Linux => new LinuxSandbox(
                _config,
                _httpProxy,
                _socksProxy,
                _logger),
            PlatformType.MacOS => new MacOSSandbox(
                _config,
                _httpProxy,
                _socksProxy,
                _logger),
            PlatformType.Windows => new WindowsSandbox(_config, _logger),
            _ => throw new PlatformNotSupportedException($"Unsupported platform: {platform}")
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_platformSandbox != null)
            await _platformSandbox.DisposeAsync();

        if (_httpProxy != null)
            await _httpProxy.DisposeAsync();

        if (_socksProxy != null)
            await _socksProxy.DisposeAsync();

        _initLock.Dispose();
    }
}
