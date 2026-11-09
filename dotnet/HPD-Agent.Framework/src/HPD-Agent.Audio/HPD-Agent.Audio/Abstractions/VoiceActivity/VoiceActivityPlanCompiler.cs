using System.Collections.ObjectModel;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivityPromotionMode : ushort
{
    SingleSource = 1,
    Fused = 2,
    Manual = 3,
}

internal sealed record VoiceActivitySourceCandidate
{
    internal VoiceActivitySourceCandidate(
        string sourceKey,
        ActivitySourceKind kind,
        VoiceActivitySourceCapabilities capabilities,
        bool available,
        ProviderActivityVisibility providerVisibility)
    {
        SourceKey = ActivitySourceRequest.RequireAscii(sourceKey, nameof(sourceKey));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Enum.IsDefined(providerVisibility)) throw new ArgumentOutOfRangeException(nameof(providerVisibility));
        Kind = kind;
        Capabilities = capabilities;
        Available = available;
        ProviderVisibility = providerVisibility;
    }

    internal string SourceKey { get; }
    internal ActivitySourceKind Kind { get; }
    internal VoiceActivitySourceCapabilities Capabilities { get; }
    internal bool Available { get; }
    internal ProviderActivityVisibility ProviderVisibility { get; }
}

internal sealed record VoiceActivityEffectiveSourcePlan(
    ActivitySourceRequest Request,
    VoiceActivitySourceCapabilities Capabilities,
    TimeSpan EffectiveMaximumWindow,
    ProviderActivityVisibility ProviderVisibility);

internal sealed record VoiceActivityPromotionAuthority
{
    private readonly string[] _sourceKeys;

    internal VoiceActivityPromotionAuthority(VoiceActivityPromotionMode mode, IReadOnlyList<string> sourceKeys)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentNullException.ThrowIfNull(sourceKeys);
        _sourceKeys = sourceKeys.Select(static key => ActivitySourceRequest.RequireAscii(key, nameof(sourceKeys))).ToArray();
        if (_sourceKeys.Length == 0 || _sourceKeys.Distinct(StringComparer.Ordinal).Count() != _sourceKeys.Length)
            throw new ArgumentException("Promotion authority requires a nonempty unique source set.", nameof(sourceKeys));
        if (mode != VoiceActivityPromotionMode.Fused && _sourceKeys.Length != 1)
            throw new ArgumentException("Only fused promotion may consume multiple sources.", nameof(sourceKeys));
        Mode = mode;
        SourceKeys = Array.AsReadOnly(_sourceKeys);
    }

    internal VoiceActivityPromotionMode Mode { get; }
    internal IReadOnlyList<string> SourceKeys { get; }
}

internal sealed record VoiceActivityEffectivePlan
{
    private readonly VoiceActivityEffectiveSourcePlan[] _sources;
    private readonly string[] _differences;

    internal VoiceActivityEffectivePlan(ulong planGeneration, ulong configRevision, VoiceActivityRequest request,
        IReadOnlyList<VoiceActivityEffectiveSourcePlan> sources, VoiceActivityPromotionAuthority promotionAuthority,
        IReadOnlyList<string> differences, VoiceActivityHealthState health)
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
        Sources = new ReadOnlyCollection<VoiceActivityEffectiveSourcePlan>(_sources);
        PromotionAuthority = promotionAuthority;
        Differences = Array.AsReadOnly(_differences);
        Health = health;
    }

    internal ulong PlanGeneration { get; }
    internal ulong ConfigRevision { get; }
    internal VoiceActivityRequest Request { get; }
    internal IReadOnlyList<VoiceActivityEffectiveSourcePlan> Sources { get; }
    internal VoiceActivityPromotionAuthority PromotionAuthority { get; }
    internal IReadOnlyList<string> Differences { get; }
    internal VoiceActivityHealthState Health { get; }

    internal VoiceActivitySnapshot ToSnapshot() => new(
        PlanGeneration,
        ConfigRevision,
        Request.Profile,
        Sources.Select(static source => new EffectiveActivitySource(
            source.Request.SourceKey,
            source.Request.Kind,
            source.Request.Role,
            usable: true,
            source.ProviderVisibility)).ToArray(),
        Differences,
        Health);
}

