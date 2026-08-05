using HPD.Base;

namespace HPD.Base.Tests;

internal sealed class AbstainPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Abstain,
            Outcome = PolicyOutcome.Unsupported,
            ReasonCode = "abstain"
        });
    }
}
