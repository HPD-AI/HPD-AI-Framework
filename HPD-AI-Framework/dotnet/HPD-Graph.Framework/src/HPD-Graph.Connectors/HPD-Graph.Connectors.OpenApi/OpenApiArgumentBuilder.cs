using System.Text.Json;

namespace HPD.Graph.Connectors.OpenApi;

public sealed class OpenApiArgumentBuilder
{
    private readonly Dictionary<string, object?> _arguments = new(StringComparer.Ordinal);

    public void Path(string name, object? value) => Add(name, value);
    public void Query(string name, object? value) => Add(name, value);
    public void Header(string name, object? value) => Add(name, value);
    public void Body(string name, object? value) => Add(name, value);

    public void Add(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _arguments[name] = Normalize(value);
    }

    public IDictionary<string, object?> Build()
        => new Dictionary<string, object?>(_arguments, StringComparer.Ordinal);

    internal void Merge(IDictionary<string, object?> arguments)
    {
        foreach (var (key, value) in arguments)
            Add(key, value);
    }

    internal void Merge(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in element.EnumerateObject())
            Add(property.Name, ConvertJsonElement(property.Value));
    }

    private static object? Normalize(object? value)
        => value is JsonElement element ? ConvertJsonElement(element) : value;

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value), StringComparer.Ordinal),
            _ => element.GetRawText()
        };
    }
}
