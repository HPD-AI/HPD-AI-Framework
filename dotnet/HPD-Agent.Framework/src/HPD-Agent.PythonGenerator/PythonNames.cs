using System.Text;
using HPD.Agent.SDK.Contract;

namespace HPD.Agent.PythonGenerator;

internal sealed class PythonNames
{
    private readonly IReadOnlyDictionary<string, HpdTypeIr> _types;
    private readonly IReadOnlyDictionary<string, string> _models;
    private readonly IReadOnlyDictionary<string, string> _enums;

    public PythonNames(HpdSdkIr ir)
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
    public IReadOnlyDictionary<string, string> Enums => _enums;

    public string Model(string componentId) =>
        _models.TryGetValue(componentId, out var name)
            ? name
            : throw new InvalidOperationException(
                $"Component '{componentId}' has no Python object model.");

    public string Type(HpdTypeIr type, bool optional = false, int depth = 0)
    {
        if (depth > 32)
            return "Any";
        var nullable = optional || type.Nullable;
        var mapped = type.Kind switch
        {
            "reference" when type.Reference is { Length: > 0 } reference => Reference(reference, depth),
            "primitive" => type.Primitive switch
            {
                "string" => "str",
                "boolean" => "bool",
                "integer" => "int",
                "number" => "float",
                "null" => "None",
                _ => "Any"
            },
            "literal" or "enum" => "str",
            "array" => "list[" + (type.ItemType is null ? "Any" : Type(type.ItemType, depth: depth + 1)) + "]",
            "map" => "dict[str, " + (type.ValueType is null ? "Any" : Type(type.ValueType, depth: depth + 1)) + "]",
            _ => "Any"
        };
        return nullable && mapped is not "Any" and not "None"
            ? mapped + " | None"
            : mapped;
    }

    public static string Field(string wireName)
    {
        var python = Snake(wireName);
        return PythonKeywords.Contains(python) ? python + "_" : python;
    }

    public static string Method(string operationId) => Snake(operationId);

    public static string Property(string tag) => Snake(tag);

    public static string Service(string tag) => Pascal(tag) + "Service";

    public static string Pascal(string value)
    {
        var normalized = value.StartsWith("HPD.Agent.", StringComparison.Ordinal)
            ? value["HPD.Agent.".Length..]
            : value;
        var parts = normalized.Split(
            [' ', '.', '-', '_', '/', '+'],
            StringSplitOptions.RemoveEmptyEntries);
        var result = string.Concat(parts.Select(static part =>
            part.Length == 0
                ? string.Empty
                : char.ToUpperInvariant(part[0]) + part[1..]));
        if (result.Length == 0)
            result = "GeneratedType";
        if (char.IsAsciiDigit(result[0]))
            result = "_" + result;
        return result;
    }

    public static string Snake(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        var previousWasLowerOrDigit = false;
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                if (builder.Length > 0 && builder[^1] != '_')
                    builder.Append('_');
                previousWasLowerOrDigit = false;
                continue;
            }
            if (character is >= 'A' and <= 'Z' &&
                builder.Length > 0 &&
                builder[^1] != '_' &&
                previousWasLowerOrDigit)
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(character));
            previousWasLowerOrDigit = character is >= 'a' and <= 'z' or >= '0' and <= '9';
        }
        var result = builder.ToString().Trim('_');
        if (result.Length == 0)
            result = "value";
        if (char.IsAsciiDigit(result[0]))
            result = "_" + result;
        return result;
    }

    private string Reference(string reference, int depth)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            return "Any";
        var componentId = reference[prefix.Length..]
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
        if (_models.TryGetValue(componentId, out var model))
            return model;
        if (_enums.TryGetValue(componentId, out var enumName))
            return enumName;
        return _types.TryGetValue(componentId, out var target)
            ? Type(target, depth: depth + 1)
            : "Any";
    }

    private static readonly HashSet<string> PythonKeywords = new(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue",
        "def", "del", "elif", "else", "except", "False", "finally", "for",
        "from", "global", "if", "import", "in", "is", "lambda", "None",
        "nonlocal", "not", "or", "pass", "raise", "return", "True", "try",
        "while", "with", "yield", "match", "case", "type"
    };
}
