using HPD.Environment.Contracts;
using HPD.Environment.Virtualization.Contracts;
using Xunit;

namespace HPD.Environment.Contracts.Tests;

public sealed class VirtualizationContractsTests
{
    [Fact]
    public void Vm_platform_requirement_uses_common_platform_types()
    {
        var requirement = new VirtualMachinePlatformRequirement
        {
            HostPlatform = new PlatformSpec("macos", "arm64"),
            GuestPlatform = new PlatformSpec("linux", "arm64"),
            ExecutionMode = ExecutionMode.Native,
            Accelerator =
                VirtualMachineAcceleratorKind.AppleVirtualization,
        };

        Assert.Equal("macos", requirement.HostPlatform.OperatingSystem);
        Assert.Equal("linux", requirement.GuestPlatform.OperatingSystem);
        Assert.Equal(
            VirtualMachineAcceleratorKind.AppleVirtualization,
            requirement.Accelerator);
    }

    [Fact]
    public void Vm_disk_roles_are_virtualization_specific()
    {
        Assert.Contains(VirtualMachineDiskRole.System, Enum.GetValues<VirtualMachineDiskRole>());
        Assert.Contains(VirtualMachineDiskRole.Data, Enum.GetValues<VirtualMachineDiskRole>());
        Assert.Contains(VirtualMachineDiskRole.ProviderDefined, Enum.GetValues<VirtualMachineDiskRole>());
    }

    [Fact]
    public void Vm_boot_plan_reuses_common_boot_artifact_contract()
    {
        RuntimeHostBootArtifactSpec artifact = new(
            RuntimeHostBootArtifactKind.Kernel);
        var plan = new VirtualMachineBootPlan
        {
            Artifacts = [artifact],
            KernelCommandLine = "console=ttyS0",
        };

        Assert.Same(artifact, Assert.Single(plan.Artifacts));
    }
}
