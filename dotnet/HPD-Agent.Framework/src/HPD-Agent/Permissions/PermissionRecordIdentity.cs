using System.Buffers;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HPD.Agent.Permissions;

/// <summary>Versioned, deterministic encoding independent of JSON property order.</summary>
internal static class PermissionRecordIdentity
{
    private const string Version = "hpd.permission.record.v2";
    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Default,
        Indented = false,
        SkipValidation = false
    };

    internal static string SourceIdentity(string identity, string revision)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(identity);
            writer.WriteStringValue(revision);
            writer.WriteEndArray();
        }
        return "source:" + Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    internal static byte[] Canonicalize(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options))
            WriteCanonical(writer, value);
        return buffer.WrittenSpan.ToArray();
    }

    internal static bool CanonicalEquals(JsonElement left, JsonElement right) =>
        Canonicalize(left).AsSpan().SequenceEqual(Canonicalize(right));

    internal static string Create(PermissionDecisionKey key, PermissionPersistenceKind kind,
        string? ruleTypeId = null, JsonElement? rule = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.PolicyDescriptorId);
        var scope = key.Scope ?? throw new InvalidOperationException("PermissionDecisionStateInvalid: missing approval scope.");
        ValidateScopeText(scope.SourceIdentity, 512);
        ValidateScopeText(scope.FunctionIdentity, 512);
        ValidateScopeText(scope.ContractFingerprint, 128);
        ValidateScopeText(scope.PolicyVersion, 128);
        if (!Enum.IsDefined(scope.Mode) || !Enum.IsDefined(scope.InputContractKind) ||
            !Enum.IsDefined(scope.InputValidation) || !Enum.IsDefined(scope.IngressProvenance))
            throw new InvalidOperationException("PermissionDecisionStateInvalid: invalid approval scope.");
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
            writer.WriteStringValue(scope.SourceIdentity);
            writer.WriteStringValue(scope.FunctionIdentity);
            writer.WriteStringValue(scope.ContractFingerprint);
            writer.WriteStringValue(scope.PolicyVersion);
            writer.WriteStringValue(scope.Mode.ToString());
            writer.WriteStringValue(scope.InputContractKind.ToString());
            writer.WriteStringValue(scope.InputValidation.ToString());
            writer.WriteStringValue(scope.IngressProvenance.ToString());
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

    private static void ValidateScopeText(string value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() ||
            value.Any(char.IsControl) || System.Text.Encoding.UTF8.GetByteCount(value) > maximumBytes)
            throw new InvalidOperationException("PermissionDecisionStateInvalid: invalid approval scope identity.");
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
