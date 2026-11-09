using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace HPD.Agent.Audio.VoiceActivity;

/// <summary>Identifies the requested high-level voice-activity policy.</summary>
public enum VoiceActivityProfile : ushort
{
    /// <summary>Compiles the best supported effective plan without hiding degradation.</summary>
    Automatic = 1,
    /// <summary>Requests provider-managed detection and reports whether activity is observable.</summary>
    ProviderManaged = 2,
    /// <summary>Requests HPD-managed local activity promotion.</summary>
    HpdManaged = 3,
    /// <summary>Requests an explicitly validated multi-source fusion rule.</summary>
    Fused = 4,
    /// <summary>Requests manual/PTT activity input through the common promotion path.</summary>
    Manual = 5,
}

/// <summary>Describes the requested latency versus stability preference.</summary>
public enum ActivityResponsiveness : ushort { Conservative = 1, Balanced = 2, Responsive = 3 }
/// <summary>Describes the expected acoustic environment.</summary>
public enum VoiceActivityNoiseEnvironment : ushort { Quiet = 1, Variable = 2, Noisy = 3 }
/// <summary>Describes expected speech continuity without selecting an engine.</summary>
public enum VoiceActivitySpeechContinuity : ushort { Intermittent = 1, Natural = 2, Continuous = 3 }
/// <summary>Describes whether and how optional activity capability may degrade.</summary>
public enum ActivityDegradationPolicy : ushort { Strict = 1, AllowOptionalSources = 2, ObservationOnly = 3 }
/// <summary>Identifies one requested source family without naming a concrete provider implementation.</summary>
public enum ActivitySourceKind : ushort { LocalDetector = 1, ProviderNative = 2, SttAdjacent = 3, Manual = 4 }
/// <summary>Identifies one source's role in activity promotion.</summary>
public enum ActivitySourceRole : ushort { Authoritative = 1, Corroborating = 2, Advisory = 3, Fallback = 4, Diagnostic = 5 }
/// <summary>Reports how much provider-side activity truth can be established.</summary>
public enum ProviderActivityVisibility : ushort
{
    Requested = 1, Translated = 2, AcceptedLocally = 3, Acknowledged = 4,
    ObservedConsistent = 5, Rejected = 6, ReconnectRequired = 7, Unknown = 8, NotObservable = 9,
}
/// <summary>Reports the bounded health of one effective activity plan.</summary>
public enum VoiceActivityHealthState : ushort { Ready = 1, Degraded = 2, Unobservable = 3, Faulted = 4, Quarantined = 5 }
/// <summary>Reports the closed outcome of a requested policy update.</summary>
public enum VoiceActivityUpdateDisposition : ushort { Applied = 1, NoChange = 2, Rejected = 3, Stale = 4, OutcomeUnknown = 5 }

/// <summary>Contains one bounded requested activity source and its promotion role.</summary>
public sealed record ActivitySourceRequest
{
    /// <summary>Initializes one source request.</summary>
    public ActivitySourceRequest(string sourceKey, ActivitySourceKind kind, ActivitySourceRole role, bool required)
    {
        SourceKey = RequireAscii(sourceKey, nameof(sourceKey));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        Kind = kind; Role = role; Required = required;
    }
    /// <summary>Gets the stable application/provider-catalog source key.</summary>
    public string SourceKey { get; }
    /// <summary>Gets the requested source family.</summary>
    public ActivitySourceKind Kind { get; }
    /// <summary>Gets the source's promotion role.</summary>
    public ActivitySourceRole Role { get; }
    /// <summary>Gets whether loss of this source must fail readiness.</summary>
    public bool Required { get; }

    internal static string RequireAscii(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (value.Length is 0 or > 128 || value.Any(static character => character > 0x7f))
            throw new ArgumentException("A source key must contain one to 128 ASCII characters.", parameter);
        return value;
    }
}

