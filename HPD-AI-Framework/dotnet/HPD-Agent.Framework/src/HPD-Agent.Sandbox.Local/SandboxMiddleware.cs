using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Events;
using HPD.Sandbox.Local.Platforms;
using HPD.Sandbox.Local.State;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local;

/// <summary>
/// Owns the local sandbox runtime session and publishes the sandboxed process
/// runner capability for process-aware tools.
/// </summary>
/// <remarks>
/// <para>
/// This middleware owns sandbox lifetime only. It does not inspect function
/// arguments, infer command parameters, or rewrite shell strings. Code that
/// starts a process must resolve <see cref="ISandboxedProcessRunner"/> from the
/// runtime/function context and execute through that runner.
/// </para>
/// <para>
/// During runtime startup, <c>BeforeStartAsync</c> creates the local sandbox
/// session, publishes the process runner in runtime capabilities, and registers
/// session disposal. Runtime capabilities are sealed after startup by the agent
/// runtime. For one-shot turns without an explicit runtime start,
/// <c>BeforeMessageTurnAsync</c> lazily creates the same capability.
/// </para>
/// <para>
/// During shutdown, <c>BeforeStopAsync</c> stops the sandbox session so active
/// sandboxed processes, proxies, platform sandboxes, and violation drains are
/// cleaned up promptly. Registered disposal remains a backstop.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var agent = new AgentBuilder()
///     .WithSandbox(config)
///     .Build();
/// </code>
/// </example>
public sealed class SandboxMiddleware : IAgentMiddleware, IAsyncDisposable
{
    private readonly SandboxConfig _config;
    private readonly ILogger<SandboxMiddleware>? _logger;
    private SandboxRuntimeSession? _runtimeSession;
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

    public async Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
    {
        var session = await EnsureRuntimeSessionAsync(context.Emit, cancellationToken).ConfigureAwait(false);
        context.RuntimeCapabilities.Set<ISandboxedProcessRunner>(session.ProcessRunner);
        context.RegisterAsyncDisposable(session);
        context.Emit(new SandboxInitializedEvent
        {
            Tier = SandboxTier.Local,
            Platform = PlatformDetector.Current.ToString()
        });
    }

    /// <summary>
    /// Ensures non-runtime turns also receive the sandbox process capability.
    /// </summary>
    public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (context.RuntimeCapabilities.TryGet<ISandboxedProcessRunner>(out _))
            return;

        var session = await EnsureRuntimeSessionAsync(evt => context.TryEmit(evt), cancellationToken).ConfigureAwait(false);
        context.RuntimeCapabilities.Set<ISandboxedProcessRunner>(session.ProcessRunner);
    }

    public async Task BeforeStopAsync(BeforeStopContext context, CancellationToken cancellationToken)
    {
        SandboxRuntimeSession? session;
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            session = _runtimeSession;
            _runtimeSession = null;
            _initialized = false;
        }
        finally
        {
            _initLock.Release();
        }

        if (session is not null)
            await session.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SandboxRuntimeSession> EnsureRuntimeSessionAsync(
        Action<AgentEvent>? eventSink,
        CancellationToken cancellationToken)
    {
        if (_runtimeSession is not null)
            return _runtimeSession;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_runtimeSession is not null)
                return _runtimeSession;

            var session = new SandboxRuntimeSession(_config, _logger, eventSink);
            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            _runtimeSession = session;
            _initialized = true;
            _logger?.LogInformation("Sandbox process runner initialized for {Platform}", PlatformDetector.Current);
            return session;
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
    /// Process sandboxing is explicit through ISandboxedProcessRunner.
    /// </summary>
    public async Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
    {
        return await handler(request).ConfigureAwait(false);
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

    internal static SandboxConfigOverride? TryGetFunctionSandboxOverride(AIFunction function) =>
        SandboxFunctionMetadata.TryGetOverride(function);

    public async ValueTask DisposeAsync()
    {
        if (_runtimeSession != null)
            await _runtimeSession.DisposeAsync();

        _initLock.Dispose();
    }
}
