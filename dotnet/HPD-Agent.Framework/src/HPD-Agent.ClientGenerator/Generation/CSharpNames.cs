using System.Security.Cryptography;
using System.Text;
using HPD.Agent.SDK.Contract;

namespace HPD.Agent.ClientGenerator.Generation;

internal sealed class CSharpNames
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "HpdAgentClient",
        "HpdAgentClientOptions",
        "HpdAgentJsonContext",
        "HpdAgentEvent",
        "HpdAgentInputEvent",
        "HpdAgentResponseInput",
        "HpdAgentEventDelivery",
        "HpdAgentSseMessage",
        "HpdAgentApiException",
        "HpdAgentContractMetadata",
        "HpdAgentRoute",
        "HpdAgentThreadKey",
        "HpdAgentGeneratedServices"
    };

    private readonly IReadOnlyDictionary<string, HpdTypeIr> _types;
    private readonly IReadOnlyDictionary<string, string> _modelNames;
    private readonly IReadOnlyDictionary<string, string> _componentEnumNames;
    private readonly IReadOnlyDictionary<(string ComponentId, string PropertyName), string> _inlineEnumNames;
    private readonly IReadOnlyDictionary<string, ExtensibleEnum> _enums;

    public CSharpNames(HpdSdkIr ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        _types = ir.Types.ToDictionary(
            static type => type.ComponentId,
            static type => type.Schema,
            StringComparer.Ordinal);

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(Reserved, StringComparer.Ordinal);
        foreach (var type in ir.Types.OrderBy(static value => value.ComponentId, StringComparer.Ordinal))
        {
            if (!type.Schema.ObjectModel)
                continue;
            var candidate = TypeIdentifier(type.ComponentId);
            if (!used.Add(candidate))
                candidate = candidate + "_" + ShortHash(type.ComponentId);
            used.Add(candidate);
            names.Add(type.ComponentId, candidate);
        }
        _modelNames = names;

        var enums = new Dictionary<string, ExtensibleEnum>(StringComparer.Ordinal);
        var componentEnums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in ir.Types.OrderBy(static value => value.ComponentId, StringComparer.Ordinal))
        {
            if (type.Schema.Kind != "enum" ||
                type.Schema.EnumValues is not { Count: > 0 } values)
            {
                continue;
            }
            var candidate = TypeIdentifier(type.ComponentId);
            if (!used.Add(candidate))
                candidate += "_" + ShortHash(type.ComponentId);
            componentEnums.Add(type.ComponentId, candidate);
            enums.Add(candidate, new ExtensibleEnum(candidate, values));
        }
        _componentEnumNames = componentEnums;

        var inlineEnums = new Dictionary<(string ComponentId, string PropertyName), string>();
        foreach (var model in names)
        {
            var type = _types[model.Key];
            if (type.Properties is null)
                continue;
            foreach (var property in type.Properties)
            {
                if (string.Equals(property.Name, "type", StringComparison.Ordinal) ||
                    property.Type.Kind != "enum" ||
                    property.Type.EnumValues is not { Count: > 0 } values)
                {
                    continue;
                }
                var candidate = model.Value + PropertyIdentifier(property.Name);
                if (!used.Add(candidate))
                    candidate += "_" + ShortHash(model.Key + "|" + property.Name);
                inlineEnums.Add((model.Key, property.Name), candidate);
                enums.Add(candidate, new ExtensibleEnum(candidate, values));
            }
        }
        _inlineEnumNames = inlineEnums;
        _enums = enums;
    }

    public IReadOnlyDictionary<string, string> ModelNames => _modelNames;

    public string ForComponent(string componentId) =>
        _modelNames.TryGetValue(componentId, out var name)
            ? name
            : throw new InvalidOperationException(
                $"Component '{componentId}' has no generated C# object model.");

    public IReadOnlyList<ExtensibleEnum> Enums =>
        _enums.Values.OrderBy(static value => value.Name, StringComparer.Ordinal).ToArray();

    public HpdTypeIr Type(string componentId) =>
        _types.TryGetValue(componentId, out var type)
            ? type
            : throw new InvalidOperationException(
                $"Component '{componentId}' has no HPD Agent SDK IR type.");

    public IReadOnlySet<string> PropertyNames(string componentId)
    {
        var type = Type(componentId);
        return type.Properties is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : type.Properties
                .Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
    }

    public HpdTypeIr PropertyType(string componentId, string propertyName) =>
        Type(componentId).Properties?.SingleOrDefault(property =>
            string.Equals(property.Name, propertyName, StringComparison.Ordinal))?.Type
        ?? throw new InvalidOperationException(
            $"Component '{componentId}' has no '{propertyName}' property in HPD Agent SDK IR.");

    public string MapPropertyType(
        string componentId,
        string propertyName,
        bool optional = false)
    {
        if (_inlineEnumNames.TryGetValue((componentId, propertyName), out var enumName))
            return Nullable(enumName, optional || PropertyType(componentId, propertyName).Nullable);
        return MapType(PropertyType(componentId, propertyName), optional);
    }

    public string MapType(HpdTypeIr type, bool optional = false, int depth = 0)
    {
        if (depth > 32)
            return Nullable("JsonElement", optional || type.Nullable);
        var nullable = optional || type.Nullable;
        switch (type.Kind)
        {
            case "reference" when type.Reference is { Length: > 0 } reference:
                var componentId = ReferenceId(reference);
                if (_modelNames.TryGetValue(componentId, out var modelName))
                    return Nullable(modelName, nullable);
                if (_componentEnumNames.TryGetValue(componentId, out var enumName))
                    return Nullable(enumName, nullable);
                return _types.TryGetValue(componentId, out var target)
                    ? MapType(target, nullable, depth + 1)
                    : Nullable("JsonElement", nullable);
            case "primitive":
                return type.Primitive switch
                {
                    "string" => Nullable("string", nullable),
                    "boolean" => Nullable("bool", nullable),
                    "integer" => Nullable(type.Format == "int64" ? "long" : "int", nullable),
                    "number" => Nullable(type.Format == "float" ? "float" : "double", nullable),
                    _ => Nullable("JsonElement", nullable)
                };
            case "literal":
                return Nullable("string", nullable);
            case "enum":
                return Nullable("string", nullable);
            case "array":
                return Nullable(
                    "List<" + (type.ItemType is null
                        ? "JsonElement"
                        : MapType(type.ItemType, depth: depth + 1)) + ">",
                    nullable);
            case "map":
                return Nullable(
                    "Dictionary<string, " +
                    (type.ValueType is null
                        ? "JsonElement"
                        : MapType(type.ValueType, depth: depth + 1)) + ">",
                    nullable);
            default:
                return Nullable("JsonElement", nullable);
        }
    }

    public string? ReferencedModelName(HpdTypeIr? type)
    {
        if (type is null)
            return null;
        if (type.Kind == "reference" &&
            type.Reference is { Length: > 0 } reference &&
            _modelNames.TryGetValue(ReferenceId(reference), out var name))
        {
            return name;
        }
        if (type.Branches is null)
            return null;
        foreach (var branch in type.Branches)
        {
            var branchName = ReferencedModelName(branch);
            if (branchName is not null)
                return branchName;
        }
        return null;
    }

    public string? ComponentForModelName(string modelName) =>
        _modelNames.FirstOrDefault(pair =>
            string.Equals(pair.Value, modelName, StringComparison.Ordinal)).Key;

    public static string TypeInfoName(string typeExpression) => TypeInfoNameCore(typeExpression);

    public static string TypeIdentifier(string value)
    {
        var normalized = value.StartsWith("HPD.Agent.", StringComparison.Ordinal)
            ? value["HPD.Agent.".Length..]
            : value;
        var segments = normalized
            .Split(['.', '+', '/', '-', ' ', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Pascal)
            .ToArray();
        var result = segments.Length == 0 ? "GeneratedType" : string.Concat(segments);
        if (char.IsAsciiDigit(result[0]))
            result = "_" + result;
        return result;
    }

    public static string PropertyIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length + 1);
        var upperNext = true;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                upperNext = true;
                continue;
            }
            if (builder.Length == 0 && char.IsAsciiDigit(character))
                builder.Append('_');
            builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
            upperNext = false;
        }
        var result = builder.Length == 0 ? "Value" : builder.ToString();
        return Reserved.Contains(result) ? result + "Value" : result;
    }

    public static string ParameterIdentifier(string value)
    {
        var property = PropertyIdentifier(value);
        return char.ToLowerInvariant(property[0]) + property[1..];
    }

    public static string TypeInfoNameCore(string typeExpression)
    {
        var simple = typeExpression.TrimEnd('?');
        if (simple.StartsWith("List<", StringComparison.Ordinal) && simple.EndsWith('>'))
            simple = "List" + TypeInfoNameCore(simple[5..^1]);
        if (simple.StartsWith("Dictionary<string, ", StringComparison.Ordinal) && simple.EndsWith('>'))
            simple = "Dictionary" + TypeInfoNameCore(simple["Dictionary<string, ".Length..^1]);
        return TypeIdentifier(simple);
    }

    private static string Pascal(string value)
    {
        if (value.Length == 0)
            return string.Empty;
        if (value.All(static character => !char.IsLetter(character) || char.IsUpper(character)))
            return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string Nullable(string type, bool nullable) =>
        nullable && !type.EndsWith('?')
            ? type + "?"
            : type;

    private static string ReferenceId(string reference)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported schema reference '{reference}'.");
        return reference[prefix.Length..]
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }

    private static string ShortHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
}

internal sealed record ExtensibleEnum(string Name, IReadOnlyList<string> Values);
