using System.Text.Json;

namespace HPD.Base;

internal sealed class DefaultBaseRecordRedactor : IBaseRecordRedactor
{
    /// <summary>Executes the redact record operation.</summary>
    public RecordEnvelope RedactRecord(
        RecordEnvelope record,
        CollectionDefinition collection,
        BasePolicyEvaluation policy,
        VisibilityLevel view)
    {
        var allowed = AllowedFields(collection, policy.EffectiveReadMask, view);
        var payload = RedactPayload(record.Payload, collection, allowed, out var omitted);
        return record with
        {
            Payload = payload,
            Policy = omitted.Length == 0
                ? record.Policy
                : new RecordPolicyMetadata
                {
                    Redacted = true,
                    OmittedFields = omitted
                }
        };
    }

    /// <summary>Executes the redact page operation.</summary>
    public RecordPage RedactPage(
        RecordPage page,
        CollectionDefinition collection,
        BasePolicyEvaluation policy,
        VisibilityLevel view)
    {
        return page with
        {
            Items = page.Items
                .Select(record => RedactRecord(record, collection, policy, view))
                .ToArray()
        };
    }

    private static HashSet<string> AllowedFields(
        CollectionDefinition collection,
        FieldMask? readMask,
        VisibilityLevel view)
    {
        var hasFieldMetadata = collection.Fields is { Length: > 0 };
        FieldDefinition[] visible = hasFieldMetadata
            ? collection.Fields!.Where(field => FieldVisible(field, view)).ToArray()
            : [];

        if (!hasFieldMetadata && (readMask is null || readMask.Mode is FieldMaskMode.Unspecified or FieldMaskMode.AllowAll))
        {
            return new HashSet<string>(["*"], StringComparer.Ordinal);
        }

        IEnumerable<FieldDefinition> masked = readMask?.Mode switch
        {
            null or FieldMaskMode.Unspecified or FieldMaskMode.AllowAll => visible,
            FieldMaskMode.DenyAll => [],
            FieldMaskMode.IncludeOnly => visible.Where(field => (readMask.Include ?? []).Contains(field.Id, StringComparer.Ordinal)),
            FieldMaskMode.Exclude => visible.Where(field => !(readMask.Exclude ?? []).Contains(field.Id, StringComparer.Ordinal)),
            _ => [],
        };
        return masked.Select(static field => field.WireName).ToHashSet(StringComparer.Ordinal);
    }

    private static bool FieldVisible(FieldDefinition field, VisibilityLevel view)
    {
        if ((field.Visibility?.Visibility ?? VisibilityLevel.Public) > view)
        {
            return false;
        }

        if (view == VisibilityLevel.Public)
        {
            return !field.Hidden
                && !field.System
                && field.Visibility?.WriteOnly != true
                && field.Visibility?.AdminOnly != true;
        }

        return field.Visibility?.WriteOnly != true;
    }

    private static RecordPayload RedactPayload(
        RecordPayload payload,
        CollectionDefinition collection,
        HashSet<string> allowed,
        out string[] omitted)
    {
        Dictionary<string, FieldDefinition> declared = (collection.Fields ?? []).ToDictionary(static field => field.WireName, StringComparer.Ordinal);
        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            var fields = payload.Fields ?? [];
            var kept = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var omittedList = new List<string>();
            foreach (FieldDefinition field in collection.Fields ?? [])
            {
                BaseRecordDisclosure disclosure = field.Disclosure?.RecordRead ?? BaseConfidentialityPolicy.Default(field.Confidentiality).RecordRead;
                bool policyAllowed = allowed.Contains("*") || allowed.Contains(field.WireName);
                if (!policyAllowed || disclosure == BaseRecordDisclosure.Omit) { if (fields.ContainsKey(field.WireName)) omittedList.Add(field.WireName); continue; }
                if (disclosure == BaseRecordDisclosure.FixedMarker) { kept[field.WireName] = RedactedElement(); continue; }
                if (fields.TryGetValue(field.WireName, out JsonElement value)) kept[field.WireName] = value.Clone();
            }
            foreach ((string key, JsonElement value) in fields)
                if (!declared.ContainsKey(key) && allowed.Contains("*")) kept[key] = value.Clone();
            omitted = omittedList.ToArray();
            return payload with { Fields = kept };
        }

        if (payload.Json.ValueKind != JsonValueKind.Object)
        {
            omitted = [];
            return payload;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            var omittedList = new List<string>();
            Dictionary<string, JsonElement> source = payload.Json.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value.Clone(), StringComparer.Ordinal);
            foreach (FieldDefinition field in collection.Fields ?? [])
            {
                BaseRecordDisclosure disclosure = field.Disclosure?.RecordRead ?? BaseConfidentialityPolicy.Default(field.Confidentiality).RecordRead;
                bool policyAllowed = allowed.Contains("*") || allowed.Contains(field.WireName);
                if (!policyAllowed || disclosure == BaseRecordDisclosure.Omit) { if (source.ContainsKey(field.WireName)) omittedList.Add(field.WireName); continue; }
                if (disclosure == BaseRecordDisclosure.FixedMarker)
                {
                    writer.WritePropertyName(field.WireName); RedactedElement().WriteTo(writer);
                }
                else if (source.TryGetValue(field.WireName, out JsonElement value))
                {
                    writer.WritePropertyName(field.WireName); value.WriteTo(writer);
                }
            }
            foreach ((string key, JsonElement value) in source)
                if (!declared.ContainsKey(key) && allowed.Contains("*")) { writer.WritePropertyName(key); value.WriteTo(writer); }

            writer.WriteEndObject();
            omitted = omittedList.ToArray();
        }

        var document = JsonDocument.Parse(stream.ToArray());
        return payload with { Json = document.RootElement.Clone() };
    }

    private static JsonElement RedactedElement()
    {
        using JsonDocument document = JsonDocument.Parse("{\"$base\":\"redacted\"}");
        return document.RootElement.Clone();
    }
}
