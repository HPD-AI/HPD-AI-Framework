using HPD.Agent;
using HPD.Agent.Sandbox;

namespace HPD.Sandbox.Local.Events;

/// <summary>
/// Emitted when a sandbox violation is detected.
/// </summary>
public sealed record SandboxViolationEvent : AgentEvent
{
    public string SourceName => "SandboxMiddleware";
    public string FunctionName { get; init; } = string.Empty;
    public ViolationType ViolationType { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Path { get; init; }

    public SandboxViolationEvent() { }

    public SandboxViolationEvent(
        string functionName,
        ViolationType violationType,
        string message,
        string? path = null)
    {
        FunctionName = functionName;
        ViolationType = violationType;
        Message = message;
        Path = path;
    }
}

/// <summary>
/// Emitted when a function is blocked due to sandbox policy.
/// </summary>
public sealed record SandboxBlockedEvent : AgentEvent
{
    public string SourceName => "SandboxMiddleware";
    public string FunctionName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;

    public SandboxBlockedEvent() { }

    public SandboxBlockedEvent(string functionName, string reason)
    {
        FunctionName = functionName;
        Reason = reason;
    }
}

/// <summary>
/// Emitted when sandbox initialization fails.
/// </summary>
public sealed record SandboxErrorEvent : AgentEvent
{
    public string SourceName => "SandboxMiddleware";
    public string Message { get; init; } = string.Empty;

    public SandboxErrorEvent() { }
    public SandboxErrorEvent(string message) => Message = message;
}

/// <summary>
/// Emitted when sandbox initialization fails but execution continues.
/// </summary>
public sealed record SandboxWarningEvent : AgentEvent
{
    public string SourceName => "SandboxMiddleware";
    public string Message { get; init; } = string.Empty;

    public SandboxWarningEvent() { }
    public SandboxWarningEvent(string message) => Message = message;
}

/// <summary>
/// Emitted when sandbox infrastructure is successfully initialized.
/// Implements IObservabilityEvent for optional observability filtering.
/// </summary>
public sealed record SandboxInitializedEvent : AgentEvent, IObservabilityEvent
{
    public SandboxTier Tier { get; init; }
    public string Platform { get; init; } = string.Empty;
    public int? HttpProxyPort { get; init; }
    public int? Socks5ProxyPort { get; init; }
}

/// <summary>
/// Emitted when an MCP server starts with sandbox protection.
/// Implements IObservabilityEvent for optional observability filtering.
/// </summary>
public sealed record SandboxServerStartedEvent : AgentEvent, IObservabilityEvent
{
    public string ServerName { get; init; } = string.Empty;
    public SandboxTier Tier { get; init; }
    public string[] AllowedDomains { get; init; } = [];
}
