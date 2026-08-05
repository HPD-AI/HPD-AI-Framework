namespace HPD.Base;

/// <summary>Defines the ipolicy evaluator contract.</summary>
public interface IPolicyEvaluator
{
    /// <summary>Executes the evaluate async operation.</summary>
    ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
