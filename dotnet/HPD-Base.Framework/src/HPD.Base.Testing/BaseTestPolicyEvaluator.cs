using HPD.Base.Policy;

namespace HPD.Base.Testing;

/// <summary>Controls the deterministic allow/deny policy of one test host.</summary>
public sealed class BaseTestPolicy
{
    private PolicyDecision _decision = PolicyDecision.Allow();

    /// <summary>Allows subsequent operations.</summary>
    public void AllowAll() => Volatile.Write(ref _decision, PolicyDecision.Allow());

    /// <summary>Denies subsequent operations with a bounded safe error.</summary>
    public void DenyAll(
        string code = "base.testing.policyDenied",
        string safeMessage = "The test policy denied the operation.") =>
        Volatile.Write(ref _decision, PolicyDecision.Deny(code, safeMessage));

    internal PolicyDecision Current => Volatile.Read(ref _decision);
}

internal sealed class BaseTestPolicyEvaluator(BaseTestPolicy policy)
    : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(policy.Current);
}
