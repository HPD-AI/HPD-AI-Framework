using HPD.Base;

namespace HPD.Base.Tests;

internal sealed class AllowPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.Allowed
        });
    }
}
