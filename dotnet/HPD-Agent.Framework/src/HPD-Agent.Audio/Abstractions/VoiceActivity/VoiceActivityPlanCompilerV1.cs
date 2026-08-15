using System.Collections.ObjectModel;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivityPromotionModeV1 : ushort
{
    SingleSource = 1,
    Fused = 2,
    Manual = 3,
}

internal sealed record VoiceActivitySourceCandidateV1
{
    internal VoiceActivitySourceCandidateV1(
        string sourceKey,
        ActivitySourceKindV1 kind,
        VoiceActivitySourceCapabilitiesV1 capabilities,
        bool available,
        ProviderActivityVisibilityV1 providerVisibility)
    {
        SourceKey = ActivitySourceRequestV1.RequireAscii(sourceKey, nameof(sourceKey));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Enum.IsDefined(providerVisibility)) throw new ArgumentOutOfRangeException(nameof(providerVisibility));
        Kind = kind;
        Capabilities = capabilities;
        Available = available;
        ProviderVisibility = providerVisibility;
    }

    internal string SourceKey { get; }
    internal ActivitySourceKindV1 Kind { get; }
    internal VoiceActivitySourceCapabilitiesV1 Capabilities { get; }
    internal bool Available { get; }
    internal ProviderActivityVisibilityV1 ProviderVisibility { get; }
}

internal sealed record VoiceActivityEffectiveSourcePlanV1(
    ActivitySourceRequestV1 Request,
    VoiceActivitySourceCapabilitiesV1 Capabilities,
    TimeSpan EffectiveMaximumWindow,
    ProviderActivityVisibilityV1 ProviderVisibility);

internal sealed record VoiceActivityPromotionAuthorityV1
{
    private readonly string[] _sourceKeys;

    internal VoiceActivityPromotionAuthorityV1(VoiceActivityPromotionModeV1 mode, IReadOnlyList<string> sourceKeys)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentNullException.ThrowIfNull(sourceKeys);
        _sourceKeys = sourceKeys.Select(static key => ActivitySourceRequestV1.RequireAscii(key, nameof(sourceKeys))).ToArray();
        if (_sourceKeys.Length == 0 || _sourceKeys.Distinct(StringComparer.Ordinal).Count() != _sourceKeys.Length)
            throw new ArgumentException("Promotion authority requires a nonempty unique source set.", nameof(sourceKeys));
        if (mode != VoiceActivityPromotionModeV1.Fused && _sourceKeys.Length != 1)
            throw new ArgumentException("Only fused promotion may consume multiple sources.", nameof(sourceKeys));
        Mode = mode;
        SourceKeys = Array.AsReadOnly(_sourceKeys);
    }

    internal VoiceActivityPromotionModeV1 Mode { get; }
    internal IReadOnlyList<string> SourceKeys { get; }
}

internal sealed record VoiceActivityEffectivePlanV1
{
    private readonly VoiceActivityEffectiveSourcePlanV1[] _sources;
    private readonly string[] _differences;

    internal VoiceActivityEffectivePlanV1(ulong planGeneration, ulong configRevision, VoiceActivityRequestV1 request,
        IReadOnlyList<VoiceActivityEffectiveSourcePlanV1> sources, VoiceActivityPromotionAuthorityV1 promotionAuthority,
        IReadOnlyList<string> differences, VoiceActivityHealthStateV1 health)
    {
        if (planGeneration == 0 || configRevision == 0) throw new ArgumentOutOfRangeException();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(promotionAuthority);
        ArgumentNullException.ThrowIfNull(differences);
        _sources = sources.ToArray();
        _differences = differences.ToArray();
        PlanGeneration = planGeneration;
        ConfigRevision = configRevision;
        Request = request;
        Sources = new ReadOnlyCollection<VoiceActivityEffectiveSourcePlanV1>(_sources);
        PromotionAuthority = promotionAuthority;
        Differences = Array.AsReadOnly(_differences);
        Health = health;
    }

