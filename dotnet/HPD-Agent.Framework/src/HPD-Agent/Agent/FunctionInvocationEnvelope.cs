using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Separates model-selected execution controls from a function's argument namespace.</summary>
internal static class FunctionInvocationEnvelope
{
    /// <summary>Projects a function schema into a model-choice envelope without changing its domain fields.</summary>
    internal static JsonElement CreateSchema(JsonElement sourceSchema)
    {
        if (sourceSchema.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Model-choice functions require an object schema.");
        EnsureEmbeddable(sourceSchema);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            writer.WritePropertyName("arguments");
            sourceSchema.WriteTo(writer);
            writer.WritePropertyName("invocation");
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            writer.WritePropertyName("mode");
            writer.WriteStartObject();
            writer.WriteString("type", "string");
            writer.WritePropertyName("enum");
            writer.WriteStartArray();
            writer.WriteStringValue("synchronous");
            writer.WriteStringValue("background");
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WritePropertyName("required");
            writer.WriteStartArray();
            writer.WriteStringValue("arguments");
            writer.WriteEndArray();
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>Returns the domain arguments and requested mode from an exact envelope.</summary>
    internal static AIFunctionArguments Unwrap(AIFunctionArguments source, out AgentInvocationMode? requestedMode)
    {
        var root = source.GetJson();
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Invocation envelope must be a JSON object.");
        JsonElement arguments = default;
        JsonElement invocation = default;
        var seenArguments = false;
        var seenInvocation = false;
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "arguments" when !seenArguments:
                    arguments = property.Value;
                    seenArguments = true;
                    break;
                case "invocation" when !seenInvocation:
                    invocation = property.Value;
                    seenInvocation = true;
                    break;
                default:
                    throw new InvalidOperationException("Invocation envelope contains an unknown or duplicate property.");
            }
        }
        if (!seenArguments || arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Invocation envelope requires an arguments object.");

        requestedMode = null;
        if (seenInvocation)
        {
            if (invocation.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Invocation controls must be a JSON object.");
            var seenMode = false;
            foreach (var property in invocation.EnumerateObject())
            {
                if (property.Name != "mode" || seenMode || property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("Invocation controls contain an invalid or duplicate property.");
                seenMode = true;
                requestedMode = property.Value.GetString() switch
                {
                    "synchronous" => AgentInvocationMode.Synchronous,
                    "background" => AgentInvocationMode.Background,
                    _ => throw new InvalidOperationException("Invocation mode must be synchronous or background.")
                };
            }
        }

        var prepared = ExternalFunctionInput.Prepare(arguments);
        if (prepared.Errors.Count != 0)
            throw new InvalidOperationException(prepared.Errors[0].ErrorMessage);
        var result = new AIFunctionArguments();
        foreach (var property in prepared.EffectiveJson.EnumerateObject())
            result[property.Name] = property.Value.Clone();
        result.SetJson(prepared.EffectiveJson);
        result.SetJsonSerializerOptions(source.GetJsonSerializerOptions());
        result.SetIngressProvenance(source.GetIngressProvenance());
        return result;
    }

    private static void EnsureEmbeddable(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name is "$ref" or "$dynamicRef" or "$recursiveRef" or "$id" or "$anchor" or "$dynamicAnchor" or "$recursiveAnchor")
                    throw new InvalidOperationException("The function schema contains references or identifiers that cannot be faithfully embedded in an invocation envelope.");
                EnsureEmbeddable(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) EnsureEmbeddable(item);
    }
}
