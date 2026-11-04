namespace HPD.Environment.Virtualization.Contracts;

using HPD.Environment.Contracts;

/// <summary>Identifies the hypervisor acceleration mechanism required by a VM.</summary>
public enum VirtualMachineAcceleratorKind
{
    None,
    AppleVirtualization,
    Kvm,
    HyperV,
    WindowsHypervisorPlatform,
    Software,
    ProviderDefined,
}

/// <summary>
/// Describes host support, guest platform, and acceleration requirements for a
/// virtual machine without selecting a concrete hypervisor.
/// </summary>
public sealed record VirtualMachinePlatformRequirement
{
    public required PlatformSpec HostPlatform { get; init; }
    public required PlatformSpec GuestPlatform { get; init; }
    public required ExecutionMode ExecutionMode { get; init; }
    public VirtualMachineAcceleratorKind Accelerator { get; init; } =
        VirtualMachineAcceleratorKind.ProviderDefined;
    public IReadOnlyList<string> RequiredHostFeatures { get; init; } = [];
}

/// <summary>Identifies the role of a disk attached to a virtual machine.</summary>
public enum VirtualMachineDiskRole
{
    System,
    Data,
    Runtime,
    Scratch,
    CloudInit,
    ProviderDefined,
}

/// <summary>
/// A VM disk attachment that reuses Environment storage resources while
/// assigning a provider-neutral disk role.
/// </summary>
public sealed record VirtualMachineDiskAttachment
{
    public required VirtualMachineDiskRole Role { get; init; }
    public ResourceRef<DurableVolume>? Volume { get; init; }
    public ResourceRef<BlockVolume>? BlockVolume { get; init; }
    public GuestPath? GuestPath { get; init; }
    public VolumeAccessMode AccessMode { get; init; } =
        VolumeAccessMode.ReadWrite;
}

/// <summary>
/// VM boot intent composed from the common runtime-host boot artifact model.
/// </summary>
public sealed record VirtualMachineBootPlan
{
    public IReadOnlyList<RuntimeHostBootArtifactSpec> Artifacts { get; init; } = [];
    public ResourceRef<ContentArtifact>? GuestImage { get; init; }
    public string? KernelCommandLine { get; init; }
    public bool ExpectGuestControl { get; init; } = true;
}

/// <summary>
/// VM-specific lifecycle expectations layered over the common runtime-host
/// lifecycle policy.
/// </summary>
public sealed record VirtualMachineLifecycleRequirements
{
    public RuntimeHostLifecyclePolicy Host { get; init; } =
        RuntimeHostLifecyclePolicy.Default;
    public bool RequireGuestControlForReadiness { get; init; } = true;
    public bool RequireExplicitShutdown { get; init; } = true;
    public bool SupportsSuspend { get; init; } = true;
}

/// <summary>
/// Byte-oriented guest-control transport shared by hypervisor providers.
/// Concrete transports own framing, authentication, and connection lifecycle.
/// </summary>
public interface IVirtualMachineGuestTransport : IAsyncDisposable
{
    ValueTask SendAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(
        CancellationToken cancellationToken = default);
}
