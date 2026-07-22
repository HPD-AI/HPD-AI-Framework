namespace HPD.Agent.ToolHarness.Coding.Debugging.Adapters;

public abstract class BuiltInBehavioralDebugAdapterFactory(
    StandardDebugAdapterFactory standardFactory,
    IReadOnlyList<string> probeArguments,
    DebugAdapterTransportKind transportKind = DebugAdapterTransportKind.EnvironmentStdio,
    DebugDynamicEndpointMode dynamicEndpointMode = DebugDynamicEndpointMode.None)
    : IDebugAdapterFactory
{
    public ValueTask<DebugAdapterAvailability> ProbeAsync(
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext context,
        CancellationToken cancellationToken = default)
        => standardFactory.ProbeAsync(
            descriptor,
            context with { ProbeArgumentsOverride = probeArguments },
            cancellationToken);

    public async ValueTask<DebugAdapterLaunchPlan> CreateLaunchPlanAsync(
        DebugAdapterDescriptor descriptor,
        DebugLaunchContext context,
        CancellationToken cancellationToken = default)
        => Configure(await standardFactory.CreateLaunchPlanAsync(
            descriptor,
            context with { Resolution = context.Resolution with { ProbeArgumentsOverride = probeArguments } },
            cancellationToken).ConfigureAwait(false));

    public async ValueTask<DebugAdapterLaunchPlan> CreateAttachPlanAsync(
        DebugAdapterDescriptor descriptor,
        DebugAttachContext context,
        CancellationToken cancellationToken = default)
        => Configure(await standardFactory.CreateAttachPlanAsync(
            descriptor,
            context with { Resolution = context.Resolution with { ProbeArgumentsOverride = probeArguments } },
            cancellationToken).ConfigureAwait(false));

    protected virtual DebugAdapterLaunchPlan Configure(DebugAdapterLaunchPlan plan)
        => transportKind == DebugAdapterTransportKind.EnvironmentStdio
            ? plan
            : plan with
            {
                Transport = plan.Transport with
                {
                    Kind = transportKind,
                    AllocatesDynamicLoopbackEndpoint = true,
                    DynamicEndpointMode = dynamicEndpointMode
                }
            };
}

public sealed class DebugPyAdapterFactory(StandardDebugAdapterFactory standardFactory)
    : BuiltInBehavioralDebugAdapterFactory(
        standardFactory,
        ["-c", "import debugpy; import debugpy.adapter; print(debugpy.__version__)"]);

public sealed class CodeLldbAdapterFactory(StandardDebugAdapterFactory standardFactory)
    : BuiltInBehavioralDebugAdapterFactory(
        standardFactory,
        ["--version"],
        DebugAdapterTransportKind.EnvironmentTcpServer,
        DebugDynamicEndpointMode.AdapterReportsSelectedPort);

public sealed class DelveAdapterFactory(StandardDebugAdapterFactory standardFactory)
    : BuiltInBehavioralDebugAdapterFactory(
        standardFactory,
        ["version"],
        DebugAdapterTransportKind.EnvironmentTcpServer,
        DebugDynamicEndpointMode.AdapterReportsSelectedPort)
{
    protected override DebugAdapterLaunchPlan Configure(DebugAdapterLaunchPlan plan)
    {
        var arguments = plan.CommandArguments.Concat(["--listen=127.0.0.1:0"]).ToArray();
        var configured = base.Configure(plan);
        return configured with
        {
            CommandArguments = arguments,
            Transport = configured.Transport with { Arguments = arguments }
        };
    }
}

public sealed class JavaScriptDebugAdapterFactory(StandardDebugAdapterFactory standardFactory)
    : BuiltInBehavioralDebugAdapterFactory(
        standardFactory,
        ["--help"],
        DebugAdapterTransportKind.EnvironmentTcpServer,
        DebugDynamicEndpointMode.AppendSelectedPortAndLoopbackHost);
