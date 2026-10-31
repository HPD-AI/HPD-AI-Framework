using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Serialization;

public sealed record HpdConfigSourceLocation(long Line, long Column);
public sealed record HpdConfigDocument(JsonNode? Root, IReadOnlyDictionary<string, HpdConfigSourceLocation> Locations);

internal static class HpdJsonDocumentParser
{
    internal static HpdConfigDocument Parse(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var lines = new List<int> { 0 };
        for (var index = 0; index < bytes.Length; index++) if (bytes[index] == '\n') lines.Add(index + 1);
        var map = new Dictionary<string, HpdConfigSourceLocation>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read()) throw new JsonException("Empty configuration document.");
        Visit(ref reader, "", bytes, lines, map);
        if (reader.Read()) throw new JsonException("Expected one JSON document.");
        return new(JsonNode.Parse(text), new ReadOnlyDictionary<string, HpdConfigSourceLocation>(map));
    }
    private static HpdConfigSourceLocation Location(long offset, byte[] bytes, List<int> lines)
    {
        var index = lines.BinarySearch((int)offset);
        if (index < 0) index = ~index - 1;
        return new(index + 1, Encoding.UTF8.GetCharCount(bytes, lines[index], (int)offset - lines[index]) + 1);
    }
    private static void Visit(ref Utf8JsonReader reader, string path, byte[] bytes, List<int> lines,
        Dictionary<string, HpdConfigSourceLocation> map)
    {
        map.TryAdd(path, Location(reader.TokenStartIndex, bytes, lines));
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name.");
                var name = reader.GetString()!;
                var child = path.Length == 0 ? name : path + "." + name;
                var location = Location(reader.TokenStartIndex, bytes, lines);
                if (!seen.Add(name)) throw new JsonException($"Duplicate configuration property '{child}' at line {location.Line}, column {location.Column}.");
                map[child] = location;
                if (!reader.Read()) throw new JsonException("Missing property value.");
                Visit(ref reader, child, bytes, lines, map);
            }
            if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Incomplete object.");
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            var index = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) Visit(ref reader, path + "[" + index++ + "]", bytes, lines, map);
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Incomplete array.");
        }
    }
}
