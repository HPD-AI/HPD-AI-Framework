using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Reads and writes bounded finite vectors as JSON float32 arrays.</summary>
public sealed class BaseVectorJsonConverter : JsonConverter<BaseVector>
{
    /// <inheritdoc />
    public override BaseVector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A vector must be a JSON array.");
        var values = new List<float>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetSingle(out float value) || !float.IsFinite(value)) throw new JsonException("A vector must contain finite float32 numbers.");
            if (values.Count == 32_768) throw new JsonException("A vector exceeds the maximum supported dimensions.");
            values.Add(value);
        }
        if (reader.TokenType != JsonTokenType.EndArray || values.Count == 0) throw new JsonException("A vector must contain one or more values.");
        return BaseVector.Create(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BaseVector value, JsonSerializerOptions options)
    {
        if (value.Dimensions == 0) throw new JsonException("The default vector is invalid.");
        writer.WriteStartArray();
        for (int index = 0; index < value.Dimensions; index++) writer.WriteNumberValue(value[index]);
        writer.WriteEndArray();
    }
}
