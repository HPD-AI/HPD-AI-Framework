using HPD.Agent.Middleware;

namespace HPD.Agent.Sandbox;

public static class SandboxRuntimeCapabilityExtensions
{
    public static ISandboxedProcessRunner GetSandboxedProcessRunner(
        this FunctionExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.RuntimeCapabilities.GetRequired<ISandboxedProcessRunner>();
    }

    public static Task<SandboxedProcessResult> RunSandboxedAsync(
        this FunctionExecutionContext context,
        SandboxedProcessCommand command,
        SandboxedProcessOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetSandboxedProcessRunner().RunAsync(
            command,
            context.SandboxConfigOverride,
            options,
            cancellationToken);
    }

    public static Task<ISandboxedProcessHandle> StartSandboxedAsync(
        this FunctionExecutionContext context,
        SandboxedProcessCommand command,
        SandboxedProcessOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetSandboxedProcessRunner().StartAsync(
            command,
            context.SandboxConfigOverride,
            options,
            cancellationToken);
    }

    public static TCapability GetRequiredRuntimeCapability<TCapability>(
        this FunctionExecutionContext context)
        where TCapability : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.RuntimeCapabilities.GetRequired<TCapability>();
    }
}
