using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Reads and writes opaque vector consistency tokens as bounded JSON strings.</summary>
public sealed class BaseVectorConsistencyTokenJsonConverter : JsonConverter<BaseVectorConsistencyToken>
{
    /// <inheritdoc />
    public override BaseVectorConsistencyToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && BaseVectorConsistencyToken.TryParse(reader.GetString(), out BaseVectorConsistencyToken token)
            ? token
            : throw new JsonException("The vector consistency token is invalid.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BaseVectorConsistencyToken value, JsonSerializerOptions options) => writer.WriteStringValue(value.Encode());
}
