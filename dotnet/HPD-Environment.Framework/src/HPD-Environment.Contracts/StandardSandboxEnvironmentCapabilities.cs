namespace HPD.Environment.Contracts;

/// <summary>
/// Stable capability identifiers reported by sandbox-capable Environment
/// providers and evaluated by Agent Sandbox admission.
/// </summary>
public static class StandardSandboxEnvironmentCapabilities
{
    public const string Prefix = "hpd.environment.sandbox.";

    /// <summary>Foreground process execution through an Environment process provider.</summary>
    public static CapabilityId ProcessExecution { get; } =
        new(Prefix + "process-execution");

    /// <summary>Captured and streamed process output.</summary>
    public static CapabilityId ProcessOutput { get; } =
        new(Prefix + "process-output");

    /// <summary>Cancellation and ordered process stopping.</summary>
    public static CapabilityId ProcessCancellation { get; } =
        new(Prefix + "process-cancellation");

    /// <summary>Observation of a process after its start call returns.</summary>
    public static CapabilityId RetainedProcessObservation { get; } =
        new(Prefix + "process-retained-observation");

    /// <summary>Provider ownership of background processes.</summary>
    public static CapabilityId BackgroundProcessOwnership { get; } =
        new(Prefix + "process-background-ownership");

    /// <summary>Replay of previously captured process output.</summary>
    public static CapabilityId ProcessOutputReplay { get; } =
        new(Prefix + "process-output-replay");

    /// <summary>Enforcement of filesystem read restrictions.</summary>
    public static CapabilityId FilesystemReadRestrictions { get; } =
        new(Prefix + "filesystem-read-restrictions");

    /// <summary>Enforcement of filesystem write restrictions.</summary>
    public static CapabilityId FilesystemWriteRestrictions { get; } =
        new(Prefix + "filesystem-write-restrictions");

    /// <summary>Enforcement of configured symbolic-link behavior.</summary>
    public static CapabilityId SymlinkHandling { get; } =
        new(Prefix + "filesystem-symlink-handling");

    /// <summary>Blocking all network egress.</summary>
    public static CapabilityId NetworkBlock { get; } =
        new(Prefix + "network-block");

    /// <summary>Restricting egress to an allowlist.</summary>
    public static CapabilityId NetworkAllowlist { get; } =
        new(Prefix + "network-allowlist");

    /// <summary>Filtering network destinations and requests.</summary>
    public static CapabilityId NetworkFiltering { get; } =
        new(Prefix + "network-filtering");

    /// <summary>Mediating filtered egress through provider-owned proxies.</summary>
    public static CapabilityId NetworkProxyMediation { get; } =
        new(Prefix + "network-proxy-mediation");

    /// <summary>Restricting access to Unix-domain sockets.</summary>
    public static CapabilityId UnixSocketRestriction { get; } =
        new(Prefix + "unix-socket-restriction");

    /// <summary>Filtering and injecting process environment variables.</summary>
    public static CapabilityId EnvironmentFiltering { get; } =
        new(Prefix + "environment-filtering");

    /// <summary>Applying the requested process identity.</summary>
    public static CapabilityId ProcessIdentity { get; } =
        new(Prefix + "process-identity");

    /// <summary>Providing a pseudo-terminal to a process.</summary>
    public static CapabilityId Pty { get; } =
        new(Prefix + "interactive-pty");

    /// <summary>Providing standard input to a process.</summary>
    public static CapabilityId StandardInput { get; } =
        new(Prefix + "interactive-stdin");

    /// <summary>Allowing the process to bind a local port.</summary>
    public static CapabilityId LocalPortBinding { get; } =
        new(Prefix + "interactive-local-binding");

    /// <summary>Enforcing process resource limits.</summary>
    public static CapabilityId ResourceLimits { get; } =
        new(Prefix + "resource-limits");

    /// <summary>Observing sandbox-policy violations.</summary>
    public static CapabilityId ViolationObservation { get; } =
        new(Prefix + "violation-observation");

    /// <summary>Creating and releasing content projections.</summary>
    public static CapabilityId ContentProjection { get; } =
        new(Prefix + "content-projection");

    /// <summary>Ordered, idempotent sandbox-session cleanup.</summary>
    public static CapabilityId SessionCleanup { get; } =
        new(Prefix + "session-cleanup");
}
