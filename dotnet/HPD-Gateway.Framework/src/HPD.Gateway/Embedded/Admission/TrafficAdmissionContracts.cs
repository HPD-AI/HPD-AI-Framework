using System.Collections.Immutable;
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
    int? AcquisitionOrdinal);