/// <summary>Contains finite operational bounds for one requested activity plan.</summary>
public sealed record VoiceActivityOperationalLimits
{
    /// <summary>Initializes finite plan, history and timing bounds.</summary>
    public VoiceActivityOperationalLimits(int maximumSources, int maximumObservationHistory,
        int maximumCorrectionHistory, TimeSpan maximumWindow, TimeSpan maximumProcessingLatency)
    {
        if (maximumSources is < 1 or > VoiceActivityRequest.MaximumSources) throw new ArgumentOutOfRangeException(nameof(maximumSources));
        if (maximumObservationHistory is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(maximumObservationHistory));
        if (maximumCorrectionHistory < 0 || maximumCorrectionHistory > maximumObservationHistory) throw new ArgumentOutOfRangeException(nameof(maximumCorrectionHistory));
        if (maximumWindow <= TimeSpan.Zero || maximumWindow > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(maximumWindow));
        if (maximumProcessingLatency <= TimeSpan.Zero || maximumProcessingLatency > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(maximumProcessingLatency));
        MaximumSources = maximumSources; MaximumObservationHistory = maximumObservationHistory;
        MaximumCorrectionHistory = maximumCorrectionHistory; MaximumWindow = maximumWindow;
        MaximumProcessingLatency = maximumProcessingLatency;
    }
    /// <summary>Gets the maximum active source count.</summary>
    public int MaximumSources { get; }
    /// <summary>Gets the maximum retained observation entries.</summary>
    public int MaximumObservationHistory { get; }
    /// <summary>Gets the maximum retained correction entries.</summary>
    public int MaximumCorrectionHistory { get; }
    /// <summary>Gets the maximum source window duration.</summary>
    public TimeSpan MaximumWindow { get; }
    /// <summary>Gets the maximum processing latency admitted by the request.</summary>
    public TimeSpan MaximumProcessingLatency { get; }
}

/// <summary>Contains immutable user intent for one voice-activity plan.</summary>
public sealed record VoiceActivityRequest
{
    /// <summary>The maximum number of requested sources.</summary>
    public const int MaximumSources = 16;
    private readonly ActivitySourceRequest[] _sources;

    /// <summary>Initializes a deeply owned request without acquiring any runtime resource.</summary>
    public VoiceActivityRequest(VoiceActivityProfile profile, ActivityResponsiveness responsiveness,
        VoiceActivityNoiseEnvironment noiseEnvironment, VoiceActivitySpeechContinuity speechContinuity,
        TimeSpan? prefixContext, IReadOnlyList<ActivitySourceRequest> sources,
        ActivityDegradationPolicy degradation, VoiceActivityOperationalLimits? limits)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        if (!Enum.IsDefined(responsiveness)) throw new ArgumentOutOfRangeException(nameof(responsiveness));
        if (!Enum.IsDefined(noiseEnvironment)) throw new ArgumentOutOfRangeException(nameof(noiseEnvironment));
        if (!Enum.IsDefined(speechContinuity)) throw new ArgumentOutOfRangeException(nameof(speechContinuity));
        if (!Enum.IsDefined(degradation)) throw new ArgumentOutOfRangeException(nameof(degradation));
        if (prefixContext.HasValue && (prefixContext.Value <= TimeSpan.Zero || prefixContext.Value > TimeSpan.FromMinutes(1))) throw new ArgumentOutOfRangeException(nameof(prefixContext));
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToArray();
        if (_sources.Length > MaximumSources || _sources.Any(static source => source is null)) throw new ArgumentOutOfRangeException(nameof(sources));
        if (_sources.Select(static source => source.SourceKey).Distinct(StringComparer.Ordinal).Count() != _sources.Length)
            throw new ArgumentException("Source keys must be unique.", nameof(sources));
        if (limits is not null && _sources.Length > limits.MaximumSources) throw new ArgumentException("The source set exceeds its operational bound.", nameof(sources));
        if (profile != VoiceActivityProfile.Automatic && profile != VoiceActivityProfile.ProviderManaged && _sources.Length == 0)
            throw new ArgumentException("The requested profile requires at least one explicit source.", nameof(sources));
        Profile = profile; Responsiveness = responsiveness; NoiseEnvironment = noiseEnvironment;
        SpeechContinuity = speechContinuity; PrefixContext = prefixContext;
        Sources = new ReadOnlyCollection<ActivitySourceRequest>(_sources);
        Degradation = degradation; Limits = limits;
    }
    /// <summary>Gets the requested high-level policy.</summary>
    public VoiceActivityProfile Profile { get; }
    /// <summary>Gets the requested responsiveness.</summary>
    public ActivityResponsiveness Responsiveness { get; }
    /// <summary>Gets the expected noise environment.</summary>
    public VoiceActivityNoiseEnvironment NoiseEnvironment { get; }
    /// <summary>Gets the expected speech continuity.</summary>
    public VoiceActivitySpeechContinuity SpeechContinuity { get; }
    /// <summary>Gets optional bounded prefix context.</summary>
    public TimeSpan? PrefixContext { get; }
    /// <summary>Gets the deeply owned requested sources.</summary>
    public IReadOnlyList<ActivitySourceRequest> Sources { get; }
    /// <summary>Gets the degradation policy.</summary>
    public ActivityDegradationPolicy Degradation { get; }
    /// <summary>Gets optional explicit operational limits.</summary>
    public VoiceActivityOperationalLimits? Limits { get; }
}

