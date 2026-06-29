using HPD.Base.Policy;

namespace HPD.Base.Runtime.Tests;

internal sealed class DenyExistingRecordPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(request.Resource.ExistingRecord is null
            ? new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed
            }
            : new PolicyDecision
            {
                Effect = PolicyEffect.Deny,
                Outcome = PolicyOutcome.FilteredOut,
                ReasonCode = "filtered",
                SafeMessage = "Filtered."
            });
    }
}
