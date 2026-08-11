using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace HPD.Gateway;

[JsonConverter(typeof(StrictStringEnumJsonConverter<TrafficAdmissionScope>))]
public enum TrafficAdmissionScope : byte
{
    ProcessLocal = 0,
    Deployment = 1
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<TrafficAdmissionKind>))]
public enum TrafficAdmissionKind : byte
{
    RequestRate = 0,
    Concurrency = 1
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<TrafficAdmissionRateAlgorithm>))]
public enum TrafficAdmissionRateAlgorithm : byte
{
    FixedWindow = 0,
    SlidingWindow = 1,
    TokenBucket = 2
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<TrafficAdmissionPartitionKind>))]
public enum TrafficAdmissionPartitionKind : byte
{
    Global = 0,
    Route = 1,
    SourceIp = 2,
    AuthenticatedSubject = 3,
    Tenant = 4,
    Consumer = 5,
    Custom = 6
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<TrafficAdmissionFailureDisposition>))]
public enum TrafficAdmissionFailureDisposition : byte
{
    Reject = 0,
    Bypass = 1,
    LocalFallback = 2
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayAdmissionPartitionFailure>))]
public enum GatewayAdmissionPartitionFailure : byte
{
    Unavailable = 0,
    Invalid = 1,
    Canceled = 2
}

public sealed record GatewayAdmissionPartitionContext(
    ClaimsPrincipal Principal,
    RouteId Route);

public sealed record GatewayAdmissionPartitionResult(
    string? Value,
    GatewayAdmissionPartitionFailure? Failure)
{
    public static GatewayAdmissionPartitionResult Success(string value) => new(value, null);
    public static GatewayAdmissionPartitionResult Failed(GatewayAdmissionPartitionFailure failure) => new(null, failure);
}

public interface IGatewayAdmissionPartitionProjector
{
    ValueTask<GatewayAdmissionPartitionResult> ProjectAsync(
        GatewayAdmissionPartitionContext context,
        CancellationToken cancellationToken);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FixedWindowAdmissionEntry), "fixedWindow")]
[JsonDerivedType(typeof(SlidingWindowAdmissionEntry), "slidingWindow")]
[JsonDerivedType(typeof(TokenBucketAdmissionEntry), "tokenBucket")]
[JsonDerivedType(typeof(ConcurrencyAdmissionEntry), "concurrency")]
public abstract record TrafficAdmissionEntry
{
    internal abstract string ProfileName { get; }
}

public abstract record RequestRateAdmissionEntry : TrafficAdmissionEntry;

public sealed record FixedWindowAdmissionEntry : RequestRateAdmissionEntry
{
    public required string Profile { get; init; }
    internal override string ProfileName => Profile;
    public required long PermitLimit { get; init; }
    public required TimeSpan Window { get; init; }
}

public sealed record SlidingWindowAdmissionEntry : RequestRateAdmissionEntry
{
    public required string Profile { get; init; }
    internal override string ProfileName => Profile;
    public required long PermitLimit { get; init; }
    public required TimeSpan Window { get; init; }
    public required int SegmentsPerWindow { get; init; }
}

public sealed record TokenBucketAdmissionEntry : RequestRateAdmissionEntry
{
    public required string Profile { get; init; }
    internal override string ProfileName => Profile;
    public required long TokenLimit { get; init; }
    public required long TokensPerPeriod { get; init; }
    public required TimeSpan ReplenishmentPeriod { get; init; }
}

public sealed record ConcurrencyAdmissionEntry : TrafficAdmissionEntry
{
    public required string Profile { get; init; }
    internal override string ProfileName => Profile;
    public required int PermitLimit { get; init; }
    public required int QueueLimit { get; init; }
}

public sealed record TrafficAdmissionPlan
{
    public ImmutableArray<TrafficAdmissionEntry> Entries { get; init; } = [];
}

public sealed record TrafficAdmissionLimits(
    long MinimumLimit,
    long MaximumLimit,
    TimeSpan? MinimumPeriod,
    TimeSpan? MaximumPeriod,
    int MinimumSegments,
    int MaximumSegments,
    int MinimumQueue,
    int MaximumQueue);

public sealed record TrafficAdmissionCapability(
    string Name,
    ushort ContractVersion,
    TrafficAdmissionScope Scope,
    TrafficAdmissionKind Kind,
    TrafficAdmissionRateAlgorithm? RateAlgorithm,
    TrafficAdmissionPartitionKind Partition,
    TrafficAdmissionFailureDisposition FailureDisposition,
    TrafficAdmissionLimits Limits,
    string AuthorityId,
    ContentHash BehaviorIdentity,
    int? AcquisitionOrdinal,
    string? PartitionProjectorId = null,
    ContentHash? PartitionProjectorIdentity = null,
    string? ProviderId = null,
    ContentHash? ProviderBehaviorIdentity = null,
    TimeSpan? OperationTimeout = null,
    int? MaximumConcurrentInvocations = null,
    string? LocalFallbackProfile = null,
    ContentHash? LocalFallbackIdentity = null);

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayAdmissionAuthorityState>))]
public enum GatewayAdmissionAuthorityState : byte
{
    NotRequired = 0,
    NotObserved = 1,
    Healthy = 2,
    DegradedBypass = 3,
    DegradedLocalFallback = 4,
    Unavailable = 5,
    Indeterminate = 6,
    ConfigurationConflict = 7,
}

public sealed record GatewayAdmissionProfileStatus(
    string Profile,
    TrafficAdmissionScope Scope,
    string AuthorityId,
    GatewayAdmissionAuthorityState State,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Acquired,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Rejected,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long InfrastructureFailures,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long DegradedBypasses,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long LocalFallbacks,
    DateTimeOffset? LastObservedAt,
    string? SafeDiagnosticCode);

public sealed record GatewayAdmissionStatusSnapshot(
    ushort SchemaVersion,
    ImmutableArray<GatewayAdmissionProfileStatus> Profiles,
    bool IsTruncated);

public interface IGatewayAdmissionStatusReader
{
    GatewayAdmissionStatusSnapshot GetCurrent();
    CancellationToken GetChangeToken();
}