/// <summary>Describes one effective source without exposing its runtime object.</summary>
public sealed record EffectiveActivitySource
{
    /// <summary>Initializes an immutable effective-source projection.</summary>
    public EffectiveActivitySource(string sourceKey, ActivitySourceKind kind, ActivitySourceRole role,
        bool usable, ProviderActivityVisibility providerVisibility)
    {
        SourceKey = ActivitySourceRequest.RequireAscii(sourceKey, nameof(sourceKey));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (!Enum.IsDefined(providerVisibility)) throw new ArgumentOutOfRangeException(nameof(providerVisibility));
        Kind = kind; Role = role; Usable = usable; ProviderVisibility = providerVisibility;
    }
    /// <summary>Gets the source key.</summary>
    public string SourceKey { get; }
    /// <summary>Gets the effective source kind.</summary>
    public ActivitySourceKind Kind { get; }
    /// <summary>Gets the effective promotion role.</summary>
    public ActivitySourceRole Role { get; }
    /// <summary>Gets whether the source is currently usable.</summary>
    public bool Usable { get; }
    /// <summary>Gets provider visibility without upgrading unknown truth.</summary>
    public ProviderActivityVisibility ProviderVisibility { get; }
}

/// <summary>Contains the immutable requested-versus-effective plan projection.</summary>
public sealed record VoiceActivitySnapshot
{
    private readonly EffectiveActivitySource[] _sources;
    private readonly string[] _differences;
    /// <summary>Initializes one bounded effective snapshot.</summary>
    public VoiceActivitySnapshot(ulong planGeneration, ulong configRevision, VoiceActivityProfile requestedProfile,
        IReadOnlyList<EffectiveActivitySource> sources, IReadOnlyList<string> requestedEffectiveDifferences,
        VoiceActivityHealthState health)
    {
        if (planGeneration == 0) throw new ArgumentOutOfRangeException(nameof(planGeneration));
        if (configRevision == 0) throw new ArgumentOutOfRangeException(nameof(configRevision));
        if (!Enum.IsDefined(requestedProfile)) throw new ArgumentOutOfRangeException(nameof(requestedProfile));
        if (!Enum.IsDefined(health)) throw new ArgumentOutOfRangeException(nameof(health));
        ArgumentNullException.ThrowIfNull(sources); ArgumentNullException.ThrowIfNull(requestedEffectiveDifferences);
        _sources = sources.ToArray(); _differences = requestedEffectiveDifferences.Select(static value => ActivitySourceRequest.RequireAscii(value, nameof(requestedEffectiveDifferences))).ToArray();
        if (_sources.Length > VoiceActivityRequest.MaximumSources || _differences.Length > 64) throw new ArgumentOutOfRangeException();
        PlanGeneration = planGeneration; ConfigRevision = configRevision; RequestedProfile = requestedProfile;
        Sources = Array.AsReadOnly(_sources); RequestedEffectiveDifferences = Array.AsReadOnly(_differences); Health = health;
    }
    /// <summary>Gets the positive plan generation.</summary>
    public ulong PlanGeneration { get; }
    /// <summary>Gets the positive configuration revision.</summary>
    public ulong ConfigRevision { get; }
    /// <summary>Gets the originally requested profile.</summary>
    public VoiceActivityProfile RequestedProfile { get; }
    /// <summary>Gets effective source projections.</summary>
    public IReadOnlyList<EffectiveActivitySource> Sources { get; }
    /// <summary>Gets bounded requested-versus-effective differences.</summary>
    public IReadOnlyList<string> RequestedEffectiveDifferences { get; }
    /// <summary>Gets current bounded health.</summary>
    public VoiceActivityHealthState Health { get; }
}

