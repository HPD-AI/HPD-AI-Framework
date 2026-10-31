using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>Small AOT-safe primitives used by library-generated settings codecs.</summary>
public static class HarnessConfigJson
{
    public static JsonElement Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
    public static T Read<T>(Func<T> read, string path)
    {
        try { return read(); }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidOperationException or KeyNotFoundException
            && ex is not AgentRunConfigurationException)
        { throw new AgentRunConfigurationException("HarnessConfigInvalidValue", path, "Value does not match the generated settings contract."); }
    }
    public static void Object(JsonElement value, string[] names, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new AgentRunConfigurationException("HarnessConfigInvalidType", path, "Expected an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new AgentRunConfigurationException("HarnessConfigDuplicateProperty", path + "." + property.Name, "Duplicate setting.");
            if (!names.Contains(property.Name, StringComparer.Ordinal))
                throw new AgentRunConfigurationException("HarnessConfigUnknownProperty", path + "." + property.Name, "Unknown setting; names are case-sensitive.");
        }
    }
    public static T EnumValue<T>(JsonElement value) where T : struct, Enum
        => value.ValueKind == JsonValueKind.String && Enum.TryParse<T>(value.GetString(), false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new JsonException("Invalid enum value.");
    public static ImmutableArray<T> Array<T>(JsonElement value, Func<JsonElement, string, T> read, string path)
    {
        var builder = ImmutableArray.CreateBuilder<T>(); var index = 0;
        foreach (var element in value.EnumerateArray()) builder.Add(read(element, path + "[" + index++ + "]"));
        return builder.ToImmutable();
    }
    public static ImmutableDictionary<string, T> Dictionary<T>(JsonElement value, Func<JsonElement, string, T> read, string path)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (builder.ContainsKey(property.Name)) throw new JsonException("Duplicate dictionary key.");
            builder.Add(property.Name, read(property.Value, path + "." + property.Name));
        }
        return builder.ToImmutable();
    }
    public static void WriteArray<T>(Utf8JsonWriter writer, ImmutableArray<T> values, Action<Utf8JsonWriter, T> write)
    {
        writer.WriteStartArray();
        if (!values.IsDefault) foreach (var value in values) write(writer, value);
        writer.WriteEndArray();
    }
    public static void WriteDictionary<T>(Utf8JsonWriter writer, ImmutableDictionary<string, T> values, Action<Utf8JsonWriter, T> write)
    {
        writer.WriteStartObject();
        foreach (var pair in values) { writer.WritePropertyName(pair.Key); write(writer, pair.Value); }
        writer.WriteEndObject();
    }
}
