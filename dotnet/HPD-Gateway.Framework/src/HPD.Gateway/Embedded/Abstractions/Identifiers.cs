using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Gateway;

public readonly record struct GatewaySchemaVersion(ushort Major, ushort Minor);

public readonly record struct RouteId(string Value);

public readonly record struct UpstreamId(string Value);

public readonly record struct DestinationId(string Value);

public readonly record struct ListenerId(string Value);

public readonly record struct DefinitionId(string Value);

public readonly record struct ProviderId(string Value);

public readonly record struct ProviderObjectId(string Value);

public readonly record struct DiscoveryProfileId(string Value);

public readonly record struct ServiceDiscoveryName(string Value);

public readonly record struct ServiceDiscoveryEndpointName(string Value);

public readonly record struct DeclarationFamilyId(string Value);

[JsonConverter(typeof(CandidateIdJsonConverter))]
public readonly record struct CandidateId(string Value);

internal sealed class CandidateIdJsonConverter : JsonConverter<CandidateId>
{
    public override CandidateId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (!GatewayIdentifier.IsCanonical(value))
            throw new JsonException("The candidate identity is invalid.");
        return new CandidateId(value!);
    }

    public override void Write(Utf8JsonWriter writer, CandidateId value, JsonSerializerOptions options)
    {
        if (!GatewayIdentifier.IsCanonical(value.Value))
            throw new JsonException("The candidate identity is invalid.");
        writer.WriteStringValue(value.Value);
    }
}

public static class GatewayIdentifier
{
    public const int MaximumUtf8Bytes = 128;

    public static bool IsCanonical(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumUtf8Bytes)
        {
            return false;
        }

        if (!IsAsciiAlphaNumeric(value[0]))
        {
            return false;
        }

        var utf8Bytes = 0;
        foreach (var character in value)
        {
            if (character > 0x7f ||
                !(IsAsciiAlphaNumeric(character) || character is '.' or '_' or '-'))
            {
                return false;
            }

            utf8Bytes++;
        }

        return utf8Bytes <= MaximumUtf8Bytes;
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';
}
