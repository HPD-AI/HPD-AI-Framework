using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base.Records;

public sealed record RecordPayload
{
    public required RecordPayloadKind Kind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Json { get; init; }
    public Dictionary<string, JsonElement>? Fields { get; init; }
}

public enum RecordPayloadKind
{
    Json,
    FieldMap
}