    internal ulong PlanGeneration { get; }
    internal ulong ConfigRevision { get; }
    internal VoiceActivityRequestV1 Request { get; }
    internal IReadOnlyList<VoiceActivityEffectiveSourcePlanV1> Sources { get; }
    internal VoiceActivityPromotionAuthorityV1 PromotionAuthority { get; }
    internal IReadOnlyList<string> Differences { get; }
    internal VoiceActivityHealthStateV1 Health { get; }

    internal VoiceActivitySnapshotV1 ToSnapshot() => new(
        PlanGeneration,
        ConfigRevision,
        Request.Profile,
        Sources.Select(static source => new EffectiveActivitySourceV1(
            source.Request.SourceKey,
            source.Request.Kind,
            source.Request.Role,
            usable: true,
            source.ProviderVisibility)).ToArray(),
        Differences,
        Health);
}

internal abstract record VoiceActivityPlanCompilationResultV1
{
    private VoiceActivityPlanCompilationResultV1() { }
    internal sealed record Compiled(VoiceActivityEffectivePlanV1 Plan) : VoiceActivityPlanCompilationResultV1;
    internal sealed record Rejected(string SafeCode) : VoiceActivityPlanCompilationResultV1;
}

internal static class VoiceActivityPlanCompilerV1
{
    internal static VoiceActivityPlanCompilationResultV1 Compile(
        ulong planGeneration,
        ulong configRevision,
        VoiceActivityRequestV1 request,
        IReadOnlyList<VoiceActivitySourceCandidateV1> candidates)
    {
        if (planGeneration == 0 || configRevision == 0) throw new ArgumentOutOfRangeException();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count > VoiceActivityRequestV1.MaximumSources ||
            candidates.Select(static item => item.SourceKey).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            return Reject("candidate-set-invalid");
        if (request.PrefixContext is { } prefix && request.Limits is { } prefixLimits && prefix > prefixLimits.MaximumWindow)
            return Reject("prefix-context-exceeds-limit");

        var byKey = candidates.ToDictionary(static item => item.SourceKey, StringComparer.Ordinal);
        var effective = new List<VoiceActivityEffectiveSourcePlanV1>();
        var differences = new List<string>();
        var degraded = false;
        IReadOnlyList<ActivitySourceRequestV1> requestedSources = request.Sources;
        if (requestedSources.Count == 0)
        {
            var selected = request.Profile switch
            {
                VoiceActivityProfileV1.ProviderManaged => candidates.FirstOrDefault(static item =>
                    item.Available && item.Kind == ActivitySourceKindV1.ProviderNative &&
                    ProviderIsObservable(item.ProviderVisibility)),
                VoiceActivityProfileV1.Automatic => candidates.FirstOrDefault(static item =>
                    item.Available && item.Kind == ActivitySourceKindV1.LocalDetector)
                    ?? candidates.FirstOrDefault(static item => item.Available &&
                        (item.Kind != ActivitySourceKindV1.ProviderNative || ProviderIsObservable(item.ProviderVisibility))),
                _ => null,
            };
            if (selected is null)
                return Reject("no-promotion-source");
            requestedSources = [new ActivitySourceRequestV1(selected.SourceKey, selected.Kind,
                ActivitySourceRoleV1.Authoritative, true)];
            differences.Add($"source-auto-selected:{selected.SourceKey}");
        }

        foreach (var source in requestedSources)
        {
            if (!byKey.TryGetValue(source.SourceKey, out var candidate) || !candidate.Available)
            {
                if (source.Required || request.Degradation == ActivityDegradationPolicyV1.Strict)
                    return Reject("required-source-unavailable");
                differences.Add($"source-unavailable:{source.SourceKey}");
                degraded = true;
                continue;
            }
            if (candidate.Kind != source.Kind)
                return Reject("source-kind-mismatch");
            if (!KindMatches(source.Kind, candidate.Capabilities))
                return Reject("source-capability-mismatch");
            if (source.Kind == ActivitySourceKindV1.ProviderNative && !ProviderIsObservable(candidate.ProviderVisibility))
            {
                if (source.Required || request.Degradation == ActivityDegradationPolicyV1.Strict)
                    return Reject("provider-visibility-insufficient");
                differences.Add($"provider-unobservable:{source.SourceKey}");
                degraded = true;
                continue;
            }
            var maximumWindow = request.Limits is null
                ? candidate.Capabilities.Window.MaximumWindow
                : Min(candidate.Capabilities.Window.MaximumWindow, request.Limits.MaximumWindow);
            if (maximumWindow < candidate.Capabilities.Window.MinimumWindow)
                return Reject("window-limit-unsupported");
            effective.Add(new VoiceActivityEffectiveSourcePlanV1(source, candidate.Capabilities,
                maximumWindow, candidate.ProviderVisibility));
        }

        if (effective.Count == 0)
            return Reject("no-promotion-source");
        var authoritative = effective.Where(static item => item.Request.Role == ActivitySourceRoleV1.Authoritative).ToArray();
        if (authoritative.Length != 1)
            return Reject("promotion-authority-count-invalid");
        if (!ProfileMatches(request.Profile, authoritative[0].Request.Kind, effective.Count))
            return Reject("profile-authority-mismatch");

        var mode = request.Profile switch
        {
            VoiceActivityProfileV1.Fused => VoiceActivityPromotionModeV1.Fused,
            VoiceActivityProfileV1.Manual => VoiceActivityPromotionModeV1.Manual,
            _ => VoiceActivityPromotionModeV1.SingleSource,
        };
        var authorityKeys = mode == VoiceActivityPromotionModeV1.Fused
            ? effective.Where(static item => item.Request.Role is ActivitySourceRoleV1.Authoritative or ActivitySourceRoleV1.Corroborating)
                .Select(static item => item.Request.SourceKey).ToArray()
            : [authoritative[0].Request.SourceKey];
        if (mode == VoiceActivityPromotionModeV1.Fused && authorityKeys.Length < 2)
            return Reject("fusion-source-count-invalid");

        var health = degraded ? VoiceActivityHealthStateV1.Degraded : VoiceActivityHealthStateV1.Ready;
        return new VoiceActivityPlanCompilationResultV1.Compiled(new VoiceActivityEffectivePlanV1(
            planGeneration, configRevision, request, effective,
            new VoiceActivityPromotionAuthorityV1(mode, authorityKeys), differences, health));
    }

