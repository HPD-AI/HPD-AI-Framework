using System.Security.Cryptography;
using System.Text.Json;

namespace HPD.Agent.ClientGenerator.RuntimeCatalog;

internal sealed record RuntimeCatalogIr(
    IReadOnlyList<RuntimeCatalogSchemaIr> Schemas,
    string Digest);

internal sealed record RuntimeCatalogSchemaIr(
    string Key,
    string Namespace,
    string Name,
    int Version,
    int Usage,
    string PayloadSchemaDigest,
    JsonElement PayloadSchema,
    string? Description);

internal static class RuntimeCatalogReader
{
    public static async Task<RuntimeCatalogIr> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemas", out var schemasElement) ||
            schemasElement.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("digest", out var digestElement) ||
            digestElement.GetString() is not { Length: > 0 } digest)
        {
            throw new InvalidDataException("Runtime extension catalog is invalid.");
        }
        var schemas = new List<RuntimeCatalogSchemaIr>();
        foreach (var item in schemasElement.EnumerateArray())
        {
            var key = RequiredString(item, "key");
            var extensionNamespace = RequiredString(item, "namespace");
            var name = RequiredString(item, "name");
            var version = RequiredInt(item, "version");
            var usage = RequiredInt(item, "usage");
            var payloadDigest = RequiredString(item, "payloadSchemaDigest");
            var payloadSchema = RequiredObject(item, "payloadSchema").Clone();
            var expectedKey = $"{extensionNamespace}:{name}@{version}";
            if (!string.Equals(key, expectedKey, StringComparison.Ordinal))
                throw new InvalidDataException($"Runtime catalog key '{key}' does not match '{expectedKey}'.");
            var actualDigest = CanonicalDigest(payloadSchema);
            if (!string.Equals(payloadDigest, actualDigest, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Runtime schema '{key}' payload digest does not match its payload schema text.");
            }
            schemas.Add(new RuntimeCatalogSchemaIr(
                key,
                extensionNamespace,
                name,
                version,
                usage,
                payloadDigest,
                payloadSchema,
                item.TryGetProperty("description", out var description)
                    ? description.GetString()
                    : null));
        }
        return new RuntimeCatalogIr(
            schemas.OrderBy(static value => value.Key, StringComparer.Ordinal).ToArray(),
            digest);
    }

    private static string RequiredString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) &&
        element.GetString() is { Length: > 0 } result
            ? result
            : throw new InvalidDataException($"Runtime catalog property '{property}' is missing.");

    private static int RequiredInt(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.TryGetInt32(out var result)
            ? result
            : throw new InvalidDataException($"Runtime catalog property '{property}' is missing.");

    private static JsonElement RequiredObject(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Object
            ? element
            : throw new InvalidDataException($"Runtime catalog property '{property}' is missing.");

    private static string CanonicalDigest(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, value);
        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(static item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
