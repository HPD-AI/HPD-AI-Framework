using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Represents a record payload.</summary>
public sealed record RecordPayload
{
    /// <summary>Gets or sets the kind.</summary>
    public required RecordPayloadKind Kind { get; init; }
    /// <summary>Gets or sets the JSON.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Json { get; init; }
    /// <summary>Gets or sets the fields.</summary>
    public Dictionary<string, JsonElement>? Fields { get; init; }
}

/// <summary>Defines the record payload kind contract.</summary>
public enum RecordPayloadKind
{
    /// <summary>Identifies JSON.</summary>
Json,
    /// <summary>Identifies field map.</summary>
FieldMap
}
