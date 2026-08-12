using System.Text.Json;

namespace HPD.Base;

internal static class BaseConfidentialityProjection
{
    internal static RecordPayload Project(
        RecordPayload payload,
        CollectionDefinition collection,
        Func<BaseFieldDisclosurePolicy, BaseProjectionDisclosure> channel)
    {
        Dictionary<string, JsonElement> source = payload.Kind == RecordPayloadKind.FieldMap
            ? (payload.Fields ?? []).ToDictionary(static item => item.Key, static item => item.Value.Clone(), StringComparer.Ordinal)
            : payload.Json.ValueKind == JsonValueKind.Object
                ? payload.Json.EnumerateObject().ToDictionary(static item => item.Name, static item => item.Value.Clone(), StringComparer.Ordinal)
                : [];
        var projected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (FieldDefinition field in collection.Fields ?? [])
        {
            BaseProjectionDisclosure disclosure = channel(field.Disclosure ?? BaseFieldDisclosurePolicies.For(field.Confidentiality));
            if (disclosure == BaseProjectionDisclosure.FixedMarker)
                projected[field.Name] = Marker();
            else if (disclosure == BaseProjectionDisclosure.Include && source.TryGetValue(field.Name, out JsonElement value))
                projected[field.Name] = value.Clone();
        }
        if (payload.Kind == RecordPayloadKind.FieldMap) return payload with { Fields = projected };
        return payload with { Json = JsonSerializer.SerializeToElement(projected, HPDBaseJsonSerializerContext.Default.DictionaryStringJsonElement) };
    }

    private static JsonElement Marker()
    {
        using JsonDocument document = JsonDocument.Parse("{\"$base\":\"redacted\"}");
        return document.RootElement.Clone();
    }
}
