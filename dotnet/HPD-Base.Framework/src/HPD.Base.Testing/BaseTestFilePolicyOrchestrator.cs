using HPD.Base;

namespace HPD.Base.Testing;

internal sealed class BaseTestFilePolicyOrchestrator(BaseTestPolicy policy)
    : IFilePolicyOrchestrator
{
    public ValueTask<OperationResult<FilePolicyEvaluation>> EvaluateAsync(
        FilePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = policy.Current;
        return ValueTask.FromResult(
            decision.Effect == PolicyEffect.Allow
                ? OperationResults.Ok(new FilePolicyEvaluation
                {
                    Allowed = true,
                    Reason = "base.testing.allowed",
                })
                : OperationResults.PolicyDenied<FilePolicyEvaluation>(
                    new BaseError
                    {
                        Code = decision.ReasonCode ?? "base.testing.policyDenied",
                        Message = decision.SafeMessage ?? "The test policy denied the operation.",
                        Category = ErrorCategory.Authorization,
                    }));
    }
}
