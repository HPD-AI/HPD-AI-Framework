using HPD.Base;

namespace HPD.Base.Tests;

internal sealed class DenyPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Deny,
            Outcome = PolicyOutcome.Denied,
            ReasonCode = "denied",
            SafeMessage = "Denied."
        });
    }
}