    private static bool KindMatches(ActivitySourceKindV1 kind, VoiceActivitySourceCapabilitiesV1 capabilities) => kind switch
    {
        ActivitySourceKindV1.LocalDetector => capabilities.InputOwnership != VoiceActivityInputOwnershipV1.ProviderOpaque,
        ActivitySourceKindV1.ProviderNative => capabilities.InputOwnership == VoiceActivityInputOwnershipV1.ProviderOpaque,
        ActivitySourceKindV1.SttAdjacent => capabilities.Measurement.Kind is
            VoiceActivityMeasurementKindV1.PostProcessedState or VoiceActivityMeasurementKindV1.ProviderOpaqueCategory,
        ActivitySourceKindV1.Manual => capabilities.Measurement.Kind is
            VoiceActivityMeasurementKindV1.BinaryDecision or VoiceActivityMeasurementKindV1.PostProcessedState,
        _ => false,
    };

    private static bool ProfileMatches(VoiceActivityProfileV1 profile, ActivitySourceKindV1 authority, int count) => profile switch
    {
        VoiceActivityProfileV1.Automatic => true,
        VoiceActivityProfileV1.ProviderManaged => authority == ActivitySourceKindV1.ProviderNative,
        VoiceActivityProfileV1.HpdManaged => authority == ActivitySourceKindV1.LocalDetector,
        VoiceActivityProfileV1.Fused => count >= 2,
        VoiceActivityProfileV1.Manual => authority == ActivitySourceKindV1.Manual,
        _ => false,
    };

    private static bool ProviderIsObservable(ProviderActivityVisibilityV1 visibility) => visibility is
        ProviderActivityVisibilityV1.Acknowledged or ProviderActivityVisibilityV1.ObservedConsistent;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
    private static VoiceActivityPlanCompilationResultV1.Rejected Reject(string code) => new(code);
}
