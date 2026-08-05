using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;

namespace HPD.Agent.AspNetCore.Serialization;

/// <summary>Routes hosted run-configuration JSON through the shared generated provider composition.</summary>
internal sealed class AgentRunConfigJsonConverter(ProviderComposition composition)
    : JsonConverter<AgentRunConfig>
{
    public override AgentRunConfig? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return HpdAgentConfigSerializer.DeserializeRunConfig(
            document.RootElement.GetRawText(), composition);
    }

    public override void Write(
        Utf8JsonWriter writer,
        AgentRunConfig value,
        JsonSerializerOptions options)
    {
        var json = HpdAgentConfigSerializer.Serialize(value, composition);
        using var document = JsonDocument.Parse(json);
        document.RootElement.WriteTo(writer);
    }
}
