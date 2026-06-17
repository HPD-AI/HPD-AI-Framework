using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal static class ThreadEventJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();
    public static JsonSerializerOptions CompactOptions { get; } = CreateOptions(writeIndented: false);

    private static JsonSerializerOptions CreateOptions(bool writeIndented = true)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new ThreadEventJsonConverter());
        options.TypeInfoResolverChain.Add(new SessionJsonContext());
        options.TypeInfoResolverChain.Add(AgentEventJsonContext.Default);

        foreach (var resolver in AIJsonUtilities.DefaultOptions.TypeInfoResolverChain)
        {
            if (resolver is not null)
                options.TypeInfoResolverChain.Add(resolver);
        }

        options.AddAIContentType<ImageContent>("hpd:image");
        options.AddAIContentType<AudioContent>("hpd:audio");
        options.AddAIContentType<VideoContent>("hpd:video");
        options.AddAIContentType<DocumentContent>("hpd:document");

        options.MakeReadOnly();
        return options;
    }
}

internal sealed class ThreadEventJsonConverter : JsonConverter<AgentEvent>
{
    private static readonly HashSet<string> ThreadOmittedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "sessionId",
        "threadId",
        "version",
        "channel",
        "kind",
        "direction",
        "eventFlowId",
        "canInterrupt",
        "exchangeTimestampNs",
        "metadata",
        "traceId",
        "spanId",
        "parentSpanId",
        "extensions"
    };

    public override AgentEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return AgentEventSerializer.DeserializeEventJson(document.RootElement.GetRawText());
    }

    public override void Write(Utf8JsonWriter writer, AgentEvent value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        using var document = JsonDocument.Parse(AgentEventSerializer.ToJson(value));
        writer.WriteStartObject();

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (ThreadOmittedProperties.Contains(property.Name))
                continue;

            if (property.NameEquals("timestamp") && property.Value.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(property.Value.GetString(), out var timestamp) &&
                    timestamp == default)
                {
                    continue;
                }
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
