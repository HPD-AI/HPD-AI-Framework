namespace HPD.Agent.ToolHarness.Coding.Debugging;

public interface IDebugAdapterTrustPolicy
{
    DebugAdapterTrustDecision Evaluate(DebugAdapterDescriptor descriptor);
}

public sealed class DenyByDefaultDebugAdapterTrustPolicy : IDebugAdapterTrustPolicy
{
    public DebugAdapterTrustDecision Evaluate(DebugAdapterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new DebugAdapterTrustDecision
        {
            TrustLevel = DebugAdapterTrustLevel.Denied,
            PolicyRevision = "default-deny-v1",
            ReasonCode = "HOST_TRUST_POLICY_NOT_CONFIGURED"
        };
    }
}
