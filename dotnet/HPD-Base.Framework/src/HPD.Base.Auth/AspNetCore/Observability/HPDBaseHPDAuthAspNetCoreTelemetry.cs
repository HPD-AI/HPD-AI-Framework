using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base;

namespace HPD.Base.Auth;

internal static class HPDBaseHPDAuthAspNetCoreTelemetry
{
    private static readonly Counter<long> PrincipalMaps = HPDBaseHPDAuthAspNetCoreObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.AuthPrincipalMaps,
        unit: "{operation}",
        description: "Counts HPD.BASE HPD.Auth ASP.NET principal mappings.");

    private static readonly Histogram<double> PrincipalMapDuration = HPDBaseHPDAuthAspNetCoreObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.AuthPrincipalMapDuration,
        unit: "s",
        description: "Records HPD.BASE HPD.Auth ASP.NET principal mapping duration.");

    /// <summary>Executes the trace map async operation.</summary>
    public static async ValueTask<PrincipalContext> TraceMapAsync(
        int enricherCount,
        Func<ValueTask<PrincipalContext>> invoke)
    {
        using var activity = Start(HPDBaseTelemetrySpans.AuthPrincipalMap, "principalMap");
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var principal = await invoke().ConfigureAwait(false);
            Finish(activity, principal, enricherCount, "ok", startedAt);
            return principal;
        }
        catch
        {
            Finish(activity, null, enricherCount, "error", startedAt);
            throw;
        }
    }

    /// <summary>Executes the trace enrich async operation.</summary>
    public static async ValueTask<PrincipalContext> TraceEnrichAsync(
        PrincipalContext principal,
        int enricherCount,
        Func<ValueTask<PrincipalContext>> invoke)
    {
        using var activity = Start(HPDBaseTelemetrySpans.AuthPrincipalEnrich, "principalEnrich");
        activity?.SetTag(HPDBaseTelemetryTags.AuthState, AuthStateValue(principal.AuthenticationState));
        activity?.SetTag(HPDBaseTelemetryTags.AuthSubjectKind, SubjectKindValue(principal.SubjectKind));
        activity?.SetTag(HPDBaseTelemetryTags.CountBucket, HPDBaseTelemetryBuckets.Count(enricherCount));
        try
        {
            var enriched = await invoke().ConfigureAwait(false);
            activity?.SetTag(HPDBaseTelemetryTags.ResultStatus, "ok");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return enriched;
        }
        catch
        {
            activity?.SetTag(HPDBaseTelemetryTags.ResultStatus, "error");
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    private static Activity? Start(string spanName, string operation)
    {
        var activity = HPDBaseHPDAuthAspNetCoreObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleHPDAuth);
        activity.SetTag(HPDBaseTelemetryTags.OperationKind, operation);
        return activity;
    }

    private static void Finish(Activity? activity, PrincipalContext? principal, int enricherCount, string status, long startedAt)
    {
        activity?.SetTag(HPDBaseTelemetryTags.ResultStatus, status);
        activity?.SetTag(HPDBaseTelemetryTags.CountBucket, HPDBaseTelemetryBuckets.Count(enricherCount));
        if (principal is not null)
        {
            activity?.SetTag(HPDBaseTelemetryTags.AuthState, AuthStateValue(principal.AuthenticationState));
            activity?.SetTag(HPDBaseTelemetryTags.AuthSubjectKind, SubjectKindValue(principal.SubjectKind));
        }

        activity?.SetStatus(status == "error" ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        var tags = Tags(principal, enricherCount, status);
        PrincipalMaps.Add(1, tags);
        PrincipalMapDuration.Record((double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency, tags);
    }

    private static TagList Tags(PrincipalContext? principal, int enricherCount, string status) => new()
    {
        { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleHPDAuth },
        { HPDBaseTelemetryTags.OperationKind, "principalMap" },
        { HPDBaseTelemetryTags.AuthState, principal is null ? "unknown" : AuthStateValue(principal.AuthenticationState) },
        { HPDBaseTelemetryTags.AuthSubjectKind, principal is null ? "unknown" : SubjectKindValue(principal.SubjectKind) },
        { HPDBaseTelemetryTags.CountBucket, HPDBaseTelemetryBuckets.Count(enricherCount) },
        { HPDBaseTelemetryTags.ResultStatus, status }
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
}
