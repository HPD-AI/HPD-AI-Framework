using System.Text.Json;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Schema;

namespace HPD.Base.Runtime.Policy;

internal sealed class DefaultBaseRecordRedactor : IBaseRecordRedactor
{
    public RecordEnvelope RedactRecord(
        RecordEnvelope record,
        CollectionDefinition collection,
        BasePolicyEvaluation policy,
        VisibilityLevel view)
    {
        var allowed = AllowedFields(collection, policy.EffectiveReadMask, view);
        var payload = RedactPayload(record.Payload, allowed, out var omitted);
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
        var allowed = new HashSet<string>(
            hasFieldMetadata
                ? collection.Fields!
                    .Where(field => FieldVisible(field, view))
                    .Select(field => field.Name)
                : [],
            StringComparer.Ordinal);

        if (!hasFieldMetadata && (readMask is null || readMask.Mode is FieldMaskMode.Unspecified or FieldMaskMode.AllowAll))
        {
            allowed.Add("*");
            return allowed;
        }

        if (readMask is null || readMask.Mode == FieldMaskMode.Unspecified || readMask.Mode == FieldMaskMode.AllowAll)
        {
            return allowed;
        }

        if (readMask.Mode == FieldMaskMode.DenyAll)
        {
            allowed.Clear();
            return allowed;
        }

        if (readMask.Mode == FieldMaskMode.IncludeOnly)
        {
            allowed.IntersectWith(readMask.Include ?? []);
            return allowed;
        }

        if (readMask.Mode == FieldMaskMode.Exclude)
        {
            allowed.ExceptWith(readMask.Exclude ?? []);
        }

        return allowed;
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
        HashSet<string> allowed,
        out string[] omitted)
    {
        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            var fields = payload.Fields ?? [];
            if (allowed.Contains("*"))
            {
                omitted = [];
                return payload;
            }

            var kept = fields
                .Where(pair => allowed.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            omitted = fields.Keys.Where(key => !allowed.Contains(key)).ToArray();
            return payload with { Fields = kept };
        }

        if (payload.Json.ValueKind != JsonValueKind.Object)
        {
            omitted = [];
            return payload;
        }

        if (allowed.Contains("*"))
        {
            omitted = [];
            return payload;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            var omittedList = new List<string>();
            foreach (var property in payload.Json.EnumerateObject())
            {
                if (allowed.Contains(property.Name))
                {
                    property.WriteTo(writer);
                }
                else
                {
                    omittedList.Add(property.Name);
                }
            }

            writer.WriteEndObject();
            omitted = omittedList.ToArray();
        }

        var document = JsonDocument.Parse(stream.ToArray());
        return payload with { Json = document.RootElement.Clone() };
    }
}
