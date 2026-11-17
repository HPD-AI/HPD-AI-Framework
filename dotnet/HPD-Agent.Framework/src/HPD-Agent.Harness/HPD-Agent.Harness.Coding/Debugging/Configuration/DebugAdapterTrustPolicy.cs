namespace HPD.Agent.ToolHarness.Coding.Debugging;

public interface IDebugAdapterTrustPolicy
{
    DebugAdapterTrustDecision Evaluate(DebugAdapterDescriptor descriptor);
}

internal sealed class BuiltInDebugAdapterTrustPolicy : IDebugAdapterTrustPolicy
{
    public DebugAdapterTrustDecision Evaluate(DebugAdapterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        bool builtIn = string.Equals(
            descriptor.Provenance.AssemblyName,
            typeof(BuiltInDebugAdapterTrustPolicy).Assembly.GetName().Name,
            StringComparison.Ordinal);
        return new DebugAdapterTrustDecision
        {
            TrustLevel = builtIn ? DebugAdapterTrustLevel.Trusted : DebugAdapterTrustLevel.Denied,
            PolicyRevision = "built-in-adapters-v1",
            ReasonCode = builtIn ? "BUILT_IN_ADAPTER" : "EXTERNAL_ADAPTER_REQUIRES_HOST_TRUST"
        };
    }
}
