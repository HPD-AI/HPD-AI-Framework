using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBasePolicyOrchestrator : IBasePolicyOrchestrator
{
    private readonly IEnumerable<IPolicyEvaluator> _evaluators;
    private readonly HPDBaseRuntimeOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBasePolicyOrchestrator(
        IEnumerable<IPolicyEvaluator> evaluators,
        IOptions<HPDBaseRuntimeOptions> options)
    {
        _evaluators = evaluators;
        _options = options.Value;
    }

    /// <summary>Executes the evaluate read async operation.</summary>
    public ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateReadAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(request, cancellationToken);

    /// <summary>Executes the evaluate write async operation.</summary>
    public ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateWriteAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(request, cancellationToken);

    private async ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var evaluator = _evaluators.FirstOrDefault();
        if (evaluator is null)
        {
            if (_options.AllowPolicyAbstainAsAllowForDevelopment)
            {
                return OperationResults.Ok(Allowed());
            }

            return OperationResults.PolicyDenied<BasePolicyEvaluation>(new BaseError
            {
                Code = "base.runtime.policy.unavailable",
                Message = "No policy evaluator is registered.",
                Category = ErrorCategory.Authorization
            });
        }

        var decision = await evaluator.EvaluateAsync(new PolicyEvaluationRequest
        {
            Operation = request.Operation,
            Principal = request.Principal,
            Collection = request.Collection,
            Resource = new PolicyResource
            {
                Kind = request.ResourceKind,
                Query = request.Query,
                ExistingRecord = request.ExistingRecord,
                ProposedPayload = request.ProposedPayload,
                ProposedRecord = request.ProposedRecord,
                RecordId = request.RecordId?.Value,
                VectorIndexId = request.VectorIndexId,
                VectorSpaceId = request.VectorSpaceId,
            },
            Grants = request.Grants,
            PolicyRefs = request.PolicyRefs
        }, cancellationToken).ConfigureAwait(false);

        if (decision.Effect != PolicyEffect.Allow && (decision.Effect != PolicyEffect.Abstain || !_options.AllowPolicyAbstainAsAllowForDevelopment))
        {
            return OperationResults.PolicyDenied<BasePolicyEvaluation>(new BaseError
            {
                Code = decision.ReasonCode ?? "base.runtime.policy.denied",
                Message = decision.SafeMessage ?? "Policy denied the operation.",
                Category = ErrorCategory.Authorization,
                Policy = new PolicyErrorInfo { ReasonCode = decision.ReasonCode }
            });
        }

        var requiredObligation = decision.Obligations?.FirstOrDefault(obligation => obligation.Enforcement == ObligationEnforcement.Required);
        if (requiredObligation is not null)
        {
            return OperationResults.Unsupported<BasePolicyEvaluation>(new BaseError
            {
                Code = "base.runtime.policy.obligation.unsupported",
                Message = "Policy returned a required obligation that this runtime cannot enforce.",
                Category = ErrorCategory.Unsupported,
                Target = requiredObligation.Kind,
                Policy = new PolicyErrorInfo
                {
                    ReasonCode = requiredObligation.Code ?? requiredObligation.Kind,
                    Obligations = [requiredObligation.Kind]
                }
            });
        }

        return OperationResults.Ok(new BasePolicyEvaluation
        {
            Decision = decision,
            EffectiveRecordFilter = decision.Constraints?.RecordFilter,
            EffectiveReadMask = decision.Constraints?.ReadMask,
            EffectiveWriteMask = decision.Constraints?.WriteMask
        });
    }

    private static BasePolicyEvaluation Allowed() => new()
    {
        Decision = new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.Allowed
        }
    };
}
