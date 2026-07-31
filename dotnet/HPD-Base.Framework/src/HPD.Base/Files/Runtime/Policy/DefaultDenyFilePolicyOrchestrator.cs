using HPD.Base.Files.Policy;
using HPD.Base.Files.Runtime;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Files.Policy;

internal sealed class DefaultDenyFilePolicyOrchestrator : IFilePolicyOrchestrator
{
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
