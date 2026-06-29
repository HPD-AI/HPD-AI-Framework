using System.Text.Json;
using HPD.Base.Query;
using HPD.Base.Results;

namespace HPD.Base.Stores;

public sealed record StoreCapabilityDescriptor
{
    public required string StoreId { get; init; }
    public required string StoreKind { get; init; }
    public required string StoreVersion { get; init; }
    public required CrudCapability Crud { get; init; }
    public required QueryCapability Query { get; init; }
    public RevisionCapability? Revision { get; init; }
    public StreamingCapability? Streaming { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record CrudCapability
{
    public bool List { get; init; }
    public bool Get { get; init; }
    public bool Create { get; init; }
    public bool Patch { get; init; }
    public bool Replace { get; init; }
    public bool Delete { get; init; }
    public IdAuthority IdAuthority { get; init; }
    public TimestampAuthority TimestampAuthority { get; init; }
    public ConsistencyModel Consistency { get; init; }
}

public enum IdAuthority { Runtime, Store, Client, Hybrid }
public enum TimestampAuthority { Runtime, Store, Client, Hybrid, None }
public enum ConsistencyModel { Strong, Eventual, Session, StoreDefined }

public sealed record RevisionCapability
{
    public bool Supported { get; init; }
    public RevisionGuarantee Guarantee { get; init; }
    public bool Patch { get; init; }
    public bool Delete { get; init; }
}

public sealed record StreamingCapability
{
    public bool Supported { get; init; }
    public int? MaxItems { get; init; }
    public bool RequiresStableSort { get; init; }
}