internal abstract record VoiceActivityPlanCompilationResult
{
    private VoiceActivityPlanCompilationResult() { }
    internal sealed record Compiled(VoiceActivityEffectivePlan Plan) : VoiceActivityPlanCompilationResult;
    internal sealed record Rejected(string SafeCode) : VoiceActivityPlanCompilationResult;
}

internal static class VoiceActivityPlanCompiler
{
    internal static VoiceActivityPlanCompilationResult Compile(
        ulong planGeneration,
        ulong configRevision,
        VoiceActivityRequest request,
        IReadOnlyList<VoiceActivitySourceCandidate> candidates)
    {
        if (planGeneration == 0 || configRevision == 0) throw new ArgumentOutOfRangeException();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count > VoiceActivityRequest.MaximumSources ||
            candidates.Select(static item => item.SourceKey).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            return Reject("candidate-set-invalid");
        if (request.PrefixContext is { } prefix && request.Limits is { } prefixLimits && prefix > prefixLimits.MaximumWindow)
            return Reject("prefix-context-exceeds-limit");

        var byKey = candidates.ToDictionary(static item => item.SourceKey, StringComparer.Ordinal);
        var effective = new List<VoiceActivityEffectiveSourcePlan>();
        var differences = new List<string>();
        var degraded = false;
        IReadOnlyList<ActivitySourceRequest> requestedSources = request.Sources;
        if (requestedSources.Count == 0)
        {
            var selected = request.Profile switch
            {
                VoiceActivityProfile.ProviderManaged => candidates.FirstOrDefault(static item =>
                    item.Available && item.Kind == ActivitySourceKind.ProviderNative &&
                    ProviderIsObservable(item.ProviderVisibility)),
                VoiceActivityProfile.Automatic => candidates.FirstOrDefault(static item =>
                    item.Available && item.Kind == ActivitySourceKind.LocalDetector)
                    ?? candidates.FirstOrDefault(static item => item.Available &&
                        (item.Kind != ActivitySourceKind.ProviderNative || ProviderIsObservable(item.ProviderVisibility))),
                _ => null,
            };
            if (selected is null)
                return Reject("no-promotion-source");
            requestedSources = [new ActivitySourceRequest(selected.SourceKey, selected.Kind,
                ActivitySourceRole.Authoritative, true)];
            differences.Add($"source-auto-selected:{selected.SourceKey}");
        }

        foreach (var source in requestedSources)
        {
            if (!byKey.TryGetValue(source.SourceKey, out var candidate) || !candidate.Available)
            {
                if (source.Required || request.Degradation == ActivityDegradationPolicy.Strict)
                    return Reject("required-source-unavailable");
                differences.Add($"source-unavailable:{source.SourceKey}");
                degraded = true;
                continue;
            }
            if (candidate.Kind != source.Kind)
                return Reject("source-kind-mismatch");
            if (!KindMatches(source.Kind, candidate.Capabilities))
                return Reject("source-capability-mismatch");
            if (source.Kind == ActivitySourceKind.ProviderNative && !ProviderIsObservable(candidate.ProviderVisibility))
            {
                if (source.Required || request.Degradation == ActivityDegradationPolicy.Strict)
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
            effective.Add(new VoiceActivityEffectiveSourcePlan(source, candidate.Capabilities,
                maximumWindow, candidate.ProviderVisibility));
        }

        if (effective.Count == 0)
            return Reject("no-promotion-source");
        var authoritative = effective.Where(static item => item.Request.Role == ActivitySourceRole.Authoritative).ToArray();
        if (authoritative.Length != 1)
            return Reject("promotion-authority-count-invalid");
        if (!ProfileMatches(request.Profile, authoritative[0].Request.Kind, effective.Count))
            return Reject("profile-authority-mismatch");

        var mode = request.Profile switch
        {
            VoiceActivityProfile.Fused => VoiceActivityPromotionMode.Fused,
            VoiceActivityProfile.Manual => VoiceActivityPromotionMode.Manual,
            _ => VoiceActivityPromotionMode.SingleSource,
        };
        var authorityKeys = mode == VoiceActivityPromotionMode.Fused
            ? effective.Where(static item => item.Request.Role is ActivitySourceRole.Authoritative or ActivitySourceRole.Corroborating)
                .Select(static item => item.Request.SourceKey).ToArray()
            : [authoritative[0].Request.SourceKey];
        if (mode == VoiceActivityPromotionMode.Fused && authorityKeys.Length < 2)
            return Reject("fusion-source-count-invalid");
        if (mode == VoiceActivityPromotionMode.Fused && !FusionCompatible(effective
                .Where(item => authorityKeys.Contains(item.Request.SourceKey, StringComparer.Ordinal))
                .Select(static item => item.Capabilities.Measurement).ToArray()))
            return Reject("fusion-measurement-incompatible");

        var health = degraded ? VoiceActivityHealthState.Degraded : VoiceActivityHealthState.Ready;
        return new VoiceActivityPlanCompilationResult.Compiled(new VoiceActivityEffectivePlan(
            planGeneration, configRevision, request, effective,
            new VoiceActivityPromotionAuthority(mode, authorityKeys), differences, health));
    }

    private static bool KindMatches(ActivitySourceKind kind, VoiceActivitySourceCapabilities capabilities) => kind switch
    {
        ActivitySourceKind.LocalDetector => capabilities.InputOwnership != VoiceActivityInputOwnership.ProviderOpaque,
        ActivitySourceKind.ProviderNative => capabilities.InputOwnership == VoiceActivityInputOwnership.ProviderOpaque,
        ActivitySourceKind.SttAdjacent => capabilities.Measurement.Kind is
            VoiceActivityMeasurementKind.PostProcessedState or VoiceActivityMeasurementKind.ProviderOpaqueCategory,
        ActivitySourceKind.Manual => capabilities.Measurement.Kind is
            VoiceActivityMeasurementKind.BinaryDecision or VoiceActivityMeasurementKind.PostProcessedState,
        _ => false,
    };

    private static bool ProfileMatches(VoiceActivityProfile profile, ActivitySourceKind authority, int count) => profile switch
    {
        VoiceActivityProfile.Automatic => true,
        VoiceActivityProfile.ProviderManaged => authority == ActivitySourceKind.ProviderNative,
        VoiceActivityProfile.HpdManaged => authority == ActivitySourceKind.LocalDetector,
        VoiceActivityProfile.Fused => count >= 2,
        VoiceActivityProfile.Manual => authority == ActivitySourceKind.Manual,
        _ => false,
    };

    private static bool ProviderIsObservable(ProviderActivityVisibility visibility) => visibility is
        ProviderActivityVisibility.Acknowledged or ProviderActivityVisibility.ObservedConsistent;

    private static bool FusionCompatible(IReadOnlyList<VoiceActivityMeasurementDescriptor> descriptors)
    {
        if (descriptors.Count < 2) return false;
        var first = descriptors[0];
        if (descriptors.All(static descriptor => descriptor.Kind is
                VoiceActivityMeasurementKind.BinaryDecision or
                VoiceActivityMeasurementKind.PostProcessedState or
                VoiceActivityMeasurementKind.ProviderOpaqueCategory))
            return true;
        return first.Kind == VoiceActivityMeasurementKind.CalibratedLikelihood &&
            descriptors.All(descriptor => descriptor.Kind == first.Kind && descriptor.Units == first.Units &&
                descriptor.Minimum == first.Minimum && descriptor.Maximum == first.Maximum &&
                descriptor.CalibrationIdentity == first.CalibrationIdentity);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
    private static VoiceActivityPlanCompilationResult.Rejected Reject(string code) => new(code);
}
