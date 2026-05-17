namespace HPD.Sandbox.Local.Events;

/// <summary>
/// Event type constants for sandbox events.
/// Follows HPD-Agent's SCREAMING_SNAKE_CASE convention.
/// </summary>
public static class SandboxEventTypes
{
    /// <summary>Sandbox violation detected (filesystem or network)</summary>
    public const string SANDBOX_VIOLATION = "SANDBOX_VIOLATION";

    /// <summary>Function blocked due to sandbox policy</summary>
    public const string SANDBOX_BLOCKED = "SANDBOX_BLOCKED";

    /// <summary>Sandbox initialization error</summary>
    public const string SANDBOX_ERROR = "SANDBOX_ERROR";

    /// <summary>Sandbox initialization warning (non-fatal)</summary>
    public const string SANDBOX_WARNING = "SANDBOX_WARNING";

    /// <summary>Sandbox successfully initialized</summary>
    public const string SANDBOX_INITIALIZED = "SANDBOX_INITIALIZED";

    /// <summary>MCP server started with sandbox</summary>
    public const string SANDBOX_SERVER_STARTED = "SANDBOX_SERVER_STARTED";

    /// <summary>Sandboxed process is about to start</summary>
    public const string SANDBOX_PROCESS_STARTING = "SANDBOX_PROCESS_STARTING";

    /// <summary>Sandboxed process started</summary>
    public const string SANDBOX_PROCESS_STARTED = "SANDBOX_PROCESS_STARTED";

    /// <summary>Sandboxed process completed</summary>
    public const string SANDBOX_PROCESS_COMPLETED = "SANDBOX_PROCESS_COMPLETED";

    /// <summary>Sandboxed process failed</summary>
    public const string SANDBOX_PROCESS_FAILED = "SANDBOX_PROCESS_FAILED";

    /// <summary>Sandboxed process timed out</summary>
    public const string SANDBOX_PROCESS_TIMED_OUT = "SANDBOX_PROCESS_TIMED_OUT";

    /// <summary>Sandboxed process was cancelled</summary>
    public const string SANDBOX_PROCESS_CANCELLED = "SANDBOX_PROCESS_CANCELLED";

    /// <summary>Sandboxed process was killed by cleanup</summary>
    public const string SANDBOX_PROCESS_KILLED = "SANDBOX_PROCESS_KILLED";
}
