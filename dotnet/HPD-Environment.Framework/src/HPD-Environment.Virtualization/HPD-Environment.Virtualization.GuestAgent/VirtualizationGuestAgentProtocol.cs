namespace HPD.Environment.Virtualization.GuestAgent;

/// <summary>
/// Shared guest-agent wire vocabulary. Hypervisor-specific payloads remain in
/// their provider assemblies while operation names, transport states, and
/// readiness states remain portable.
/// </summary>
public static class VirtualizationGuestAgentProtocol
{
    public const string CurrentVersion = "1.0";
}

/// <summary>Direction of one guest-agent protocol message.</summary>
public enum VirtualizationGuestAgentMessageType
{
    Request,
    Response,
    Event,
}

/// <summary>Outcome status carried by a guest-agent response.</summary>
public enum VirtualizationGuestAgentResponseStatus
{
    Ok,
    Accepted,
    Error,
}

/// <summary>Portable guest-agent operations implemented by virtualization providers.</summary>
public enum VirtualizationGuestAgentOperation
{
    Hello,
    Health,
    Ready,
    Capabilities,
    ProjectionMount,
    ProjectionStatus,
    ProjectionUnmount,
    ProjectionObserve,
    ProjectionSync,
    ProjectionFinalize,
    ProjectionEnumerateChanges,
    ProjectionPromote,
    ProcessStart,
    ProcessStatus,
    ProcessStdin,
    ProcessCloseStdin,
    ProcessSignal,
    ProcessStop,
    ProcessResize,
    ProcessWait,
    ProcessReadOutput,
    NetworkStatus,
    AuthorityBind,
    AuthorityStatus,
    AuthorityRevoke,
    EngineStatus,
    EngineProvision,
    HostShutdown,
}

/// <summary>Portable events emitted by a virtualization guest agent.</summary>
public enum VirtualizationGuestAgentEventKind
{
    GuestAgentReady,
    GuestAgentDegraded,
    ProjectionMounted,
    ProjectionMountVerificationFailed,
    ProjectionObserved,
    ProjectionSynced,
    ProjectionFinalized,
    ProjectionPromoted,
    ProcessStarted,
    ProcessOutput,
    ProcessExited,
    NetworkObserved,
    AuthorityProjected,
    AuthorityRevoked,
    EngineObserved,
    EngineDegraded,
    EngineProvisioned,
    Diagnostic,
}

/// <summary>Connection state of the hypervisor-to-guest control transport.</summary>
public enum VirtualizationGuestAgentTransportState
{
    NotAttempted,
    WaitingForVmRunning,
    Connecting,
    Connected,
    Refused,
    Timeout,
    Unsupported,
    Failed,
}

/// <summary>Transport mechanism used to reach the guest agent.</summary>
public enum VirtualizationGuestAgentTransportKind
{
    VirtioSocket,
    SerialPort,
    Network,
    ProviderDefined,
}

/// <summary>Address information for a guest-agent transport.</summary>
public sealed record VirtualizationGuestAgentTransportEndpoint
{
    public VirtualizationGuestAgentTransportKind Kind { get; init; } =
        VirtualizationGuestAgentTransportKind.VirtioSocket;
    public uint? Port { get; init; }
    public string? Address { get; init; }
    public string? Name { get; init; }
}

/// <summary>Readiness state of a virtual-machine guest agent.</summary>
public enum VirtualizationGuestAgentReadinessState
{
    NotAttempted,
    TransportNotConnected,
    Handshaking,
    Ready,
    NotReady,
    IncompatibleProtocol,
    IncompatibleAgentVersion,
    MissingCapability,
    MalformedFrame,
    GuestAgentError,
    Disconnected,
    Timeout,
    Failed,
}
