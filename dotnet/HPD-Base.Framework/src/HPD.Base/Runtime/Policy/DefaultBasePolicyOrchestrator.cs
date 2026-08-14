using Microsoft.Extensions.Options;
using System.Collections.Immutable;

namespace HPD.Base;

internal sealed class DefaultBasePolicyOrchestrator : IBasePolicyOrchestrator
{
    private readonly IEnumerable<IPolicyEvaluator> _evaluators;
    private readonly HPDBaseRuntimeOptions _options;
    private readonly BasePolicyAuthorityOwner? _owner;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBasePolicyOrchestrator(
        IEnumerable<IPolicyEvaluator> evaluators,
        IOptions<HPDBaseRuntimeOptions> options,
        BasePolicyAuthorityOwner? owner = null)
    {
        _evaluators = evaluators;
        _options = options.Value;
        _owner = owner;
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

        if (_owner is { Policies.Length: > 0 })
            return await EvaluateInstalledAsync(request, cancellationToken).ConfigureAwait(false);

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
                SubjectContractId = request.SubjectContractId,
                SubjectContractVersion = request.SubjectContractVersion,
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
            EffectiveWriteCheck = decision.Constraints?.WriteCheck,
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

    private async ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateInstalledAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var applied = ImmutableArray.CreateBuilder<BaseAppliedPolicyAuthority>();
        var recordFilters = new List<FilterExpression>();
        var writeChecks = new List<FilterExpression>();
        FieldMask? readMask = null;
        FieldMask? writeMask = null;

        foreach (BasePolicyRegistration registration in _owner!.Policies)
        {
            PolicyDecision decision;
            try
            {
                decision = await registration.Evaluator.EvaluateAsync(CreateEvaluationRequest(request), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                return OperationResults.Unsupported<BasePolicyEvaluation>(new BaseError
                {
                    Code = "base.policy.evaluatorUnavailable",
                    Message = "Policy evaluation is unavailable.",
                    Category = ErrorCategory.Capability,
                });
            }

            if (decision is null || !Enum.IsDefined(decision.Effect) || !Enum.IsDefined(decision.Outcome))
                return InvalidAuthority();
            if (decision.Effect == PolicyEffect.Deny)
                return Denied(decision);
            if (decision.Effect == PolicyEffect.Abstain)
            {
                if (decision.Constraints is not null) return InvalidAuthority();
                continue;
            }
            if (decision.Obligations?.Any(static obligation => obligation.Enforcement == ObligationEnforcement.Required) == true)
                return OperationResults.Unsupported<BasePolicyEvaluation>(new BaseError
                {
                    Code = "base.runtime.policy.obligation.unsupported",
                    Message = "Policy returned a required obligation that this runtime cannot enforce.",
                    Category = ErrorCategory.Unsupported,
                });

            BasePolicyAuthorityDefinition definition = registration.Definition;
            applied.Add(new BaseAppliedPolicyAuthority
            {
                CompositionOrder = definition.CompositionOrder,
                PolicyId = new string(definition.Id.AsSpan()),
                PolicyVersion = definition.Version,
                PolicyChecksum = [.. BasePolicyAuthorityCanonicalizer.HashPolicyDefinition(definition)],
            });
            if (decision.Constraints?.RecordFilter is { } filter) recordFilters.Add(filter);
            if (decision.Constraints?.WriteCheck is { } check) writeChecks.Add(check);
            readMask = IntersectMasks(request.Collection, readMask, decision.Constraints?.ReadMask);
            writeMask = IntersectMasks(request.Collection, writeMask, decision.Constraints?.WriteMask);
        }

        if (applied.Count == 0)
            return OperationResults.PolicyDenied<BasePolicyEvaluation>(new BaseError
            {
                Code = "base.runtime.policy.denied",
                Message = "Policy denied the operation.",
                Category = ErrorCategory.Authorization,
            });

        FilterExpression? effectiveFilter = Conjoin(recordFilters);
        FilterExpression? effectiveWriteCheck = Conjoin(writeChecks);
        var constraints = new BasePolicyConstraintAuthority
        {
            EffectiveRecordFilter = effectiveFilter,
            EffectiveWriteCheck = effectiveWriteCheck,
            EffectiveReadMask = readMask,
            EffectiveWriteMask = writeMask,
        };
        byte[] checksum = BasePolicyAuthorityCanonicalizer.Hash(writer =>
        {
            BasePolicyAuthorityCanonicalizer.Write(writer, "base.policy.evaluationAuthority.v1");
            BasePolicyAuthorityCanonicalizer.Write(writer, _owner.ApplicationId);
            BasePolicyAuthorityCanonicalizer.Write(writer, _owner.Generation);
            writer.Write(_owner.Checksum.Length); writer.Write(_owner.Checksum);
            BasePolicyAuthorityCanonicalizer.Write(writer, applied.Count);
            foreach (BaseAppliedPolicyAuthority policy in applied)
            {
                BasePolicyAuthorityCanonicalizer.Write(writer, policy.CompositionOrder);
                BasePolicyAuthorityCanonicalizer.Write(writer, policy.PolicyId);
                BasePolicyAuthorityCanonicalizer.Write(writer, policy.PolicyVersion);
                writer.Write(policy.PolicyChecksum.Length); writer.Write(policy.PolicyChecksum.AsSpan());
            }
        });
        var authority = new BasePolicyEvaluationAuthority
        {
            PolicyGraphGeneration = _owner.Generation,
            PolicyOwnerChecksum = [.. _owner.Checksum],
            AdmittedGrants = [],
            AppliedPolicies = applied.ToImmutable(),
            Constraints = constraints,
            Checksum = BasePolicyEvaluationAuthorityChecksum.Create(checksum),
        };
        return OperationResults.Ok(new BasePolicyEvaluation
        {
            Decision = PolicyDecision.Allow(),
            EffectiveRecordFilter = effectiveFilter,
            EffectiveWriteCheck = effectiveWriteCheck,
            EffectiveReadMask = readMask,
            EffectiveWriteMask = writeMask,
            Authority = authority,
        });
    }

    private static PolicyEvaluationRequest CreateEvaluationRequest(BasePolicyRequest request) => new()
    {
        Operation = request.Operation,
        Principal = request.Principal,
        Collection = request.Collection,
        Resource = new PolicyResource
        {
            Kind = request.ResourceKind, Query = request.Query, ExistingRecord = request.ExistingRecord,
            ProposedPayload = request.ProposedPayload, ProposedRecord = request.ProposedRecord,
            RecordId = request.RecordId?.Value, VectorIndexId = request.VectorIndexId,
            VectorSpaceId = request.VectorSpaceId, SubjectContractId = request.SubjectContractId,
            SubjectContractVersion = request.SubjectContractVersion,
        },
        Grants = request.Grants,
        PolicyRefs = request.PolicyRefs,
    };

    private static OperationResult<BasePolicyEvaluation> Denied(PolicyDecision decision) =>
        OperationResults.PolicyDenied<BasePolicyEvaluation>(new BaseError
        {
            Code = decision.ReasonCode ?? "base.runtime.policy.denied",
            Message = decision.SafeMessage ?? "Policy denied the operation.",
            Category = ErrorCategory.Authorization,
        });

    private static OperationResult<BasePolicyEvaluation> InvalidAuthority() =>
        OperationResults.PolicyDenied<BasePolicyEvaluation>(new BaseError
        {
            Code = BasePolicyAuthorityErrorCodes.Invalid,
            Message = "The mutation policy authority is invalid.",
            Category = ErrorCategory.Authorization,
        });

    private static FilterExpression? Conjoin(List<FilterExpression> values) => values.Count switch
    {
        0 => null,
        1 => values[0],
        _ => new FilterExpression { Kind = FilterNodeKind.And, Children = [.. values] },
    };

    private static FieldMask? IntersectMasks(CollectionDefinition collection, FieldMask? left, FieldMask? right)
    {
        if (right is null or { Mode: FieldMaskMode.Unspecified or FieldMaskMode.AllowAll }) return left;
        if (left is null or { Mode: FieldMaskMode.Unspecified or FieldMaskMode.AllowAll }) return CloneMask(right);
        HashSet<string> eligible = (collection.Fields ?? []).Select(static field => field.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> intersection = Allowed(left, eligible);
        intersection.IntersectWith(Allowed(right, eligible));
        return intersection.Count == 0 ? new FieldMask { Mode = FieldMaskMode.DenyAll }
            : intersection.SetEquals(eligible) ? new FieldMask { Mode = FieldMaskMode.AllowAll }
            : new FieldMask { Mode = FieldMaskMode.IncludeOnly, Include = [.. intersection.Order(StringComparer.Ordinal)] };
    }

    private static HashSet<string> Allowed(FieldMask mask, HashSet<string> eligible) => mask.Mode switch
    {
        FieldMaskMode.DenyAll => new(StringComparer.Ordinal),
        FieldMaskMode.IncludeOnly => (mask.Include ?? []).Where(eligible.Contains).ToHashSet(StringComparer.Ordinal),
        FieldMaskMode.Exclude => eligible.Where(value => !(mask.Exclude ?? []).Contains(value, StringComparer.Ordinal)).ToHashSet(StringComparer.Ordinal),
        _ => new HashSet<string>(eligible, StringComparer.Ordinal),
    };

    private static FieldMask CloneMask(FieldMask value) => value with
    {
        Include = value.Include?.ToArray(), Exclude = value.Exclude?.ToArray(),
    };
}
