using System.Buffers;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HPD.Agent.Permissions;

/// <summary>Versioned, deterministic encoding independent of JSON property order.</summary>
internal static class PermissionRecordIdentity
{
    private const string Version = "hpd.permission.record.v1";
    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Default,
        Indented = false,
        SkipValidation = false
    };

    internal static byte[] Canonicalize(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options))
            WriteCanonical(writer, value);
        return buffer.WrittenSpan.ToArray();
    }

    internal static string Create(PermissionDecisionKey key, PermissionPersistenceKind kind,
        string? ruleTypeId = null, JsonElement? rule = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.PolicyDescriptorId);
        if (kind is not (PermissionPersistenceKind.Session or PermissionPersistenceKind.ValidatedRule))
            throw new InvalidOperationException("PermissionChoicePersistenceInvalid: unsupported persistence kind.");
        if (kind == PermissionPersistenceKind.Session && (ruleTypeId is not null || rule is not null))
            throw new InvalidOperationException("PermissionChoicePersistenceInvalid: session approval cannot contain a rule.");
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(Version);
            writer.WriteStringValue(kind.ToString());
            writer.WriteStringValue(key.AgentId);
            writer.WriteStringValue(key.Authority);
            writer.WriteStringValue(key.Action);
            writer.WriteStringValue(key.PolicyDescriptorId);
            if (kind == PermissionPersistenceKind.ValidatedRule)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(ruleTypeId);
                if (rule is not { } document)
                    throw new InvalidOperationException("PermissionChoicePersistenceInvalid: missing rule.");
                writer.WriteStringValue(ruleTypeId);
                writer.WriteStringValue(Hash(Canonicalize(document)));
            }
            writer.WriteEndArray();
        }
        return Hash(buffer.WrittenSpan);
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!names.Add(property.Name))
                        throw new InvalidOperationException("PermissionChoicePersistenceInvalid: duplicate rule property.");
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            // The lexical spelling of number tokens is part of rule identity.
            case JsonValueKind.Number: writer.WriteRawValue(value.GetRawText(), skipInputValidation: false); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new InvalidOperationException("PermissionChoicePersistenceInvalid: undefined rule JSON.");
        }
    }
}
