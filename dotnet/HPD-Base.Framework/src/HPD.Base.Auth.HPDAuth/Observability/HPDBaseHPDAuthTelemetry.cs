using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base.Auth.HPDAuth.Configuration;
using HPD.Base.Auth.HPDAuth.Policy;
using HPD.Base.Policy;
using HPD.Base.Observability;
using HPD.Base.Runtime;

namespace HPD.Base.Auth.HPDAuth.Observability;

internal static class HPDBaseHPDAuthTelemetry
{
    private static readonly Counter<long> PolicyEvaluations = HPDBaseHPDAuthObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.AuthPolicyEvaluations,
        unit: "{operation}",
        description: "Counts HPD.BASE HPD.Auth policy evaluations.");

    private static readonly Histogram<double> PolicyDuration = HPDBaseHPDAuthObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.AuthPolicyDuration,
        unit: "s",
        description: "Records HPD.BASE HPD.Auth policy evaluation duration.");

    private static readonly Counter<long> PolicyDenials = HPDBaseHPDAuthObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.AuthPolicyDenials,
        unit: "{operation}",
        description: "Counts HPD.BASE HPD.Auth policy denials.");

    private static readonly Counter<long> GrantProviderCalls = HPDBaseHPDAuthObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.AuthGrantProviderCalls,
        unit: "{operation}",
        description: "Counts HPD.BASE HPD.Auth grant provider calls.");

    private static readonly Histogram<long> GrantsMatched = HPDBaseHPDAuthObservability.Meter.CreateHistogram<long>(
        HPDBaseTelemetryInstruments.AuthGrantsMatched,
        unit: "{grant}",
        description: "Records bucketed HPD.BASE HPD.Auth grant match counts.");

    private static readonly Counter<long> Bypasses = HPDBaseHPDAuthObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.AuthBypasses,
        unit: "{operation}",
        description: "Counts HPD.BASE HPD.Auth admin and service bypass decisions.");

    public static async ValueTask<PolicyDecision> TracePolicyAsync(
        PolicyEvaluationRequest request,
        HPDAuthBasePolicyCompositionMode compositionMode,
        Func<ValueTask<PolicyDecision>> invoke)
    {
        using var activity = StartPolicy(request, compositionMode);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var decision = await invoke().ConfigureAwait(false);
            FinishPolicy(activity, request, compositionMode, decision, startedAt);
            return decision;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    public static async ValueTask<AccessGrant[]> TraceGrantsAsync(
        PolicyEvaluationRequest request,
        int providerCount,
        Func<ValueTask<AccessGrant[]>> invoke)
    {
        using var activity = StartGrants(request, providerCount);
        try
        {
            var grants = await invoke().ConfigureAwait(false);
            FinishGrants(activity, request, providerCount, grants.Length, "ok");
            return grants;
        }
        catch
        {
            FinishGrants(activity, request, providerCount, null, "error");
            throw;
        }
    }

    public static void RecordGrantProviderCall(PolicyEvaluationRequest request, string status) =>
        GrantProviderCalls.Add(1, Tags(request, null, null, null, status));

    public static void RecordMatchedGrants(PolicyEvaluationRequest request, int count, PolicyEffect effect)
    {
        var tags = Tags(request, EffectValue(effect), null, null, "ok");
        tags.Add(HPDBaseTelemetryTags.CountBucket, HPDBaseTelemetryBuckets.Count(count));
        GrantsMatched.Record(count, tags);
    }

    public static void RecordBypass(PolicyEvaluationRequest request, string bypassKind)
    {
        var tags = Tags(request, HPDBaseTelemetryValues.PolicyAllow, OutcomeValue(PolicyOutcome.Bypassed), null, "ok");
        tags.Add(HPDBaseTelemetryTags.CountBucket, bypassKind);
        Bypasses.Add(1, tags);
    }

    public static bool TraceHostCheck(int statusProviderCount, Func<bool> invoke)
    {
        using var activity = HPDBaseHPDAuthObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.AuthHostCheck, ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleHPDAuth);
            activity.SetTag(HPDBaseTelemetryTags.OperationKind, "hostCheck");
            activity.SetTag(HPDBaseTelemetryTags.CountBucket, HPDBaseTelemetryBuckets.Count(statusProviderCount));
        }

        var missing = invoke();
        activity?.SetTag(HPDBaseTelemetryTags.ResultStatus, missing ? "missing" : "ok");
        activity?.SetStatus(ActivityStatusCode.Ok);
        return missing;
    }

    private static Activity? StartPolicy(PolicyEvaluationRequest request, HPDAuthBasePolicyCompositionMode compositionMode)
    {
        var activity = HPDBaseHPDAuthObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.AuthPolicyEvaluate, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        SetRequestTags(activity, request);
        activity.SetTag(HPDBaseTelemetryTags.AuthCompositionMode, CompositionModeValue(compositionMode));
        return activity;
    }

    private static Activity? StartGrants(PolicyEvaluationRequest request, int providerCount)
    {
        var activity = HPDBaseHPDAuthObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.AuthGrantsResolve, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        SetRequestTags(activity, request);
        activity.SetTag(HPDBaseTelemetryTags.CountBucket, HPDBaseTelemetryBuckets.Count(providerCount));
        return activity;
    }

    private static void FinishPolicy(
        Activity? activity,
        PolicyEvaluationRequest request,
        HPDAuthBasePolicyCompositionMode compositionMode,
        PolicyDecision decision,
        long startedAt)
    {
        var effect = EffectValue(decision.Effect);
        var outcome = OutcomeValue(decision.Outcome);
        if (activity is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.PolicyEffect, effect);
            activity.SetTag(HPDBaseTelemetryTags.ResultStatus, outcome);
            activity.SetTag(HPDBaseTelemetryTags.AuthCompositionMode, CompositionModeValue(compositionMode));
            if (!string.IsNullOrWhiteSpace(decision.ReasonCode))
            {
                activity.SetTag(HPDBaseTelemetryTags.PolicyReasonCode, decision.ReasonCode);
            }

            activity.SetStatus(ActivityStatusCode.Ok);
        }

        var tags = Tags(request, effect, outcome, decision.ReasonCode, "ok");
        tags.Add(HPDBaseTelemetryTags.AuthCompositionMode, CompositionModeValue(compositionMode));
        PolicyEvaluations.Add(1, tags);
        PolicyDuration.Record((double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency, tags);
        if (decision.Effect == PolicyEffect.Deny)
        {
            PolicyDenials.Add(1, tags);
        }
    }

    private static void FinishGrants(Activity? activity, PolicyEvaluationRequest request, int providerCount, int? grantsCount, string status)
    {
        if (activity is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.ResultStatus, status);
            activity.SetTag(HPDBaseTelemetryTags.CountBucket, HPDBaseTelemetryBuckets.Count(providerCount));
            activity.SetStatus(status == "error" ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        }

        if (status == "error")
        {
            GrantProviderCalls.Add(1, Tags(request, null, null, null, "error"));
        }
    }

    private static void SetRequestTags(Activity activity, PolicyEvaluationRequest request)
    {
        activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleHPDAuth);
        activity.SetTag(HPDBaseTelemetryTags.OperationKind, OperationValue(request.Operation.Operation));
        activity.SetTag(HPDBaseTelemetryTags.CollectionId, request.Collection.Id);
        activity.SetTag(HPDBaseTelemetryTags.PolicyResourceKind, ResourceKindValue(request.Resource.Kind));
        activity.SetTag(HPDBaseTelemetryTags.AuthState, AuthStateValue(request.Principal.AuthenticationState));
        activity.SetTag(HPDBaseTelemetryTags.AuthSubjectKind, SubjectKindValue(request.Principal.SubjectKind));
        activity.SetTag(HPDBaseTelemetryTags.CorrelationIdPresent, !string.IsNullOrWhiteSpace(request.Operation.CorrelationId));
    }

    private static TagList Tags(PolicyEvaluationRequest request, string? effect, string? outcome, string? reasonCode, string status)
    {
        var tags = new TagList
        {
            { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleHPDAuth },
            { HPDBaseTelemetryTags.OperationKind, OperationValue(request.Operation.Operation) },
            { HPDBaseTelemetryTags.CollectionId, request.Collection.Id },
            { HPDBaseTelemetryTags.AuthState, AuthStateValue(request.Principal.AuthenticationState) },
            { HPDBaseTelemetryTags.AuthSubjectKind, SubjectKindValue(request.Principal.SubjectKind) },
            { HPDBaseTelemetryTags.ResultStatus, outcome ?? status }
        };
        if (effect is not null)
        {
            tags.Add(HPDBaseTelemetryTags.PolicyEffect, effect);
        }

        if (!string.IsNullOrWhiteSpace(reasonCode))
        {
            tags.Add(HPDBaseTelemetryTags.PolicyReasonCode, reasonCode);
        }

        return tags;
    }

    private static string OperationValue(BaseOperationKind value) => value switch
    {
        BaseOperationKind.List => "list",
        BaseOperationKind.Query => "query",
        BaseOperationKind.Get => "get",
        BaseOperationKind.Create => "create",
        BaseOperationKind.Patch => "patch",
        BaseOperationKind.Replace => "replace",
        BaseOperationKind.Delete => "delete",
        BaseOperationKind.AdminInspect => "adminInspect",
        _ => "unknown"
    };

    private static string AuthStateValue(PrincipalAuthenticationState value) => value switch
    {
        PrincipalAuthenticationState.Anonymous => "anonymous",
        PrincipalAuthenticationState.Authenticated => "authenticated",
        PrincipalAuthenticationState.Admin => "admin",
        PrincipalAuthenticationState.Service => "service",
        PrincipalAuthenticationState.System => "system",
        _ => "unknown"
    };

    private static string SubjectKindValue(AccessSubjectKind value) => value switch
    {
        AccessSubjectKind.Anonymous => "anonymous",
        AccessSubjectKind.Authenticated => "authenticated",
        AccessSubjectKind.User => "user",
        AccessSubjectKind.Tenant => "tenant",
        AccessSubjectKind.Role => "role",
        AccessSubjectKind.ServicePrincipal => "servicePrincipal",
        AccessSubjectKind.Admin => "admin",
        _ => "unknown"
    };

    private static string CompositionModeValue(HPDAuthBasePolicyCompositionMode value) => value switch
    {
        HPDAuthBasePolicyCompositionMode.HPDAuthOnly => "hpdAuthOnly",
        HPDAuthBasePolicyCompositionMode.HPDAuthThenInner => "hpdAuthThenInner",
        HPDAuthBasePolicyCompositionMode.InnerThenHPDAuth => "innerThenHpdAuth",
        _ => "unknown"
    };

    private static string ResourceKindValue(PolicyResourceKind value) => value switch
    {
        PolicyResourceKind.Query => "query",
        PolicyResourceKind.Record => "record",
        PolicyResourceKind.CreatePayload => "createPayload",
        PolicyResourceKind.UpdatePayload => "updatePayload",
        PolicyResourceKind.AdminMetadata => "adminMetadata",
        _ => "unknown"
    };

    private static string EffectValue(PolicyEffect value) => value switch
    {
        PolicyEffect.Allow => HPDBaseTelemetryValues.PolicyAllow,
        PolicyEffect.Deny => HPDBaseTelemetryValues.PolicyDeny,
        PolicyEffect.Abstain => HPDBaseTelemetryValues.PolicyAbstain,
        _ => "unknown"
    };

    private static string OutcomeValue(PolicyOutcome value) => value switch
    {
        PolicyOutcome.Allowed => "allowed",
        PolicyOutcome.AllowedWithConstraints => "allowedWithConstraints",
        PolicyOutcome.Denied => "denied",
        PolicyOutcome.Unauthenticated => "unauthenticated",
        PolicyOutcome.Bypassed => "bypassed",
        _ => "unknown"
    };
}
