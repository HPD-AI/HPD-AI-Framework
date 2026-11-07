namespace HPD.Agent.Security;

/// <summary>Identifies a capability controlled by the host security boundary.</summary>
public enum AgentCapabilityKind
{
    FilesystemRead,
    FilesystemWrite,
    NetworkEgress,
    LocalBinding,
    InteractiveTerminal,
    UnsandboxedExecution
}
