using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBasePolicyOrchestrator : IBasePolicyOrchestrator
{
    private readonly BasePolicyAuthorityOwner? _owner;
    private readonly HPDBaseRuntimeOptions _options;
    private readonly IServiceProvider _services;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBasePolicyOrchestrator(
        BasePolicyAuthorityOwner? owner = null,
        IOptions<HPDBaseRuntimeOptions>? options = null,
        IServiceProvider? services = null)
    {
        _owner = owner;
        _options = options?.Value ?? HPDBaseRuntimeOptions.CreateDefault();
        _services = services ?? EmptyServiceProvider.Instance;
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
        return OperationResults.PolicyDenied<BasePolicyEvaluation>(new BaseError
        {
            Code = "base.runtime.policy.unavailable",
            Message = "No graph-owned policy authority is installed.",
            Category = ErrorCategory.Authorization
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
        var emitted = new List<BaseEmittedGrant>();
        foreach (BaseGrantRegistration registration in _owner!.Grants)
        {
            if (registration.StaticGrant is not null)
            {
                emitted.Add(new BaseEmittedGrant(registration.Registration, BasePolicyAuthorityCanonicalizer.CloneGrant(registration.StaticGrant)));
                continue;
            }
            if (registration.Source is null) return InvalidAuthority();
            var context = new BaseGrantAuthorityEmissionContext(request.Principal, request.Operation, registration.SourceOwner);
            try { await registration.Source.EmitAsync(context, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return InvalidAuthority(); }
            foreach (BaseEmittedGrant value in context.Complete()) emitted.Add(value);
        }
        if (emitted.GroupBy(static value => (value.Registration.Id, value.Registration.Version)).Any(static group => group.Count() > 1))
            return InvalidAuthority();
        BaseEmittedGrant[] orderedGrants = emitted.OrderBy(static value => value.Registration.Id, StringComparer.Ordinal)
            .ThenBy(static value => value.Registration.Version)
            .ThenBy(static value => Convert.ToHexString(value.Registration.Checksum), StringComparer.Ordinal)
            .ThenBy(static value => Convert.ToHexString(BasePolicyAuthorityCanonicalizer.HashGrant(value.Grant)), StringComparer.Ordinal)
            .ToArray();
        AccessGrant[] evaluatorGrants = orderedGrants.Select(static value => BasePolicyAuthorityCanonicalizer.CloneGrant(value.Grant)).ToArray();
        ImmutableArray<BaseAdmittedGrantAuthority> admitted = [.. orderedGrants.Select(static value => new BaseAdmittedGrantAuthority
        {
            GrantId = new string(value.Registration.Id.AsSpan()), GrantVersion = value.Registration.Version,
            GrantRegistrationChecksum = value.Registration.Checksum.ToArray().ToImmutableArray(),
            GrantChecksum = BasePolicyAuthorityCanonicalizer.HashGrant(value.Grant).ToImmutableArray(),
        })];
        var applied = ImmutableArray.CreateBuilder<BaseAppliedPolicyAuthority>();
        var recordFilters = new List<FilterExpression>();
        var writeChecks = new List<FilterExpression>();
        FieldMask? readMask = null;
        FieldMask? writeMask = null;
        PolicyDecision? soleAppliedDecision = null;
        int appliedDecisionCount = 0;

        foreach (BasePolicyRegistration registration in _owner!.Policies)
        {
            PolicyDecision decision;
            try
            {
                decision = await registration.Resolve(_services).EvaluateAsync(CreateEvaluationRequest(request, evaluatorGrants), cancellationToken).ConfigureAwait(false);
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
            {
                PolicyObligation obligation = decision.Obligations.First(static value => value.Enforcement == ObligationEnforcement.Required);
                return new OperationResult<BasePolicyEvaluation>
                {
                    Status = OperationStatus.Unsupported,
                    Value = new BasePolicyEvaluation { Decision = decision },
                    Error = new BaseError
                    {
                        Code = "base.runtime.policy.obligation.unsupported",
                        Message = "Policy returned a required obligation that this runtime cannot enforce.",
                        Category = ErrorCategory.Unsupported,
                        Target = obligation.Kind,
                    },
                };
            }

            BasePolicyAuthorityDefinition definition = registration.Definition;
            applied.Add(new BaseAppliedPolicyAuthority
            {
                CompositionOrder = definition.CompositionOrder,
                PolicyId = new string(definition.Id.AsSpan()),
                PolicyVersion = definition.Version,
                PolicyChecksum = [.. BasePolicyAuthorityCanonicalizer.HashPolicyDefinition(definition)],
            });
            soleAppliedDecision = decision;
            appliedDecisionCount++;
            if (decision.Constraints?.RecordFilter is { } filter) recordFilters.Add(filter);
            if (decision.Constraints?.WriteCheck is { } check) writeChecks.Add(check);
            readMask = IntersectMasks(request.Collection, readMask, decision.Constraints?.ReadMask);
            writeMask = IntersectMasks(request.Collection, writeMask, decision.Constraints?.WriteMask);
        }

        if (applied.Count == 0)
        {
            if (_options.AllowPolicyAbstainAsAllowForDevelopment
                && request.Operation.Mode != OperationMode.System)
                return OperationResults.Ok(new BasePolicyEvaluation
                {
                    Decision = new PolicyDecision
                    {
                        Effect = PolicyEffect.Abstain,
                        Outcome = PolicyOutcome.Allowed,
                        ReasonCode = "abstain",
                    },
                });
            return new OperationResult<BasePolicyEvaluation>
            {
                Status = OperationStatus.PolicyDenied,
                Value = new BasePolicyEvaluation
                {
                    Decision = new PolicyDecision
                    {
                        Effect = PolicyEffect.Abstain,
                        Outcome = PolicyOutcome.Denied,
                        ReasonCode = "abstain",
                    },
                },
                Error = new BaseError
                {
                    Code = "base.runtime.policy.denied",
                    Message = "Policy denied the operation.",
                    Category = ErrorCategory.Authorization,
                },
            };
        }

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
            BasePolicyAuthorityCanonicalizer.Write(writer, admitted.Length);
            foreach (BaseAdmittedGrantAuthority grant in admitted)
            {
                BasePolicyAuthorityCanonicalizer.Write(writer, grant.GrantId);
                BasePolicyAuthorityCanonicalizer.Write(writer, grant.GrantVersion);
                writer.Write(grant.GrantRegistrationChecksum.Length); writer.Write(grant.GrantRegistrationChecksum.AsSpan());
                writer.Write(grant.GrantChecksum.Length); writer.Write(grant.GrantChecksum.AsSpan());
            }
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
            AdmittedGrants = admitted,
            AppliedPolicies = applied.ToImmutable(),
            Constraints = constraints,
            Checksum = BasePolicyEvaluationAuthorityChecksum.Create(checksum),
        };
        bool hasConstraints = effectiveFilter is not null
            || effectiveWriteCheck is not null
            || readMask is not null
            || writeMask is not null;
        bool hasDecisionConstraints = hasConstraints
            || appliedDecisionCount == 1 && soleAppliedDecision?.Constraints?.Tags is { Count: > 0 };
        return OperationResults.Ok(new BasePolicyEvaluation
        {
            Decision = new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = hasConstraints ? PolicyOutcome.AllowedWithConstraints : PolicyOutcome.Allowed,
                Constraints = hasDecisionConstraints ? new PolicyConstraints
                {
                    RecordFilter = effectiveFilter,
                    WriteCheck = effectiveWriteCheck,
                    ReadMask = readMask,
                    WriteMask = writeMask,
                    Tags = appliedDecisionCount == 1 ? soleAppliedDecision?.Constraints?.Tags : null,
                } : null,
                Obligations = appliedDecisionCount == 1 ? soleAppliedDecision?.Obligations : null,
                Audit = appliedDecisionCount == 1 ? soleAppliedDecision?.Audit : null,
                ReasonCode = appliedDecisionCount == 1 ? soleAppliedDecision?.ReasonCode : null,
                SafeMessage = appliedDecisionCount == 1 ? soleAppliedDecision?.SafeMessage : null,
            },
            EffectiveRecordFilter = effectiveFilter,
            EffectiveWriteCheck = effectiveWriteCheck,
            EffectiveReadMask = readMask,
            EffectiveWriteMask = writeMask,
            Authority = authority,
        });
    }

    private static PolicyEvaluationRequest CreateEvaluationRequest(BasePolicyRequest request, AccessGrant[] grants) => new()
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
        Grants = grants,
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

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        internal static EmptyServiceProvider Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }
}
