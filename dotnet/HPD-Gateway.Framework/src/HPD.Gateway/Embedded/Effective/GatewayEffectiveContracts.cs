using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HPD.Gateway;

public static class GatewayEffectiveBounds
{
    public const int MaximumRecords = 100_000;
    public const int MaximumContributionsPerRecord = 64;
    public const int MaximumDiagnosticsPerRecord = 16;
    public const int MaximumDiagnostics = 256;
}

public static class GatewayEffectiveFamilies
{
    public const string Authorization = "hpd.gateway/authorization";
    public const string Cors = "hpd.gateway/cors";
    public const string TrafficAdmission = "hpd.gateway/traffic-admission";
    public const string RequestTimeout = "hpd.gateway/request-timeout";
    public const string OutputCache = "hpd.gateway/output-cache";
    public const string Inspection = "hpd.gateway/inspection";
    public const string CredentialDisposition = "hpd.gateway/credential-disposition";
    public const string RequestHeaderTransforms = "hpd.gateway/request-header-transforms";
    public const string ResponseHeaderTransforms = "hpd.gateway/response-header-transforms";
    public const string ResponseTrailerTransforms = "hpd.gateway/response-trailer-transforms";
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayEffectiveTargetKind>))]
public enum GatewayEffectiveTargetKind : byte { Route = 0 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayEffectiveComposition>))]
public enum GatewayEffectiveComposition : byte { ReplaceMoreSpecific = 0, AdditiveOrdered = 1 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayContributionSourceKind>))]
public enum GatewayContributionSourceKind : byte { RootDefault = 0, Inline = 1, ReusableDefinition = 2, HostProfile = 3 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayContributionDisposition>))]
public enum GatewayContributionDisposition : byte { Selected = 0, Overridden = 1, Correlated = 2 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayContributionScope>))]
public enum GatewayContributionScope : byte { RootDefault = 0, RouteLocal = 1, Host = 2 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayMaterializationDisposition>))]
public enum GatewayMaterializationDisposition : byte { Materialized = 0 }

public sealed record GatewayNativeProjection(string Owner, string Seam, string PackageIdentity);

public sealed record GatewayEffectiveContribution(
    GatewayContributionSourceKind SourceKind,
    GatewayContributionScope Scope,
    GatewayContributionDisposition Disposition,
    string SourceIdentity,
    DefinitionId? Definition,
    int DeterministicOrder,
    ContentHash ContentHash);

public sealed record GatewayEffectiveDiagnostic(string Code, string SafeMessage);

public sealed record GatewayEffectiveRecord(
    ushort SchemaVersion,
    GatewayEffectiveTargetKind TargetKind,
    string TargetId,
    string Family,
    GatewayEffectiveComposition Composition,
    ImmutableArray<GatewayEffectiveContribution> Contributions,
    GatewayNativeProjection NativeProjection,
    string CompilerPackage,
    string CompilerVersion,
    GatewayMaterializationDisposition Disposition,
    ContentHash EffectiveContentHash,
    ImmutableArray<GatewayEffectiveDiagnostic> Diagnostics);

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayAppliedUpstreamKind>))]
public enum GatewayAppliedUpstreamKind : byte { Static = 0, ServiceDiscovery = 1 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayAppliedMembershipDisposition>))]
public enum GatewayAppliedMembershipDisposition : byte
{
    Static = 0,
    Fresh = 1,
    LastKnownMembership = 2,
    UnavailableWhenStale = 3,
    RefreshFailed = 4,
}

public sealed record GatewayAppliedTrafficAdmissionEntry(
    int Order,
    string Profile,
    TrafficAdmissionScope Scope,
    TrafficAdmissionKind Kind,
    TrafficAdmissionRateAlgorithm? RateAlgorithm,
    TrafficAdmissionPartitionKind Partition,
    TrafficAdmissionFailureDisposition FailureDisposition,
    string AuthorityId,
    ContentHash BehaviorIdentity,
    int? AcquisitionOrdinal,
    long? PermitLimit,
    long? WindowMilliseconds,
    int? SegmentsPerWindow,
    long? TokenLimit,
    long? TokensPerPeriod,
    long? ReplenishmentPeriodMilliseconds,
    int? ConcurrencyPermitLimit,
    int? QueueLimit,
    string? PartitionProjectorId,
    ContentHash? PartitionProjectorIdentity,
    string? ProviderId,
    ContentHash? ProviderBehaviorIdentity,
    long? OperationTimeoutMilliseconds,
    int? MaximumConcurrentInvocations,
    string? LocalFallbackProfile,
    ContentHash? LocalFallbackIdentity);

public sealed record GatewayAppliedTrafficAdmissionPlan(
    ContentHash PlanIdentity,
    ImmutableArray<GatewayAppliedTrafficAdmissionEntry> Entries);

public sealed record GatewayAppliedRoute(
    string RouteId,
    ImmutableArray<GatewayEffectiveRecord> Contributions,
    GatewayAppliedTrafficAdmissionPlan? TrafficAdmission);

public sealed record GatewayAppliedUpstream(
    string UpstreamId,
    GatewayAppliedUpstreamKind Kind,
    string? DiscoveryProfile,
    string? Service,
    string? Endpoint,
    long? MembershipGeneration,
    ContentHash MembershipIdentity,
    int DestinationCount,
    GatewayAppliedMembershipDisposition Disposition,
    string SafeDiagnostic);

public sealed record GatewayAppliedRuntimeSnapshot(
    ushort SchemaVersion,
    CandidateId CandidateId,
    ContentHash CandidateContentHash,
    string ApplicationId,
    ContentHash SymbolicPlanIdentity,
    DateTimeOffset AppliedAt,
    ImmutableArray<GatewayAppliedRoute> Routes,
    ImmutableArray<GatewayAppliedUpstream> Upstreams,
    bool IsComplete,
    bool IsTruncated);

public sealed record GatewayAppliedRuntimeObservation(
    string NamespaceId,
    string TargetNodeId,
    GatewayAppliedRuntimeSnapshot Snapshot);

public interface IGatewayNodeAppliedRuntimeReader
{
    GatewayAppliedRuntimeObservation? GetCurrent();
    CancellationToken GetChangeToken();
}
