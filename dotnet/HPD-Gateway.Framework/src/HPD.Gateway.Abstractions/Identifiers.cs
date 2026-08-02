namespace HPD.Gateway.Abstractions;

public readonly record struct GatewaySchemaVersion(ushort Major, ushort Minor);

public readonly record struct RouteId(string Value);

public readonly record struct UpstreamId(string Value);

public readonly record struct DestinationId(string Value);

public readonly record struct ListenerId(string Value);

public readonly record struct DefinitionId(string Value);

public readonly record struct ProviderId(string Value);

public readonly record struct ProviderObjectId(string Value);

public readonly record struct DeclarationFamilyId(string Value);

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
