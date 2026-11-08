using System.Text.Json;

namespace HPD.Agent.Client;

/// <summary>
/// AOT-safe builder for the stable per-run fields shared by HPD input events.
/// Complex extension policies remain lossless JSON.
/// </summary>
public sealed record HpdAgentRunConfig
{
    public string? ProviderKey { get; init; }
    public string? ModelId { get; init; }
    public string? AdditionalSystemInstructions { get; init; }
    public HpdAgentRunChatConfig? Chat { get; init; }
    public IReadOnlyDictionary<string, bool>? PermissionOverrides { get; init; }
    public bool? CoalesceDeltas { get; init; }
    public bool? SkipTools { get; init; }
    public string? RunTimeout { get; init; }
    public JsonElement? Security { get; init; }
    public JsonElement? Sandbox { get; init; }
    public JsonElement? Compaction { get; init; }
    public JsonElement? Goals { get; init; }
    public JsonElement? StructuredOutput { get; init; }

    /// <summary>Serializes only fields explicitly set by the caller.</summary>
    public JsonElement ToJsonElement()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            WriteString(writer, "providerKey", ProviderKey);
            WriteString(writer, "modelId", ModelId);
            WriteString(
                writer,
                "additionalSystemInstructions",
                AdditionalSystemInstructions);
            if (Chat is not null)
            {
                writer.WritePropertyName("chat");
                Chat.WriteTo(writer);
            }
            if (PermissionOverrides is not null)
            {
                writer.WritePropertyName("permissionOverrides");
                writer.WriteStartObject();
                foreach (var item in PermissionOverrides)
                    writer.WriteBoolean(item.Key, item.Value);
                writer.WriteEndObject();
            }
            WriteBoolean(writer, "coalesceDeltas", CoalesceDeltas);
            WriteBoolean(writer, "skipTools", SkipTools);
            WriteString(writer, "runTimeout", RunTimeout);
            WriteElement(writer, "security", Security);
            WriteElement(writer, "sandbox", Sandbox);
            WriteElement(writer, "compaction", Compaction);
            WriteElement(writer, "goals", Goals);
            WriteElement(writer, "structuredOutput", StructuredOutput);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
            writer.WriteString(name, value);
    }

    private static void WriteBoolean(Utf8JsonWriter writer, string name, bool? value)
    {
        if (value is { } boolean)
            writer.WriteBoolean(name, boolean);
    }

    private static void WriteElement(Utf8JsonWriter writer, string name, JsonElement? value)
    {
        if (value is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } element)
            return;
        writer.WritePropertyName(name);
        element.WriteTo(writer);
    }
}

/// <summary>Stable chat sampling options nested under RunConfig.chat.</summary>
public sealed record HpdAgentRunChatConfig
{
    public double? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public double? TopP { get; init; }
    public double? FrequencyPenalty { get; init; }
    public double? PresencePenalty { get; init; }

    internal void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        if (Temperature is { } temperature)
            writer.WriteNumber("temperature", temperature);
        if (MaxOutputTokens is { } maxOutputTokens)
            writer.WriteNumber("maxOutputTokens", maxOutputTokens);
        if (TopP is { } topP)
            writer.WriteNumber("topP", topP);
        if (FrequencyPenalty is { } frequencyPenalty)
            writer.WriteNumber("frequencyPenalty", frequencyPenalty);
        if (PresencePenalty is { } presencePenalty)
            writer.WriteNumber("presencePenalty", presencePenalty);
        writer.WriteEndObject();
    }
}
