using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Serialization;

public sealed class AgentEventJsonConverter : JsonConverter<AgentEvent>
{
    public override AgentEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return AgentEventSerializer.DeserializeEventJson(document.RootElement.GetRawText());
    }

    public override void Write(Utf8JsonWriter writer, AgentEvent value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        using var document = JsonDocument.Parse(AgentEventSerializer.ToJson(value));
        document.RootElement.WriteTo(writer);
    }
}
