namespace HPD.Execution.AppleVirtualization.Tests.Fixtures;

using HPD.Execution.Contracts;

public static class AppleVirtualizationAcceptanceMatrix
{
    public static IReadOnlyList<AppleVirtualizationAcceptanceCase> Cases { get; } =
    [
        new(
            "boot-linux-vm-through-fake-helper",
            ProviderContractKind.RuntimeHost,
            "RuntimeHost reaches Running before Ready and preserves guest-control readiness as a separate gate.",
            "VZVirtualMachine, VZVirtualMachineConfiguration, VZLinuxBootLoader, VZDiskImageStorageDeviceAttachment",
            "AppleVirtualizationScenarioBuilder.WithHostStatus + WithGuestControlReady",
            Required: true),
        new(
            "guest-readiness",
            ProviderContractKind.RuntimeHost,
            "Ready requires guest agent handshake and command probe; VM running alone is insufficient.",
            "VZVirtioSocketDevice, VZVirtioSocketConnection, serial-ports.md, consoles.md",
            "AppleVirtualizationScenarioBuilder.WithGuestControlReady",
            Required: true),
        new(
            "projection-success",
            ProviderContractKind.ContentProjection,
            "Projection is Projected only after helper share configuration and guest mount verification.",
            "VZVirtioFileSystemDeviceConfiguration, VZSharedDirectory, shared-directories.md",
            "AppleVirtualizationScenarioBuilder.WithProjectionMounted",
            Required: true),
        new(
            "projection-fallback-degraded",
            ProviderContractKind.ContentProjection,
            "Fallback and degraded mount states remain explicit status, not silent success.",
            "VZVirtioFileSystemDevice, VZDirectoryShare, shared-directories.md",
            "AppleVirtualizationScenarioBuilder.WithProjectionFailure",
            Required: true),
        new(
            "execution-unit-creation",
            ProviderContractKind.ExecutionUnit,
            "Unit acceptance is an in-guest working-directory/session context assigned to a host.",
            "VZVirtualMachine plus guest-control protocol; no direct Apple ExecutionUnit primitive",
            "AppleVirtualizationContractFixtures.ExecutionUnitSpec + WithUnitReady",
            Required: true),
        new(
            "process-run-success",
            ProviderContractKind.ProcessInvocation,
            "Process success flows through guest-control protocol and preserves exit code/result.",
            "VZVirtioSocketDevice, VZVirtioSocketConnection, consoles.md for diagnostics only",
            "AppleVirtualizationScenarioBuilder.WithProcessStarted + WithProcessExited",
            Required: true),
        new(
            "stdout-stderr-streaming",
            ProviderContractKind.ProcessInvocation,
            "Output chunks preserve stream identity, sequence, bytes, and final/truncated flags.",
            "VZVirtioSocketConnection file descriptor transport; process semantics supplied by guest agent",
            "AppleVirtualizationScenarioBuilder.WithProcessOutput",
            Required: true),
        new(
            "cancellation-stop",
            ProviderContractKind.ProcessInvocation,
            "Cancellation/stop must map to guest-agent stop and output drain semantics.",
            "VZVirtioSocketConnection, VZVirtualMachine requestStop/stop only for host lifecycle",
            "todo provider-family fake scenario",
            Required: true),
        new(
            "timeout",
            ProviderContractKind.ProcessInvocation,
            "Timeout is a process result condition, not a helper crash by default.",
            "VZVirtioSocketConnection readiness and guest-agent wait protocol",
            "todo provider-family fake scenario",
            Required: true),
        new(
            "helper-failure",
            ProviderContractKind.Supervisor,
            "Helper crash or protocol failure increments generation and creates structured diagnostics.",
            "VZVirtualMachine owns VM object; helper process boundary owns Virtualization.framework",
            "AppleVirtualizationScenarioBuilder.WithHelperFailure",
            Required: true),
        new(
            "stale-handle",
            ProviderContractKind.Supervisor,
            "Provider generation mismatch rejects live handles after helper restart or VM recreation.",
            "VZVirtualMachine object lifetime and helper restart semantics",
            "AppleVirtualizationScenarioBuilder.WithStaleHandle",
            Required: true),
        new(
            "unsupported-host-platform",
            ProviderContractKind.RuntimeHost,
            "Non-macOS host reports unsupported platform without attempting Apple API calls.",
            "VZVirtualMachine.isSupported and entitlement article preflight surface",
            "AppleVirtualizationCapabilityReporter non-macOS query",
            Required: true),
        new(
            "port-forwarding-future",
            ProviderContractKind.EndpointPublication,
            "Endpoint publication remains deferred until host listener and guest route reconciliation exist.",
            "VZVirtioNetworkDeviceConfiguration, VZNATNetworkDeviceAttachment, sockets.md",
            "todo future endpoint provider matrix",
            Required: false),
        new(
            "docker-podman-socket-forwarding-future",
            ProviderContractKind.EngineControlPlane,
            "Engine API sockets are sensitive endpoint/authority surfaces, not first-slice primitives.",
            "sockets.md, network.md, shared-directories.md, storage.md",
            "todo future engine/authority matrix",
            Required: false),
    ];
}

public sealed record AppleVirtualizationAcceptanceCase(
    string Scenario,
    ProviderContractKind ContractKind,
    string ContractExpectation,
    string AppleApiSurface,
    string ToolHarnessCoverage,
    bool Required);