/// <summary>Requests an ordered replacement of the current activity policy.</summary>
public sealed record VoiceActivityUpdateRequest
{
    /// <summary>Initializes an update against one exact plan/config revision.</summary>
    public VoiceActivityUpdateRequest(ulong expectedPlanGeneration, ulong expectedConfigRevision, VoiceActivityRequest request)
    {
        if (expectedPlanGeneration == 0) throw new ArgumentOutOfRangeException(nameof(expectedPlanGeneration));
        if (expectedConfigRevision == 0) throw new ArgumentOutOfRangeException(nameof(expectedConfigRevision));
        ArgumentNullException.ThrowIfNull(request);
        ExpectedPlanGeneration = expectedPlanGeneration; ExpectedConfigRevision = expectedConfigRevision; Request = request;
    }
    /// <summary>Gets the expected predecessor plan generation.</summary>
    public ulong ExpectedPlanGeneration { get; }
    /// <summary>Gets the expected predecessor configuration revision.</summary>
    public ulong ExpectedConfigRevision { get; }
    /// <summary>Gets the immutable replacement request.</summary>
    public VoiceActivityRequest Request { get; }
}

/// <summary>Reports one bounded update disposition and resulting snapshot when proven.</summary>
public sealed record VoiceActivityUpdateResult
{
    /// <summary>Initializes an update result.</summary>
    public VoiceActivityUpdateResult(VoiceActivityUpdateDisposition disposition, VoiceActivitySnapshot? snapshot, string? safeCode)
    {
        if (!Enum.IsDefined(disposition)) throw new ArgumentOutOfRangeException(nameof(disposition));
        if (disposition is VoiceActivityUpdateDisposition.Applied or VoiceActivityUpdateDisposition.NoChange && snapshot is null)
            throw new ArgumentException("A proven update disposition requires a snapshot.", nameof(snapshot));
        if (safeCode is not null) safeCode = ActivitySourceRequest.RequireAscii(safeCode, nameof(safeCode));
        Disposition = disposition; Snapshot = snapshot; SafeCode = safeCode;
    }
    /// <summary>Gets the closed update disposition.</summary>
    public VoiceActivityUpdateDisposition Disposition { get; }
    /// <summary>Gets the resulting snapshot when proven.</summary>
    public VoiceActivitySnapshot? Snapshot { get; }
    /// <summary>Gets an optional bounded nonsecret diagnostic code.</summary>
    public string? SafeCode { get; }
}

/// <summary>Provides NativeAOT-safe JSON metadata for immutable voice-activity contracts.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false)]
[JsonSerializable(typeof(VoiceActivityRequest))]
[JsonSerializable(typeof(ActivitySourceRequest))]
[JsonSerializable(typeof(VoiceActivityOperationalLimits))]
[JsonSerializable(typeof(VoiceActivitySnapshot))]
[JsonSerializable(typeof(EffectiveActivitySource))]
[JsonSerializable(typeof(VoiceActivityUpdateRequest))]
[JsonSerializable(typeof(VoiceActivityUpdateResult))]
public sealed partial class VoiceActivityJsonContext : JsonSerializerContext;
