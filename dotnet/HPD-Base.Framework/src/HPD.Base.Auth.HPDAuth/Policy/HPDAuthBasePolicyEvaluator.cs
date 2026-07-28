using HPD.Base.Auth.HPDAuth.Configuration;
using HPD.Base.Auth.HPDAuth.Health;
using HPD.Base.Auth.HPDAuth.Observability;
using HPD.Base.Auth.HPDAuth.Observability.Logging;
using HPD.Base.Observability;
using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Base.Auth.HPDAuth.Policy;

/// <summary>
/// Evaluates BASE policy requests using HPD.Auth-mapped principals, configured collection rules, and optional grants.
/// </summary>
public sealed class HPDAuthBasePolicyEvaluator : IPolicyEvaluator
{
    private readonly HPDBaseHPDAuthOptions _options;
    private readonly IEnumerable<IHPDAuthBaseGrantProvider> _grantProviders;
    private readonly IEnumerable<IHPDAuthBaseHostIntegrationStatus> _hostStatuses;
    private readonly IEnumerable<IHPDAuthBaseInnerPolicyEvaluator> _innerEvaluators;
    private readonly ILogger<HPDAuthBasePolicyEvaluator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDAuthBasePolicyEvaluator"/> class.
    /// </summary>
    /// <param name="options">Adapter options.</param>
    /// <param name="grantProviders">Optional grant providers.</param>
    /// <param name="hostStatuses">Host integration status providers.</param>
    /// <param name="innerEvaluators">Optional inner policy evaluators.</param>
    /// <param name="logger">The policy evaluator logger.</param>
    public HPDAuthBasePolicyEvaluator(
        IOptions<HPDBaseHPDAuthOptions> options,
        IEnumerable<IHPDAuthBaseGrantProvider> grantProviders,
        IEnumerable<IHPDAuthBaseHostIntegrationStatus> hostStatuses,
        IEnumerable<IHPDAuthBaseInnerPolicyEvaluator> innerEvaluators,
        ILogger<HPDAuthBasePolicyEvaluator> logger)
    {
        _options = options.Value;
        _grantProviders = grantProviders;
        _hostStatuses = hostStatuses;
        _innerEvaluators = innerEvaluators;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return await HPDBaseHPDAuthTelemetry.TracePolicyAsync(
            request,
            _options.PolicyCompositionMode,
            async () => _options.PolicyCompositionMode switch
        {
            HPDAuthBasePolicyCompositionMode.HPDAuthThenInner => await EvaluateHPDAuthThenInnerAsync(request, cancellationToken).ConfigureAwait(false),
            HPDAuthBasePolicyCompositionMode.InnerThenHPDAuth => await EvaluateInnerThenHPDAuthAsync(request, cancellationToken).ConfigureAwait(false),
            _ => await EvaluateAdapterAsync(request, cancellationToken).ConfigureAwait(false)
        }).ConfigureAwait(false);
    }

    private async ValueTask<PolicyDecision> EvaluateHPDAuthThenInnerAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var adapterDecision = await EvaluateAdapterAsync(request, cancellationToken).ConfigureAwait(false);
        if (adapterDecision.Effect != PolicyEffect.Allow)
            return adapterDecision;

        var inner = _innerEvaluators.FirstOrDefault();
        if (inner is null)
            return adapterDecision;

