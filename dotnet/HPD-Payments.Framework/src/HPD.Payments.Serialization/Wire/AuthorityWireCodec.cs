using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Payments.Serialization.Wire;

/// <summary>Reads, writes and hashes bounded authority wire documents using only generated metadata.</summary>
public static class AuthorityWireCodec
{
    /// <summary>Reads a document with explicit reader ranges and preserves complete owned bytes.</summary>
    /// <param name="utf8">UTF-8 input; it is defensively copied before return.</param>
    /// <param name="minimumSemanticVersion">Lowest semantic version this reader interprets.</param>
    /// <param name="maximumSemanticVersion">Highest semantic version this reader interprets.</param>
    /// <param name="maximumRepresentationVersion">Highest representation version this reader understands.</param>
    /// <param name="limits">Resource limits.</param>
    /// <returns>A typed compatibility result. Malformed input is unsupported; unknown versions are quarantined.</returns>
    public static WireReadResult Read(
        ReadOnlySpan<byte> utf8,
        int minimumSemanticVersion,
        int maximumSemanticVersion,
        int maximumRepresentationVersion,
        WireReadLimits? limits = null)
    {
        var actualLimits = limits ?? WireReadLimits.Default;
        actualLimits.Validate();
        var owned = utf8.ToArray();
        if (owned.Length == 0 || owned.Length > actualLimits.MaximumDocumentBytes)
            return new(CompatibilityDisposition.Unsupported, null, null, owned, "document-size");
        if (minimumSemanticVersion <= 0 || maximumSemanticVersion < minimumSemanticVersion || maximumRepresentationVersion <= 0)
            return new(CompatibilityDisposition.Indeterminate, null, null, owned, "invalid-reader-range");

        try
        {
            var options = new JsonReaderOptions { MaxDepth = actualLimits.MaximumDepth, CommentHandling = JsonCommentHandling.Disallow };
            var reader = new Utf8JsonReader(owned, options);
            var document = JsonSerializer.Deserialize(ref reader, PaymentsJsonContext.Default.AuthorityWireDocument);
            if (document is null || string.IsNullOrWhiteSpace(document.Kind) || document.SemanticVersion <= 0 || document.RepresentationVersion <= 0)
                return new(CompatibilityDisposition.Unsupported, null, document, owned, "missing-required-header");
            if (document.SemanticFields.Count > actualLimits.MaximumSemanticFields ||
                (document.UnknownProperties?.Count ?? 0) > actualLimits.MaximumUnknownProperties)
                return new(CompatibilityDisposition.Unsupported, null, document, owned, "member-count");
            if (!AuthorityWireRegistry.TryResolve(document.Kind, out var family))
                return new(CompatibilityDisposition.Quarantined, null, document, owned, "unknown-discriminator");
            if (document.SemanticVersion < minimumSemanticVersion)
                return new(CompatibilityDisposition.Unsupported, family, document, owned, "semantic-version-too-old");
            if (document.SemanticVersion > maximumSemanticVersion || document.RepresentationVersion > maximumRepresentationVersion)
                return new(CompatibilityDisposition.Quarantined, family, document, owned, "newer-version");
            return new(CompatibilityDisposition.Supported, family, document, owned, "supported");
        }
        catch (JsonException)
        {
            return new(CompatibilityDisposition.Unsupported, null, null, owned, "malformed-json");
        }
    }

    /// <summary>Writes a structurally validated document using source-generated metadata.</summary>
    /// <param name="document">Document to write.</param>
    /// <returns>Owned UTF-8 representation.</returns>
    /// <exception cref="ArgumentException">Header or discriminator is invalid.</exception>
    public static byte[] Write(AuthorityWireDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!AuthorityWireRegistry.TryResolve(document.Kind, out _) || document.SemanticVersion <= 0 || document.RepresentationVersion <= 0)
            throw new ArgumentException("A known discriminator and positive versions are required.", nameof(document));
        return JsonSerializer.SerializeToUtf8Bytes(document, PaymentsJsonContext.Default.AuthorityWireDocument);
    }

    /// <summary>Computes a representation-independent digest over explicit semantic fields only.</summary>
    /// <param name="document">Document whose supported semantic fields are hashed.</param>
    /// <returns>Lowercase SHA-256 hexadecimal digest.</returns>
    /// <remarks>Unknown top-level representation properties and representation version are intentionally excluded.</remarks>
    public static string ComputeSemanticDigest(AuthorityWireDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!AuthorityWireRegistry.TryResolve(document.Kind, out _))
            throw new ArgumentException("Unknown discriminators cannot acquire semantic digests.", nameof(document));

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", document.Kind);
            writer.WriteNumber("semanticVersion", document.SemanticVersion);
            writer.WritePropertyName("semanticFields");
            writer.WriteStartObject();
            foreach (var pair in document.SemanticFields.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(pair.Key);
                WriteCanonicalElement(writer, pair.Value);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in element.EnumerateArray()) WriteCanonicalElement(writer, value);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer)) writer.WriteNumberValue(integer);
                else if (element.TryGetDecimal(out var decimalValue)) writer.WriteNumberValue(decimalValue);
                else writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new ArgumentException("Undefined JSON has no canonical semantic encoding.", nameof(element));
        }
    }
}
