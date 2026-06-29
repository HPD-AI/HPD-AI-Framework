namespace HPD.Base.Policy;

public interface IPolicyEvaluator
{
    ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
