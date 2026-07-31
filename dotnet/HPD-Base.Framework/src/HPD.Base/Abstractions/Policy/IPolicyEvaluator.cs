namespace HPD.Base;

public interface IPolicyEvaluator
{
    ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
