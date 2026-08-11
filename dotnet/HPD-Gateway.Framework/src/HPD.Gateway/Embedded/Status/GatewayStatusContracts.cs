using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Primitives;

namespace HPD.Gateway;

public enum GatewayStatusIntentState : byte { NotManaged }
public enum GatewayStatusPreparationState : byte { NotPrepared, Prepared }
public enum GatewayStatusHostState : byte { NotApplicable, NotStarted, Starting, Ready, RestartRequired, Failed, Stopping, Stopped }
public enum GatewayStatusPublicationState : byte { NotAttempted, ActiveAcknowledged, PublicationIndeterminate, Duplicate, Stale, IdentityConflict, Superseded, CanceledBeforePublish, RejectedBeforePublish }
public enum GatewayNativeEligibilityState : byte { NotObserved, EligibleDestinationsPresent, NoEligibleDestinations, PanicFallbackInUse }
public enum GatewayDiscoveryObservationState : byte { NotRequired, Resolving, AppliedFresh, AppliedFreshEmpty, AppliedLastKnownDegraded, AppliedUnavailable, RefreshFailed, Indeterminate, NotObserved }
public enum GatewayReadinessState : byte { Ready, NotReady }
public enum GatewayConditionType : byte { ConfigurationReady, ServingReady, HostReady, HostRestartRequired, PublicationCertain, ProvidersAcceptable, DestinationsEligible }
public enum GatewayConditionValue : byte { True, False, Unknown }

public sealed record GatewayStatusReason(string Code, string? ResourceKind, string? ResourceId, string SafeMessage);

public sealed record GatewayStatusObservationStamp(
    string AuthorityKind,
    string AuthorityId,
    string ProcessInstanceId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong ObservationSequence,
    string? ObservedIdentity,
    DateTimeOffset ObservedAt);

public sealed record GatewayIntentStatus(GatewayStatusIntentState State, GatewayStatusObservationStamp Stamp);
public sealed record GatewayPreparationStatus(GatewayStatusPreparationState State, string? CandidateId, GatewayStatusObservationStamp Stamp);
public sealed record GatewayHostStatus(
    GatewayStatusHostState State,
    string? RunningConfigurationHash,
    string? DesiredConfigurationHash,
    ImmutableArray<GatewayStatusReason> Reasons,
    GatewayStatusObservationStamp Stamp);

public sealed record GatewayActiveConfigurationIdentity(
    string CandidateId,
    string ContentHash,
    string ApplicationId,
    ContentHash SymbolicPlanIdentity,
    string NativeRevisionId,
    DateTimeOffset AcknowledgedAt);

public sealed record GatewayPublicationStatus(
    GatewayStatusPublicationState State,
    string? AttemptedCandidateId,
    GatewayActiveConfigurationIdentity? Active,
    GatewayActiveConfigurationIdentity? LastKnownGood,
    ImmutableArray<GatewayStatusReason> Reasons,
    GatewayStatusObservationStamp Stamp);

public sealed record GatewayDiscoveryStatus(
    GatewayDiscoveryObservationState State,
    string? Profile,
    string? Service,
    string? Endpoint,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long? MembershipGeneration,
    ContentHash? MembershipIdentity,
    int AppliedDestinationCount,
    DateTimeOffset? AppliedAt,
    string SafeDiagnostic);

public sealed record GatewayNativeUpstreamStatus(
    string UpstreamId,
    int AllDestinationCount,
    int AvailableDestinationCount,
    int ActiveHealthyCount,
    int ActiveUnhealthyCount,
    int ActiveUnknownCount,
    int PassiveHealthyCount,
    int PassiveUnhealthyCount,
    int PassiveUnknownCount,
    GatewayNativeEligibilityState Eligibility,
    string AvailabilityPolicy,
    GatewayDiscoveryStatus Discovery,
    bool CountsTruncated,
    ImmutableArray<GatewayStatusReason> Reasons,
    GatewayStatusObservationStamp Stamp);

public sealed record GatewayReadinessStatus(
    GatewayReadinessState Configuration,
    GatewayReadinessState Serving,
    ImmutableArray<GatewayStatusReason> Reasons,
    GatewayStatusObservationStamp Stamp);

public sealed record GatewayCondition(
    GatewayConditionType Type,
    GatewayConditionValue Value,
    string ReasonCode,
    DateTimeOffset LastTransitionAt,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong ObservedSnapshotSequence);

public sealed record GatewayStatusSnapshot(
    string ProcessInstanceId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong SnapshotSequence,
    DateTimeOffset GeneratedAt,
    GatewayIntentStatus Intent,
    GatewayPreparationStatus Preparation,
    GatewayHostStatus Host,
    GatewayPublicationStatus Publication,
    ImmutableArray<GatewayNativeUpstreamStatus> Upstreams,
    GatewayReadinessStatus Readiness,
    ImmutableArray<GatewayCondition> Conditions,
    bool DetailsTruncated);

public sealed record GatewayReadinessResponse(
    string SchemaVersion,
    bool Ready,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong SnapshotSequence,
    DateTimeOffset ObservedAt,
    ImmutableArray<string> Reasons);

public interface IGatewayStatusReader
{
    GatewayStatusSnapshot GetCurrent();
    IChangeToken GetChangeToken();
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GatewayStatusSnapshot))]
[JsonSerializable(typeof(GatewayDiscoveryStatus))]
[JsonSerializable(typeof(GatewayReadinessResponse))]
internal partial class GatewayStatusJsonContext : JsonSerializerContext;
