namespace HPD.Agent.Sandbox;

/// <summary>
/// Result of wrapping a command with sandbox restrictions.
/// Compatible with StdioClientTransportOptions used by MCP.
/// </summary>
/// <param name="FileName">The sandbox executable (e.g., "bwrap", "sandbox-exec", "docker")</param>
/// <param name="ArgumentList">Complete argument list including original command</param>
/// <param name="Environment">Additional environment variables for the sandboxed process</param>
public sealed record SandboxedCommand(
    string FileName,
    IReadOnlyList<string> ArgumentList,
    IReadOnlyDictionary<string, string>? Environment = null
);
