
namespace HPD.Base;

internal sealed class DefaultDenyFilePolicyOrchestrator : IFilePolicyOrchestrator
{
    /// <summary>Executes the evaluate async operation.</summary>
    public ValueTask<OperationResult<FilePolicyEvaluation>> EvaluateAsync(FilePolicyRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(OperationResults.PolicyDenied<FilePolicyEvaluation>(new BaseError
        {
            Code = FileDiagnosticIds.PolicyUnavailable,
            Message = "File policy is not configured.",
            Category = ErrorCategory.Authorization,
            Target = request.Resource.Bucket.BucketId.Value
        }));
    }
}
