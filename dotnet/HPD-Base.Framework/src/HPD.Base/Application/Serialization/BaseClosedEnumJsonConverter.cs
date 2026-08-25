using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Serializes one closed enum using its exact declared member names.</summary>
/// <typeparam name="TEnum">The closed enum type bound into the generated schema.</typeparam>
[BaseSerializerConverter("hpd.base.closed-enum-json", 1)]
public sealed class BaseClosedEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly string[] Names = Enum.GetNames<TEnum>().Order(StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } value ||
            Array.BinarySearch(Names, value, StringComparer.Ordinal) < 0 || !Enum.TryParse(value, ignoreCase: false, out TEnum result))
            throw new JsonException(BaseSchemaErrorCodes.ContractInvalid);
        return result;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        string? name = Enum.GetName(value);
        if (name is null || Array.BinarySearch(Names, name, StringComparer.Ordinal) < 0)
            throw new JsonException(BaseSchemaErrorCodes.ContractInvalid);
        writer.WriteStringValue(name);
    }
}
