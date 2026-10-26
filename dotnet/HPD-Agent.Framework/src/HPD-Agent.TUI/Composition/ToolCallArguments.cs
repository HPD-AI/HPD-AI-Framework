using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent.TUI.Composition;

/// <summary>Classifies the normalized shape of one tool-call argument payload.</summary>
public enum ToolArgumentKind
{
    Missing,
    Null,
    Object,
    Array,
    String,
    Number,
    Boolean
}

/// <summary>
/// Provides non-throwing, presentation-only access to a tool-call argument payload.
/// Ownership validation remains the responsibility of the AI function binder.
/// </summary>
public sealed class ToolCallArguments
{
    private ToolCallArguments(
        string rawJson,
        ToolArgumentValue root,
        bool isValidJson,
        string? diagnostic)
    {
        RawJson = rawJson;
        Root = root;
        IsValidJson = isValidJson;
        Diagnostic = diagnostic;
    }

    public string RawJson { get; }

    public bool IsValidJson { get; }

    public bool IsObject => IsValidJson && Root.IsObject;

    public ToolArgumentValue Root { get; }

    public string? Diagnostic { get; }

    public static ToolCallArguments Parse(string? rawJson)
    {
        var raw = rawJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return Invalid(raw, "Tool arguments are empty.");

        try
        {
            var node = JsonNode.Parse(raw);
            return new ToolCallArguments(raw, new ToolArgumentValue(node), isValidJson: true, diagnostic: null);
        }
        catch (JsonException)
        {
            return Invalid(raw, "Tool arguments are not valid JSON.");
        }
    }

    private static ToolCallArguments Invalid(string raw, string diagnostic)
        => new(raw, default, isValidJson: false, diagnostic);
}

/// <summary>A safe, read-only view over one value in a tool-call argument payload.</summary>
public readonly struct ToolArgumentValue
{
    private readonly JsonNode? _node;

    internal ToolArgumentValue(JsonNode? node)
    {
        _node = node;
    }

    public ToolArgumentKind Kind => _node switch
    {
        null => ToolArgumentKind.Null,
        JsonObject => ToolArgumentKind.Object,
        JsonArray => ToolArgumentKind.Array,
        JsonValue value when value.TryGetValue<string>(out _) => ToolArgumentKind.String,
        JsonValue value when value.TryGetValue<bool>(out _) => ToolArgumentKind.Boolean,
        JsonValue value when value.TryGetValue<JsonElement>(out var element) &&
                            element.ValueKind == JsonValueKind.Number => ToolArgumentKind.Number,
        _ => ToolArgumentKind.Missing
    };

    public bool IsObject => _node is JsonObject;

    public bool IsArray => _node is JsonArray;

    public bool TryGetProperty(string name, out ToolArgumentValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_node is JsonObject obj && obj.TryGetPropertyValue(name, out var node))
        {
            value = new ToolArgumentValue(node);
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetObject(string name, out ToolArgumentValue value)
        => TryGetProperty(name, out value) && value.IsObject;

    public bool TryGetArray(string name, out ToolArgumentValue value)
        => TryGetProperty(name, out value) && value.IsArray;

    public bool TryGetString(string name, out string? value)
    {
        value = null;
        return TryGetProperty(name, out var property) && property.TryGetString(out value);
    }

    public bool TryGetInt32(string name, out int value)
    {
        value = default;
        return TryGetProperty(name, out var property) && property.TryGetInt32(out value);
    }

    public bool TryGetBoolean(string name, out bool value)
    {
        value = default;
        return TryGetProperty(name, out var property) && property.TryGetBoolean(out value);
    }

    public bool TryGetString(out string? value)
    {
        value = null;
        if (_node is JsonValue node && node.TryGetValue<string>(out var text))
        {
            value = text;
            return true;
        }

        return false;
    }

    public bool TryGetInt32(out int value)
    {
        value = default;
        return _node is JsonValue node &&
               node.TryGetValue<int>(out value);
    }

    public bool TryGetBoolean(out bool value)
    {
        value = default;
        return _node is JsonValue node &&
               node.TryGetValue<bool>(out value);
    }

    public IReadOnlyList<ToolArgumentValue> EnumerateArray()
    {
        if (_node is not JsonArray array)
            return [];

        return array.Select(static node => new ToolArgumentValue(node)).ToArray();
    }
}