        var innerDecision = await inner.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        return ComposeAllowedDecisions(adapterDecision, innerDecision);
    }

    private async ValueTask<PolicyDecision> EvaluateInnerThenHPDAuthAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var inner = _innerEvaluators.FirstOrDefault();
        if (inner is null)
            return await EvaluateAdapterAsync(request, cancellationToken).ConfigureAwait(false);

        var innerDecision = await inner.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (innerDecision.Effect != PolicyEffect.Allow)
            return innerDecision;

        var adapterDecision = await EvaluateAdapterAsync(request, cancellationToken).ConfigureAwait(false);
        return ComposeAllowedDecisions(innerDecision, adapterDecision);
    }

    private async ValueTask<PolicyDecision> EvaluateAdapterAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var decision = await EvaluateAdapterCoreAsync(request, cancellationToken).ConfigureAwait(false);
        if (decision.Effect == PolicyEffect.Deny)
        {
            HPDBaseHPDAuthLog.AuthPolicyDenied(
                _logger,
                HPDBaseHPDAuthLog.OperationKind(request.Operation.Operation),
                HPDBaseHPDAuthLog.PolicyReasonCode(decision.ReasonCode));
        }

        return decision;
    }

    private async ValueTask<PolicyDecision> EvaluateAdapterCoreAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var action = ActionFor(request.Operation.Operation);
        var subjects = SubjectsFor(request.Principal);

        if (RequiredHPDAuthServicesMissing())
        {
            return Deny(
                PolicyOutcome.Denied,
                "hpd.auth.base.missingAuthServices",
                "Required HPD.Auth services were not detected.",
                request,
                subjects);
        }

        if (IsAdmin(request.Principal, subjects) && _options.AllowAdminBypass)
        {
            HPDBaseHPDAuthTelemetry.RecordBypass(request, HPDBaseTelemetryValues.BypassAdmin);
            HPDBaseHPDAuthLog.PrivilegedBypassUsed(_logger, "admin");
            return Allow(PolicyOutcome.Bypassed, request, subjects, adminBypass: true);
        }

        if (IsService(request.Principal, subjects) && _options.AllowServiceBypass)
        {
            HPDBaseHPDAuthTelemetry.RecordBypass(request, HPDBaseTelemetryValues.BypassService);
            HPDBaseHPDAuthLog.PrivilegedBypassUsed(_logger, "service");
            return Allow(PolicyOutcome.Bypassed, request, subjects, serviceBypass: true);
        }

        var grants = await GrantsForAsync(request, cancellationToken).ConfigureAwait(false);
        var grantDecision = EvaluateGrants(request, grants, subjects, action);
        if (grantDecision is not null)
            return grantDecision;

        var rule = _options.CollectionRules.FirstOrDefault(candidate =>
            string.Equals(candidate.CollectionId, request.Collection.Id, StringComparison.Ordinal));
        if (rule is not null)
        {
            var ruleDecision = EvaluateRule(request, rule, subjects, action);
            if (ruleDecision is not null)
                return ruleDecision;
        }

        if (_options.RequireAuthenticatedByDefault && request.Principal.AuthenticationState == PrincipalAuthenticationState.Anonymous)
        {
            return Deny(
                PolicyOutcome.Unauthenticated,
                "hpd.auth.base.unauthenticated",
                "Authentication is required.",
                request,
                subjects);
        }

        return Deny(
            PolicyOutcome.Denied,
            "hpd.auth.base.noMatchingGrant",
            "No HPD.Auth BASE grant or rule allowed the operation.",
            request,
            subjects);
    }

    private static PolicyDecision ComposeAllowedDecisions(PolicyDecision first, PolicyDecision second)
    {
        if (second.Effect != PolicyEffect.Allow)
            return second;

        var constraints = MergeConstraints(first.Constraints, second.Constraints);
        return second with
        {
            Outcome = constraints is null ? PolicyOutcome.Allowed : PolicyOutcome.AllowedWithConstraints,
            Constraints = constraints,
            Audit = MergeAudit(first.Audit, second.Audit)
        };
    }

    private static PolicyAuditInfo? MergeAudit(PolicyAuditInfo? first, PolicyAuditInfo? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        return second with
        {
            MatchedSubjects = Concat(first.MatchedSubjects, second.MatchedSubjects),
            MatchedGrantIds = Concat(first.MatchedGrantIds, second.MatchedGrantIds),
            AdminBypass = first.AdminBypass || second.AdminBypass,
            ServiceBypass = first.ServiceBypass || second.ServiceBypass,
            CorrelationId = second.CorrelationId ?? first.CorrelationId
        };
    }

    private static T[]? Concat<T>(T[]? first, T[]? second)
    {
        if (first is null or { Length: 0 })
            return second;
        if (second is null or { Length: 0 })
            return first;

        return first.Concat(second).ToArray();
    }

    private static PolicyConstraints? MergeConstraints(PolicyConstraints? first, PolicyConstraints? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        var recordFilter = AndFilters(first.RecordFilter, second.RecordFilter);
        var readMask = MergeMasks(first.ReadMask, second.ReadMask);
        var writeMask = MergeMasks(first.WriteMask, second.WriteMask);
        var writeCheck = AndFilters(first.WriteCheck, second.WriteCheck);
        if (recordFilter is null && readMask is null && writeMask is null && writeCheck is null)
            return null;

        return new PolicyConstraints
        {
            RecordFilter = recordFilter,
            ReadMask = readMask,
            WriteMask = writeMask,
            WriteCheck = writeCheck,
            Tags = MergeTags(first.Tags, second.Tags)
        };
    }

    private static Dictionary<string, string>? MergeTags(Dictionary<string, string>? first, Dictionary<string, string>? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        var merged = new Dictionary<string, string>(first, StringComparer.Ordinal);
        foreach (var pair in second)
            merged[pair.Key] = pair.Value;
        return merged;
    }

    private static FilterExpression? AndFilters(FilterExpression? first, FilterExpression? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        return new FilterExpression
        {
            Kind = FilterNodeKind.And,
            Children = [first, second]
        };
    }

    private static FieldMask? MergeMasks(FieldMask? first, FieldMask? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        var include = IncludeFields(first).Intersect(IncludeFields(second), StringComparer.Ordinal).ToArray();
        var exclude = ExcludeFields(first).Concat(ExcludeFields(second)).Distinct(StringComparer.Ordinal).ToArray();
        if (include.Length > 0)
        {
            var effectiveInclude = include.Except(exclude, StringComparer.Ordinal).ToArray();
            return new FieldMask
            {
                Mode = FieldMaskMode.IncludeOnly,
                Include = effectiveInclude,
                AppliesToSystemFields = first.AppliesToSystemFields || second.AppliesToSystemFields
            };
        }

        if (exclude.Length > 0)
        {
            return new FieldMask
            {
                Mode = FieldMaskMode.Exclude,
                Exclude = exclude,
                AppliesToSystemFields = first.AppliesToSystemFields || second.AppliesToSystemFields
            };
        }

        return null;
    }

    private static string[] IncludeFields(FieldMask mask) =>
        mask.Mode == FieldMaskMode.IncludeOnly ? mask.Include ?? [] : [];

    private static string[] ExcludeFields(FieldMask mask) =>
        mask.Mode == FieldMaskMode.Exclude ? mask.Exclude ?? [] : [];

    private bool RequiredHPDAuthServicesMissing()
    {
        if (!_options.RequireHPDAuthServices)
            return false;

        var statuses = _hostStatuses.ToArray();
        return HPDBaseHPDAuthTelemetry.TraceHostCheck(
            statuses.Length,
            () => statuses.Length == 0 || !statuses.Any(static status => status.HPDAuthServicesDetected));
    }

    private async ValueTask<AccessGrant[]> GrantsForAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var providers = _grantProviders.ToArray();
        return await HPDBaseHPDAuthTelemetry.TraceGrantsAsync(
            request,
            providers.Length,
            async () =>
            {
                var grants = new List<AccessGrant>();
                if (request.Grants is { Length: > 0 })
                    grants.AddRange(request.Grants);
                if (_options.StaticGrants is { Length: > 0 })
                    grants.AddRange(_options.StaticGrants);

                foreach (var provider in providers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var provided = await provider.GetGrantsAsync(new HPDAuthBaseGrantRequest
                        {
                            Principal = request.Principal,
                            Operation = request.Operation,
                            Collection = request.Collection,
                            Resource = request.Resource
                        }, cancellationToken).ConfigureAwait(false);
                        HPDBaseHPDAuthTelemetry.RecordGrantProviderCall(request, "ok");
                        grants.AddRange(provided);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        HPDBaseHPDAuthTelemetry.RecordGrantProviderCall(request, "error");
                        HPDBaseHPDAuthLog.GrantProviderFailed(
                            _logger,
                            "dependency",
                            "hpd.auth.base.grantProviderFailed");
                        throw;
                    }
                }

                return grants.ToArray();
            }).ConfigureAwait(false);
    }

    private PolicyDecision? EvaluateGrants(
        PolicyEvaluationRequest request,
        AccessGrant[] grants,
        AccessSubject[] subjects,
        string action)
    {
        if (grants.Length == 0)
            return null;

        var applicable = grants
            .Where(grant => Applies(grant, request, subjects, action))
            .ToArray();
        HPDBaseHPDAuthTelemetry.RecordMatchedGrants(request, applicable.Length, applicable.Any(static grant => grant.Effect == GrantEffect.Deny) ? PolicyEffect.Deny : PolicyEffect.Allow);

        var deny = applicable.FirstOrDefault(grant => grant.Effect == GrantEffect.Deny);
        if (deny is not null)
        {
            return Deny(
                PolicyOutcome.Denied,
                "hpd.auth.base.grantDenied",
                "A matching HPD.Auth BASE grant denied the operation.",
                request,
                subjects,
                [deny.Id]);
        }

        var allows = applicable.Where(grant => grant.Effect == GrantEffect.Allow).ToArray();
        if (allows.Length == 0)
            return null;

        var filter = action == HPDAuthBasePolicyActions.Read
            ? CombineFilters(allows.Select(grant => grant.Condition).Where(static condition => condition is not null)!)
            : null;
        var writeCheck = IsWriteAction(action)
            ? CombineFilters(allows.Select(grant => grant.WriteCondition).Where(static condition => condition is not null)!)
            : null;
        var constraints = filter is null && writeCheck is null
            ? null
            : new PolicyConstraints
            {
                RecordFilter = filter,
                WriteCheck = writeCheck
            };

        return Allow(
            constraints is null ? PolicyOutcome.Allowed : PolicyOutcome.AllowedWithConstraints,
            request,
            subjects,
            matchedGrantIds: allows.Select(static grant => grant.Id).ToArray(),
            constraints: constraints);
    }

    private PolicyDecision? EvaluateRule(
        PolicyEvaluationRequest request,
        HPDAuthBaseCollectionRule rule,
        AccessSubject[] subjects,
        string action)
    {
        var isRead = action == HPDAuthBasePolicyActions.Read;
        var roles = request.Principal.Roles ?? [];
        var allowed = isRead
            ? rule.AllowAnonymousRead && request.Principal.AuthenticationState == PrincipalAuthenticationState.Anonymous
              || rule.AllowAuthenticatedRead && request.Principal.AuthenticationState != PrincipalAuthenticationState.Anonymous
              || HasAnyRole(roles, rule.ReadRoles)
            : HasAnyRole(roles, rule.WriteRoles);

        if (!allowed)
            return null;

        var constraints = ConstraintsFor(rule, request, isRead);
        return Allow(
            constraints is null ? PolicyOutcome.Allowed : PolicyOutcome.AllowedWithConstraints,
            request,
            subjects,
            constraints: constraints);
    }

    private static PolicyConstraints? ConstraintsFor(
        HPDAuthBaseCollectionRule rule,
        PolicyEvaluationRequest request,
        bool isRead)
    {
        FilterExpression? tenantFilter = null;
        if (isRead && rule.RequireTenantMatch && !string.IsNullOrWhiteSpace(rule.TenantFieldPath))
        {
            if (string.IsNullOrWhiteSpace(request.Principal.CurrentTenantId))
                return null;

            tenantFilter = new FilterExpression
            {
                Kind = FilterNodeKind.Compare,
                Field = rule.TenantFieldPath,
                Operator = FilterOperator.Equal,
                Value = new QueryValue
                {
                    Kind = QueryValueKind.String,
                    String = request.Principal.CurrentTenantId
                }
            };
        }

        var readMask = isRead ? FieldMaskFor(rule.ReadIncludeFields, rule.ReadExcludeFields) : null;
        var writeMask = isRead ? null : FieldMaskFor(rule.WriteIncludeFields, rule.WriteExcludeFields);
        if (tenantFilter is null && readMask is null && writeMask is null)
            return null;

        return new PolicyConstraints
        {
            RecordFilter = tenantFilter,
            ReadMask = readMask,
            WriteMask = writeMask
        };
    }

    private static FieldMask? FieldMaskFor(string[]? include, string[]? exclude)
    {
        if (include is { Length: > 0 })
        {
            return new FieldMask
            {
                Mode = FieldMaskMode.IncludeOnly,
                Include = include
            };
        }

        if (exclude is { Length: > 0 })
        {
            return new FieldMask
            {
                Mode = FieldMaskMode.Exclude,
                Exclude = exclude
            };
        }

        return null;
    }

    private static FilterExpression? CombineFilters(IEnumerable<FilterExpression> filters)
    {
        var materialized = filters.ToArray();
        return materialized.Length switch
        {
            0 => null,
            1 => materialized[0],
            _ => new FilterExpression
            {
                Kind = FilterNodeKind.Or,
                Children = materialized
            }
        };
    }

    private static bool Applies(
        AccessGrant grant,
        PolicyEvaluationRequest request,
        AccessSubject[] subjects,
        string action)
    {
        if (!string.Equals(grant.Action, action, StringComparison.Ordinal))
            return false;
        if (grant.ExpiresAt is { } expiresAt && expiresAt <= request.Operation.Now)
            return false;
        if (!SubjectMatches(grant.Subject, subjects))
            return false;

        return grant.Scope.Kind switch
        {
            ResourceScopeKind.Runtime => true,
            ResourceScopeKind.Collection => string.IsNullOrWhiteSpace(grant.Scope.CollectionId)
                || string.Equals(grant.Scope.CollectionId, request.Collection.Id, StringComparison.Ordinal),
            ResourceScopeKind.Record => string.Equals(grant.Scope.CollectionId, request.Collection.Id, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(grant.Scope.RecordId)
                    || string.Equals(grant.Scope.RecordId, request.Resource.RecordId, StringComparison.Ordinal)
                    || string.Equals(grant.Scope.RecordId, request.Operation.RecordId, StringComparison.Ordinal)),
            ResourceScopeKind.Admin => request.Resource.Kind == PolicyResourceKind.AdminMetadata,
            _ => false
        };
    }

    private static bool SubjectMatches(AccessSubject grantSubject, AccessSubject[] subjects) =>
        subjects.Any(subject =>
            subject.Kind == grantSubject.Kind
            && (string.IsNullOrWhiteSpace(grantSubject.Id) || string.Equals(subject.Id, grantSubject.Id, StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(grantSubject.TenantId) || string.Equals(subject.TenantId, grantSubject.TenantId, StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(grantSubject.Qualifier) || string.Equals(subject.Qualifier, grantSubject.Qualifier, StringComparison.Ordinal)));

    private static AccessSubject[] SubjectsFor(PrincipalContext principal)
    {
        var subjects = new List<AccessSubject>();
        if (principal.Subjects is { Length: > 0 })
            subjects.AddRange(principal.Subjects);

        subjects.Add(new AccessSubject
        {
            Kind = principal.AuthenticationState == PrincipalAuthenticationState.Anonymous
                ? AccessSubjectKind.Anonymous
                : AccessSubjectKind.Authenticated,
            Id = principal.SubjectId,
            TenantId = principal.CurrentTenantId,
            Source = principal.AuthSource
        });

        if (!string.IsNullOrWhiteSpace(principal.SubjectId))
        {
            subjects.Add(new AccessSubject
            {
                Kind = AccessSubjectKind.User,
                Id = principal.SubjectId,
                TenantId = principal.CurrentTenantId,
                Source = principal.AuthSource
            });
        }

        return subjects
            .DistinctBy(subject => $"{subject.Kind}|{subject.Id}|{subject.TenantId}|{subject.Qualifier}")
            .ToArray();
    }

    private static bool IsAdmin(PrincipalContext principal, AccessSubject[] subjects) =>
        principal.AuthenticationState == PrincipalAuthenticationState.Admin
        || principal.SubjectKind == AccessSubjectKind.Admin
        || subjects.Any(static subject => subject.Kind == AccessSubjectKind.Admin);

    private static bool IsService(PrincipalContext principal, AccessSubject[] subjects) =>
        principal.AuthenticationState == PrincipalAuthenticationState.Service
        || principal.SubjectKind == AccessSubjectKind.ServicePrincipal
        || subjects.Any(static subject => subject.Kind == AccessSubjectKind.ServicePrincipal);

    private static bool HasAnyRole(string[] roles, string[]? allowedRoles) =>
        allowedRoles is { Length: > 0 } && roles.Any(role => allowedRoles.Contains(role, StringComparer.Ordinal));

    private static bool IsWriteAction(string action) =>
        string.Equals(action, HPDAuthBasePolicyActions.Create, StringComparison.Ordinal)
        || string.Equals(action, HPDAuthBasePolicyActions.Update, StringComparison.Ordinal)
        || string.Equals(action, HPDAuthBasePolicyActions.Delete, StringComparison.Ordinal);

    private static string ActionFor(BaseOperationKind operation) => operation switch
    {
        BaseOperationKind.List or BaseOperationKind.Query or BaseOperationKind.Get => HPDAuthBasePolicyActions.Read,
        BaseOperationKind.Create => HPDAuthBasePolicyActions.Create,
        BaseOperationKind.Patch or BaseOperationKind.Replace => HPDAuthBasePolicyActions.Update,
        BaseOperationKind.Delete => HPDAuthBasePolicyActions.Delete,
        BaseOperationKind.AdminInspect => HPDAuthBasePolicyActions.AdminMetadataRead,
        _ => operation.ToString()
    };

    private static PolicyDecision Allow(
        PolicyOutcome outcome,
        PolicyEvaluationRequest request,
        AccessSubject[] subjects,
        string[]? matchedGrantIds = null,
        PolicyConstraints? constraints = null,
        bool adminBypass = false,
        bool serviceBypass = false) =>
        new()
        {
            Effect = PolicyEffect.Allow,
            Outcome = outcome,
            Constraints = constraints,
            Audit = new PolicyAuditInfo
            {
                EvaluatorId = HPDAuthBaseIds.PolicyEvaluator,
                MatchedSubjects = subjects,
                MatchedGrantIds = matchedGrantIds,
                AdminBypass = adminBypass,
                ServiceBypass = serviceBypass,
                CorrelationId = request.Operation.CorrelationId
            }
        };

    private static PolicyDecision Deny(
        PolicyOutcome outcome,
        string reasonCode,
        string safeMessage,
        PolicyEvaluationRequest request,
        AccessSubject[] subjects,
        string[]? matchedGrantIds = null) =>
        new()
        {
            Effect = PolicyEffect.Deny,
            Outcome = outcome,
            ReasonCode = reasonCode,
            SafeMessage = safeMessage,
            Audit = new PolicyAuditInfo
            {
                EvaluatorId = HPDAuthBaseIds.PolicyEvaluator,
                MatchedSubjects = subjects,
                MatchedGrantIds = matchedGrantIds,
                CorrelationId = request.Operation.CorrelationId
            }
        };
}

/// <summary>
/// Names policy action strings emitted by the HPD.Auth adapter.
/// </summary>
public static class HPDAuthBasePolicyActions
{
    /// <summary>
    /// Read action.
    /// </summary>
    public const string Read = "read";

    /// <summary>
    /// Create action.
    /// </summary>
    public const string Create = "create";

    /// <summary>
    /// Update action.
    /// </summary>
    public const string Update = "update";

    /// <summary>
    /// Delete action.
    /// </summary>
    public const string Delete = "delete";

    /// <summary>
    /// Admin metadata read action.
    /// </summary>
    public const string AdminMetadataRead = "metadata.read.admin";
}

/// <summary>
/// Names stable ids used by the HPD.Auth adapter.
/// </summary>
public static class HPDAuthBaseIds
{
    /// <summary>
    /// The adapter module id.
    /// </summary>
    public const string Module = "hpd.base.auth.hpd-auth";

    /// <summary>
    /// The adapter policy evaluator id.
    /// </summary>
    public const string PolicyEvaluator = "hpd.base.auth.hpd-auth.policy";
}
