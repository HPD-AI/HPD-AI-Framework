using System.Text.Json;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal sealed record CodingExplorationArgs(
    string? Path,
    string? Pattern,
    int? Offset,
    int? Limit,
    bool? Recursive);

internal static class CodingExplorationArgsParser
{
    public static CodingExplorationArgs Parse(string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
        {
            return new CodingExplorationArgs(null, null, null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;
            return new CodingExplorationArgs(
                ReadString(root, "path"),
                ReadString(root, "pattern"),
                ReadInt(root, "offset"),
                ReadInt(root, "limit"),
                ReadBool(root, "recursive"));
        }
        catch (JsonException)
        {
            return new CodingExplorationArgs(null, null, null, null, null);
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool? ReadBool(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
