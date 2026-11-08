using System.Text;
using HPD.Agent.SDK.Contract;

namespace HPD.Agent.TypeScriptGenerator;

internal sealed class TypeScriptNames
{
    private readonly IReadOnlyDictionary<string, HpdTypeIr> _types;
    private readonly IReadOnlyDictionary<string, string> _models;
    private readonly IReadOnlyDictionary<string, string> _enums;

    public TypeScriptNames(HpdSdkIr ir)
    {
        _types = ir.Types.ToDictionary(
            static type => type.ComponentId,
            static type => type.Schema,
            StringComparer.Ordinal);
        _models = ir.Types
            .Where(static type => type.Schema.ObjectModel)
            .ToDictionary(
                static type => type.ComponentId,
                static type => Pascal(type.ComponentId),
                StringComparer.Ordinal);
        _enums = ir.Types
            .Where(static type => type.Schema.Kind == "enum")
            .ToDictionary(
                static type => type.ComponentId,
                static type => Pascal(type.ComponentId),
                StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string> Models => _models;

    public string Model(string componentId) =>
        _models.TryGetValue(componentId, out var name)
            ? name
            : throw new InvalidOperationException(
                $"Component '{componentId}' has no TypeScript model.");

    public string Type(HpdTypeIr type, bool optional = false, int depth = 0)
    {
        if (depth > 32)
            return "unknown";
        var mapped = type.Kind switch
        {
            "reference" when type.Reference is { Length: > 0 } reference =>
                Reference(reference, depth),
            "primitive" => type.Primitive switch
            {
                "string" => "string",
                "boolean" => "boolean",
                "integer" or "number" => "number",
                "null" => "null",
                _ => "unknown"
            },
            "literal" => Quote(type.Literal ?? string.Empty),
            "enum" => "string",
            "array" => "Array<" + (type.ItemType is null
                ? "unknown"
                : Type(type.ItemType, depth: depth + 1)) + ">",
            "map" => "Record<string, " + (type.ValueType is null
                ? "unknown"
                : Type(type.ValueType, depth: depth + 1)) + ">",
            _ => "unknown"
        };
        return (optional || type.Nullable) && mapped is not "unknown" and not "null"
            ? mapped + " | null"
            : mapped;
    }

    public string? ReferencedModelName(HpdTypeIr? type)
    {
        if (type is null)
            return null;
        if (type.Kind == "reference" &&
            type.Reference is { Length: > 0 } reference)
        {
            const string prefix = "#/components/schemas/";
            if (reference.StartsWith(prefix, StringComparison.Ordinal))
            {
                var componentId = reference[prefix.Length..]
                    .Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                if (_models.TryGetValue(componentId, out var model))
                    return model;
            }
        }
        if (type.Branches is null)
            return null;
        foreach (var branch in type.Branches)
        {
            var result = ReferencedModelName(branch);
            if (result is not null)
                return result;
        }
        return null;
    }

    public static string Property(string value)
    {
        var result = Camel(value);
        return result is "default" or "delete" or "export" or "import" or "new" or
            "return" or "switch" or "throw" or "type" or "class" or "function"
            ? "_" + result
            : result;
    }

    public static string Method(string operationId) => Camel(operationId);

    public static string Service(string tag) => Pascal(tag) + "Service";

    public static string PropertyForTag(string tag) => Camel(tag);

    public static string Pascal(string value)
    {
        var camel = Camel(value);
        return camel.Length == 0
            ? "GeneratedType"
            : char.ToUpperInvariant(camel[0]) + camel[1..];
    }

    public static string Camel(string value)
    {
        var builder = new StringBuilder(value.Length);
        var upperNext = false;
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                upperNext = builder.Length > 0;
                continue;
            }
            if (builder.Length == 0)
                builder.Append(char.ToLowerInvariant(character));
            else
                builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
            upperNext = false;
        }
        var result = builder.Length == 0 ? "value" : builder.ToString();
        return char.IsAsciiDigit(result[0]) ? "_" + result : result;
    }

    private string Reference(string reference, int depth)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            return "unknown";
        var componentId = reference[prefix.Length..]
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
        if (_models.TryGetValue(componentId, out var model))
            return model;
        if (_enums.TryGetValue(componentId, out var enumName))
            return enumName;
        return _types.TryGetValue(componentId, out var target)
            ? Type(target, depth: depth + 1)
            : "unknown";
    }

    private static string Quote(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);
}
