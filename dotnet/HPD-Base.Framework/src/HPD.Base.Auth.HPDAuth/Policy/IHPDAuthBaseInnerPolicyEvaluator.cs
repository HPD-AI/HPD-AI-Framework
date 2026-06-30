using HPD.Base.Policy;

namespace HPD.Base.Auth.HPDAuth.Policy;

/// <summary>
/// Provides an optional downstream policy evaluator composed with the HPD.Auth BASE adapter.
/// </summary>
public interface IHPDAuthBaseInnerPolicyEvaluator
{
    /// <summary>
    /// Evaluates the supplied BASE policy request.
    /// </summary>
    /// <param name="request">The policy evaluation request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The downstream policy decision.</returns>
    ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Controls how the HPD.Auth BASE adapter composes with an optional inner evaluator.
/// </summary>
public enum HPDAuthBasePolicyCompositionMode
{
    /// <summary>
    /// Evaluate only HPD.Auth adapter grants and rules.
    /// </summary>
    HPDAuthOnly,

    /// <summary>
    /// Evaluate the HPD.Auth adapter first, then let the inner evaluator further constrain an allow.
    /// </summary>
    HPDAuthThenInner,

    /// <summary>
    /// Evaluate the inner evaluator first, then let the HPD.Auth adapter further constrain an allow.
    /// </summary>
    InnerThenHPDAuth
}
