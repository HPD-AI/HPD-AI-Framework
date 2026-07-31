
namespace HPD.Base;

/// <summary>
/// Explains admin-safe BASE runtime policy behavior without executing store mutations.
/// </summary>
public interface IBasePolicyExplainService
{
    /// <summary>
    /// Explains the policy behavior for a simulated BASE operation.
    /// </summary>
    ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainAsync(
        BasePolicyExplainRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);
}
