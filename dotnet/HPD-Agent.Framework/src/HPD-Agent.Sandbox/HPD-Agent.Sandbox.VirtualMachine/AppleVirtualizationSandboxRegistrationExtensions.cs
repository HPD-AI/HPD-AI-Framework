namespace HPD.Agent.Sandbox.VirtualMachine;

using HPD.Environment.Virtualization.Apple;
using HPD.Environment.Runtime;

public static class AppleVirtualizationSandboxRegistrationExtensions
{
    public static EnvironmentProviderRegistry RegisterAppleVirtualizationSandbox(
        this EnvironmentProviderRegistry registry,
        AppleVirtualizationProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.RegisterModule(new AppleVirtualizationProviderModule(options ?? new AppleVirtualizationProviderOptions()));
        return registry;
    }
}
