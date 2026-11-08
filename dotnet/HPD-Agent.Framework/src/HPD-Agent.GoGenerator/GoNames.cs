using System.Text;
using HPD.Agent.SDK.Contract;

namespace HPD.Agent.GoGenerator;

internal sealed class GoNames
{
    private readonly IReadOnlyDictionary<string, HpdTypeIr> _types;
    private readonly IReadOnlyDictionary<string, string> _models;
    private readonly IReadOnlyDictionary<string, string> _enums;

    public GoNames(HpdSdkIr ir)
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
                $"Component '{componentId}' has no Go model.");

    public string Type(HpdTypeIr type, bool optional = false, int depth = 0)
    {
        if (depth > 32)
            return "any";
        var pointer = optional || type.Nullable;
        var mapped = type.Kind switch
        {
            "reference" when type.Reference is { Length: > 0 } reference =>
                Reference(reference, depth),
            "primitive" => type.Primitive switch
            {
                "string" => "string",
                "boolean" => "bool",
                "integer" => type.Format == "int64" ? "int64" : "int",
                "number" => "float64",
                "null" => "any",
                _ => "any"
            },
            "literal" or "enum" => "string",
            "array" => "[]" + (type.ItemType is null
                ? "any"
                : Type(type.ItemType, depth: depth + 1)),
            "map" => "map[string]" + (type.ValueType is null
                ? "any"
                : Type(type.ValueType, depth: depth + 1)),
            _ => "any"
        };
        if (pointer &&
            mapped is not "any" and not "[]" &&
            !mapped.StartsWith("[]", StringComparison.Ordinal) &&
            !mapped.StartsWith("map[", StringComparison.Ordinal) &&
            !mapped.StartsWith("*", StringComparison.Ordinal))
        {
            return "*" + mapped;
        }
        return mapped;
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
            var name = ReferencedModelName(branch);
            if (name is not null)
                return name;
        }
        return null;
    }

    public static string Field(string wireName)
    {
        var value = Pascal(wireName);
        return GoKeywords.Contains(value) ? value + "Value" : value;
    }

    public static string Method(string operationId) => Pascal(operationId);

    public static string Service(string tag) => Pascal(tag) + "Service";

    public static string Property(string tag) => Pascal(tag);

    public static string Pascal(string value)
    {
        var normalized = value.StartsWith("HPD.Agent.", StringComparison.Ordinal)
            ? value["HPD.Agent.".Length..]
            : value;
        var builder = new StringBuilder();
        var upperNext = true;
        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                upperNext = true;
                continue;
            }
            builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
            upperNext = false;
        }
        var result = builder.Length == 0 ? "GeneratedType" : builder.ToString();
        return char.IsAsciiDigit(result[0]) ? "_" + result : result;
    }

    private string Reference(string reference, int depth)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            return "any";
        var componentId = reference[prefix.Length..]
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
        if (_models.TryGetValue(componentId, out var model))
            return "*" + model;
        if (_enums.TryGetValue(componentId, out var enumName))
            return enumName;
        return _types.TryGetValue(componentId, out var target)
            ? Type(target, depth: depth + 1)
            : "any";
    }

    private static readonly HashSet<string> GoKeywords = new(StringComparer.Ordinal)
    {
        "Break", "Default", "Func", "Interface", "Select", "Case", "Defer",
        "Go", "Map", "Struct", "Chan", "Else", "Goto", "Package", "Switch",
        "Const", "Fallthrough", "If", "Range", "Type", "Continue", "For",
        "Import", "Return", "Var"
    };
}
