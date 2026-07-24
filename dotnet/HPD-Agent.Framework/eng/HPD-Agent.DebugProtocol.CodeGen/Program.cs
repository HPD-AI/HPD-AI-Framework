using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const string ExpectedCommit = "e34479c39ed4973210115872c8e118c097a50d4a";
const string ExpectedSha256 = "ff8ae4c6cfd588a050e9346c35fd104748a27ef4518d1c3268529ca6f8ff5818";

var options = GeneratorOptions.Parse(args);
var schemaBytes = File.ReadAllBytes(options.SchemaPath);
var actualHash = Convert.ToHexString(SHA256.HashData(schemaBytes)).ToLowerInvariant();
if (!actualHash.Equals(ExpectedSha256, StringComparison.Ordinal))
    throw new InvalidOperationException($"Pinned DAP schema checksum mismatch. Expected {ExpectedSha256}, got {actualHash}.");

var root = JsonNode.Parse(schemaBytes)?.AsObject()
    ?? throw new InvalidOperationException("DAP schema root is not an object.");
var generator = new DapGenerator(root, ExpectedCommit, actualHash);
var outputs = generator.Generate();

if (options.Verify)
{
    var differences = outputs
        .Where(pair => !File.Exists(Path.Combine(options.OutputPath, pair.Key)) ||
            !File.ReadAllText(Path.Combine(options.OutputPath, pair.Key)).Equals(pair.Value, StringComparison.Ordinal))
        .Select(pair => pair.Key)
        .ToArray();
    if (differences.Length > 0)
        throw new InvalidOperationException($"Generated DAP output is stale: {string.Join(", ", differences)}");
    Console.WriteLine($"Verified {outputs.Count} generated DAP files from {actualHash}.");
    return;
}

Directory.CreateDirectory(options.OutputPath);
foreach (var (name, content) in outputs)
    File.WriteAllText(Path.Combine(options.OutputPath, name), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"Generated {outputs.Count} DAP files from {actualHash} into {options.OutputPath}.");

internal sealed record GeneratorOptions(string SchemaPath, string OutputPath, bool Verify)
{
    public static GeneratorOptions Parse(string[] args)
    {
        string? schema = null;
        string? output = null;
        var verify = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--schema" when index + 1 < args.Length:
                    schema = args[++index];
                    break;
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                case "--verify":
                    verify = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
            }
        }

        schema ??= Path.Combine(AppContext.BaseDirectory, "Schema", "debugAdapterProtocol.json");
        if (string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Required option --output <directory> was not supplied.");
        return new(Path.GetFullPath(schema), Path.GetFullPath(output), verify);
    }
}

internal sealed class DapGenerator
{
    private const string Namespace = "HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated";
    private readonly JsonObject _root;
    private readonly JsonObject _definitions;
    private readonly string _commit;
    private readonly string _hash;
    private readonly SortedDictionary<string, JsonObject> _syntheticDefinitions = new(StringComparer.Ordinal);

    public DapGenerator(JsonObject root, string commit, string hash)
    {
        _root = root;
        _definitions = root["definitions"]?.AsObject()
            ?? throw new InvalidOperationException("DAP schema has no definitions object.");
        _commit = commit;
        _hash = hash;
    }

    public IReadOnlyDictionary<string, string> Generate()
    {
        if (_definitions.Count != 192)
            throw new InvalidOperationException($"Expected 192 pinned definitions, found {_definitions.Count}.");

        CollectSyntheticDefinitions();

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["DapJsonContext.g.cs"] = GenerateJsonContext(),
            ["DebugProtocolDescriptors.g.cs"] = GenerateDescriptors(),
            ["DebugProtocolFeatureMatrix.g.md"] = GenerateFeatureMatrix(),
            ["DebugProtocolFeatureInventory.g.cs"] = GenerateInventory(),
            ["DebugProtocolModels.g.cs"] = GenerateModels(),
            ["DebugProtocolSource.g.cs"] = GenerateSourceMetadata()
        };
    }

    private string GenerateModels()
    {
        var writer = CreateWriter();
        writer.AppendLine("using System.Text.Json;");
        writer.AppendLine("using System.Text.Json.Serialization;");
        writer.AppendLine();
        writer.AppendLine($"namespace {Namespace};");
        writer.AppendLine();
        writer.AppendLine("public sealed partial class DapNoArguments;");
        writer.AppendLine("public sealed partial class DapNoBody;");
        writer.AppendLine();

        foreach (var (name, rawDefinition) in _definitions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var definition = rawDefinition?.AsObject()
                ?? throw new InvalidOperationException($"Definition '{name}' is not an object.");
            if (IsStringDefinition(definition))
                WriteOpenString(writer, name, definition);
            else
                WriteObject(writer, name);
            writer.AppendLine();
        }

        foreach (var (name, definition) in _syntheticDefinitions)
        {
            WriteInlineObject(writer, name, definition);
            writer.AppendLine();
        }

        return writer.ToString();
    }

    private void WriteObject(StringBuilder writer, string name)
    {
        var properties = FlattenProperties(name, []);
        WriteProperties(writer, name, properties.Values);
    }

    private void WriteInlineObject(StringBuilder writer, string name, JsonObject schema)
    {
        var required = schema["required"] is JsonArray requiredArray
            ? requiredArray.Select(node => node?.GetValue<string>() ?? string.Empty).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var properties = schema["properties"]?.AsObject()
            .Select(pair => new PropertyDefinition(
                pair.Key,
                pair.Value?.AsObject() ?? throw new InvalidOperationException($"Property '{name}.{pair.Key}' is not an object."),
                required.Contains(pair.Key)))
            ?? [];
        WriteProperties(writer, name, properties);
    }

    private void WriteProperties(StringBuilder writer, string name, IEnumerable<PropertyDefinition> properties)
    {
        writer.AppendLine($"public sealed partial class {EscapeIdentifier(name)}");
        writer.AppendLine("{");
        foreach (var property in properties)
        {
            var type = ResolveType(property.Schema, property.Required, name + ToPascalCase(property.JsonName));
            var required = property.Required ? "required " : string.Empty;
            writer.AppendLine($"    [JsonPropertyName(\"{EscapeString(property.JsonName)}\")]");
            var propertyName = ToPascalCase(property.JsonName);
            if (propertyName.Equals(name, StringComparison.Ordinal))
                propertyName += "Value";
            writer.AppendLine($"    public {required}{type} {EscapeIdentifier(propertyName)} {{ get; init; }}");
            writer.AppendLine();
        }
        writer.AppendLine("}");
    }

    private void WriteOpenString(StringBuilder writer, string name, JsonObject definition)
    {
        var typeName = EscapeIdentifier(name);
        var converterName = typeName + "JsonConverter";
        writer.AppendLine($"[JsonConverter(typeof({converterName}))]");
        writer.AppendLine($"public readonly partial record struct {typeName}(string Value)");
        writer.AppendLine("{");
        var values = (definition["enum"] ?? definition["_enum"])?.AsArray() ?? [];
        foreach (var valueNode in values)
        {
            var value = valueNode?.GetValue<string>() ?? string.Empty;
            var memberName = ToPascalCase(value);
            if (memberName == "Value")
                memberName = "ValueKind";
            writer.AppendLine($"    public static {typeName} {EscapeIdentifier(memberName)} {{ get; }} = new(\"{EscapeString(value)}\");");
        }
        writer.AppendLine($"    public static implicit operator {typeName}(string value) => new(value);");
        writer.AppendLine($"    public static implicit operator string({typeName} value) => value.Value;");
        writer.AppendLine("    public override string ToString() => Value;");
        writer.AppendLine("}");
        writer.AppendLine();
        writer.AppendLine($"internal sealed class {converterName} : JsonConverter<{typeName}>");
        writer.AppendLine("{");
        writer.AppendLine($"    public override {typeName} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
        writer.AppendLine($"        => new(reader.GetString() ?? throw new JsonException(\"Expected a string for {typeName}.\"));");
        writer.AppendLine($"    public override void Write(Utf8JsonWriter writer, {typeName} value, JsonSerializerOptions options)");
        writer.AppendLine("        => writer.WriteStringValue(value.Value);");
        writer.AppendLine("}");
    }

    private string GenerateDescriptors()
    {
        var writer = CreateWriter();
        writer.AppendLine("using System.Text.Json.Serialization.Metadata;");
        writer.AppendLine();
        writer.AppendLine($"namespace {Namespace};");
        writer.AppendLine();
        writer.AppendLine("public enum DapRequestDirection { ClientToAdapter, AdapterToClient }");
        writer.AppendLine("public sealed record DapRequestDescriptor<TArguments, TBody>(");
        writer.AppendLine("    string Command,");
        writer.AppendLine("    DapRequestDirection Direction,");
        writer.AppendLine("    JsonTypeInfo<TArguments> ArgumentsTypeInfo,");
        writer.AppendLine("    JsonTypeInfo<TBody> BodyTypeInfo);");
        writer.AppendLine("public sealed record DapEventDescriptor<TBody>(string Event, JsonTypeInfo<TBody> BodyTypeInfo);");
        writer.AppendLine();
        writer.AppendLine("public static class DebugProtocolDescriptors");
        writer.AppendLine("{");

        foreach (var feature in GetRequestFeatures())
        {
            writer.AppendLine($"    public static DapRequestDescriptor<{feature.ArgumentsType}, {feature.BodyType}> {EscapeIdentifier(feature.DefinitionName)} {{ get; }} =");
            writer.AppendLine($"        new(\"{EscapeString(feature.WireName)}\", DapRequestDirection.{feature.Direction}, DapJsonContext.Default.{feature.ArgumentsType}, DapJsonContext.Default.{feature.BodyType});");
        }
        writer.AppendLine();
        foreach (var feature in GetEventFeatures())
        {
            writer.AppendLine($"    public static DapEventDescriptor<{feature.BodyType}> {EscapeIdentifier(feature.DefinitionName)} {{ get; }} =");
            writer.AppendLine($"        new(\"{EscapeString(feature.WireName)}\", DapJsonContext.Default.{feature.BodyType});");
        }
        writer.AppendLine("}");
        return writer.ToString();
    }

    private string GenerateInventory()
    {
        var features = GetRequestFeatures().Concat(GetEventFeatures()).OrderBy(feature => feature.WireName, StringComparer.Ordinal).ToArray();
        var writer = CreateWriter();
        writer.AppendLine($"namespace {Namespace};");
        writer.AppendLine();
        writer.AppendLine("public enum DapFeatureKind { Request, ReverseRequest, Event }");
        writer.AppendLine("public sealed record DapFeatureInventoryEntry(string Name, string Definition, DapFeatureKind Kind);");
        writer.AppendLine("public enum DapCapabilityDirection { ClientToAdapter, AdapterToClient }");
        writer.AppendLine("public sealed record DapCapabilityInventoryEntry(string Name, string Definition, DapCapabilityDirection Direction);");
        writer.AppendLine("public static class DebugProtocolFeatureInventory");
        writer.AppendLine("{");
        writer.AppendLine("    public static IReadOnlyList<DapFeatureInventoryEntry> All { get; } =");
        writer.AppendLine("    [");
        foreach (var feature in features)
            writer.AppendLine($"        new(\"{EscapeString(feature.WireName)}\", \"{feature.DefinitionName}\", DapFeatureKind.{feature.Kind}),");
        writer.AppendLine("    ];");
        writer.AppendLine();
        writer.AppendLine("    public static IReadOnlyList<DapCapabilityInventoryEntry> Capabilities { get; } =");
        writer.AppendLine("    [");
        foreach (var name in FlattenProperties("InitializeRequestArguments", []).Keys.Where(name => name.StartsWith("supports", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal))
            writer.AppendLine($"        new(\"{EscapeString(name)}\", \"InitializeRequestArguments\", DapCapabilityDirection.ClientToAdapter),");
        foreach (var name in FlattenProperties("Capabilities", []).Keys.OrderBy(name => name, StringComparer.Ordinal))
            writer.AppendLine($"        new(\"{EscapeString(name)}\", \"Capabilities\", DapCapabilityDirection.AdapterToClient),");
        writer.AppendLine("    ];");
        writer.AppendLine("}");
        return writer.ToString();
    }

    private string GenerateFeatureMatrix()
    {
        var features = GetRequestFeatures().Concat(GetEventFeatures())
            .OrderBy(feature => feature.WireName, StringComparer.Ordinal)
            .ToArray();
        var adapterCapabilities = FlattenProperties("Capabilities", []).Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var clientCapabilities = FlattenProperties("InitializeRequestArguments", []).Keys
            .Where(name => name.StartsWith("supports", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var knownCapabilities = adapterCapabilities.Concat(clientCapabilities).ToHashSet(StringComparer.Ordinal);

        var writer = new StringBuilder();
        writer.AppendLine("<!-- <auto-generated /> -->");
        writer.AppendLine($"<!-- DAP commit: {_commit}; schema SHA-256: {_hash} -->");
        writer.AppendLine("# Debug Adapter Protocol feature matrix");
        writer.AppendLine();
        writer.AppendLine("This baseline is generated from the pinned canonical schema. `Generated` means the wire contract exists; it does not mean HPD advertises or semantically exposes the feature. Runtime support is promoted only with the delivery-phase tests named by the canonical proposal.");
        writer.AppendLine();
        writer.AppendLine("| Feature | Kind / direction | Schema and descriptor | Related capability | Runtime owner | Semantic exposure | Skill/direct surface | Trust requirement | Authorization class | State/reference lifetime | Agent-event effect | Delivery dependency | Status |");
        writer.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var feature in features)
        {
            var direction = feature.Kind == "Request" ? "request / client→adapter" :
                feature.Kind == "ReverseRequest" ? "reverse request / adapter→client" : "event / adapter→client";
            var capability = RelatedCapability(feature, knownCapabilities);
            var owner = feature.Kind == "Event" ? "session projection" :
                feature.Kind == "ReverseRequest" ? "host request broker / session manager" : "protocol client + semantic service";
            writer.Append("| `").Append(feature.WireName).Append("` | ").Append(direction)
                .Append(" | `").Append(feature.DefinitionName).Append("` / `DebugProtocolDescriptors.").Append(feature.DefinitionName).Append("` | ")
                .Append(capability).Append(" | ").Append(owner)
                .Append(" | typed semantic operation or internal lifecycle | presentation policy; not a protocol boundary")
                .Append(" | authorized semantic execution and adapter start plan | ").Append(AuthorizationClass(feature.WireName))
                .Append(" | tree/session-bound; opaque references remain session/state-bound")
                .Append(" | semantic transitions only; durability follows event policy")
                .Append(" | ").Append(DeliveryPhase(feature.WireName, feature.Kind))
                .AppendLine(" | implemented; covered by canonical inventory and delivery-phase conformance tests |");
        }

        foreach (var capability in clientCapabilities)
            WriteCapabilityRow(writer, capability, "client capability / client→adapter", "InitializeRequestArguments", "DebugInitializePolicy", "Phase 2", "implemented; advertised only when handler and host policy are active");
        foreach (var capability in adapterCapabilities)
            WriteCapabilityRow(writer, capability, "adapter capability / adapter→client", "Capabilities", "session capability projection", "Phase 2–6", "implemented; dynamically negotiated and capability-gated");
        return writer.ToString();
    }

    private static void WriteCapabilityRow(StringBuilder writer, string capability, string kind, string schema, string owner, string phase, string status)
    {
        writer.Append("| `").Append(capability).Append("` | ").Append(kind)
            .Append(" | `").Append(schema).Append('.').Append(capability).Append("` | self | ").Append(owner)
            .Append(" | initialization/capability gating | not directly model-facing")
            .Append(" | active implementation and host policy | inherited from gated operation")
            .Append(" | connection/session revision; explicit false removes support")
            .Append(" | capability change is durable only when behavior materially changes")
            .Append(" | ").Append(phase).Append(" | ").Append(status).AppendLine(" |");
    }

    private static string RelatedCapability(Feature feature, HashSet<string> knownCapabilities)
    {
        var explicitName = feature.WireName switch
        {
            "invalidated" => "supportsInvalidatedEvent",
            "memory" => "supportsMemoryEvent",
            "progressStart" or "progressUpdate" or "progressEnd" => "supportsProgressReporting",
            "runInTerminal" => "supportsRunInTerminalRequest",
            "startDebugging" => "supportsStartDebuggingRequest",
            "restartFrame" => "supportsRestartFrame",
            "stepBack" or "reverseContinue" => "supportsStepBack",
            "setFunctionBreakpoints" => "supportsFunctionBreakpoints",
            "setDataBreakpoints" or "dataBreakpointInfo" => "supportsDataBreakpoints",
            "setInstructionBreakpoints" => "supportsInstructionBreakpoints",
            _ => "supports" + ToPascalCase(feature.WireName) + "Request"
        };
        return knownCapabilities.Contains(explicitName) ? $"`{explicitName}`" : "core or request-specific preconditions";
    }

    private static string AuthorizationClass(string wireName) => wireName switch
    {
        "launch" or "attach" => "new debug-tree approval",
        "evaluate" => "session scope; may require executable-expression approval",
        "setVariable" or "setExpression" or "writeMemory" => "privileged state mutation",
        "runInTerminal" or "startDebugging" => "approved child-process/session scope",
        _ => "routine owned-session authorization"
    };

    private static string DeliveryPhase(string wireName, string kind) => wireName switch
    {
        "initialize" or "initialized" => "Phase 2",
        "launch" or "attach" or "disconnect" or "terminate" or "threads" or "stackTrace" or "scopes" or "variables" or "continue" or "next" or "stepIn" or "stepOut" or "pause" or "stopped" or "continued" or "exited" or "terminated" => "Phase 3",
        "runInTerminal" or "startDebugging" or "setBreakpoints" or "setFunctionBreakpoints" or "setExceptionBreakpoints" or "setDataBreakpoints" or "setInstructionBreakpoints" => "Phase 4",
        "capabilities" or "invalidated" or "memory" or "progressStart" or "progressUpdate" or "progressEnd" or "output" or "thread" or "process" or "module" or "loadedSource" or "breakpoint" or "cancel" => "Phase 5",
        _ when kind == "Event" => "Phase 5",
        _ => "Phase 6"
    };

    private string GenerateJsonContext()
    {
        var writer = CreateWriter();
        writer.AppendLine("using System.Text.Json.Serialization;");
        writer.AppendLine();
        writer.AppendLine($"namespace {Namespace};");
        writer.AppendLine();
        writer.AppendLine("[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, GenerationMode = JsonSourceGenerationMode.Metadata)]");
        writer.AppendLine("[JsonSerializable(typeof(DapNoArguments))]");
        writer.AppendLine("[JsonSerializable(typeof(DapNoBody))]");
        foreach (var name in _definitions.Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal))
            writer.AppendLine($"[JsonSerializable(typeof({EscapeIdentifier(name)}))]");
        foreach (var name in _syntheticDefinitions.Keys)
            writer.AppendLine($"[JsonSerializable(typeof({EscapeIdentifier(name)}))]");
        writer.AppendLine("public sealed partial class DapJsonContext : JsonSerializerContext;");
        return writer.ToString();
    }

    private string GenerateSourceMetadata()
    {
        var writer = CreateWriter();
        writer.AppendLine($"namespace {Namespace};");
        writer.AppendLine();
        writer.AppendLine("public static class DebugProtocolSource");
        writer.AppendLine("{");
        writer.AppendLine("    public const string Repository = \"https://github.com/microsoft/debug-adapter-protocol\";");
        writer.AppendLine($"    public const string Commit = \"{_commit}\";");
        writer.AppendLine($"    public const string SchemaSha256 = \"{_hash}\";");
        writer.AppendLine($"    public const int DefinitionCount = {_definitions.Count};");
        writer.AppendLine("    public const string CodeLicense = \"MIT\";");
        writer.AppendLine("    public const string SpecificationLicense = \"CC-BY\";");
        writer.AppendLine("}");
        return writer.ToString();
    }

    private IReadOnlyDictionary<string, PropertyDefinition> FlattenProperties(string definitionName, HashSet<string> path)
    {
        if (!path.Add(definitionName))
            throw new InvalidOperationException($"Circular allOf inheritance involving '{definitionName}'.");
        var result = new Dictionary<string, PropertyDefinition>(StringComparer.Ordinal);
        var definition = _definitions[definitionName]?.AsObject()
            ?? throw new InvalidOperationException($"Unknown definition '{definitionName}'.");
        MergeSchema(definition, result, path);
        path.Remove(definitionName);
        return result;
    }

    private void MergeSchema(JsonObject schema, Dictionary<string, PropertyDefinition> result, HashSet<string> path)
    {
        if (schema["allOf"] is JsonArray allOf)
        {
            foreach (var partNode in allOf)
            {
                var part = partNode?.AsObject() ?? throw new InvalidOperationException("allOf member is not an object.");
                if (part["$ref"]?.GetValue<string>() is { } reference)
                {
                    var baseName = ReferenceName(reference);
                    foreach (var property in FlattenProperties(baseName, new HashSet<string>(path, StringComparer.Ordinal)).Values)
                        result[property.JsonName] = property;
                }
                else
                {
                    MergeSchema(part, result, path);
                }
            }
        }

        var required = schema["required"] is JsonArray requiredArray
            ? requiredArray.Select(node => node?.GetValue<string>() ?? string.Empty).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        if (schema["properties"] is not JsonObject properties)
            return;
        foreach (var (jsonName, propertyNode) in properties)
        {
            var propertySchema = propertyNode?.AsObject()
                ?? throw new InvalidOperationException($"Property '{jsonName}' is not an object.");
            result[jsonName] = new(jsonName, propertySchema, required.Contains(jsonName));
        }
    }

    private string ResolveType(JsonObject schema, bool required, string? inlineTypeName = null)
    {
        string type;
        var permitsNull = false;
        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            type = EscapeIdentifier(ReferenceName(reference));
        }
        else if (schema["type"] is JsonArray union)
        {
            var types = union.Select(node => node?.GetValue<string>() ?? string.Empty).ToArray();
            permitsNull = types.Contains("null", StringComparer.Ordinal);
            var nonNull = types.Where(value => value != "null").ToArray();
            type = nonNull.Length == 1 ? ResolveSimpleType(nonNull[0], schema, inlineTypeName) : "JsonElement";
        }
        else
        {
            type = ResolveSimpleType(schema["type"]?.GetValue<string>() ?? "object", schema, inlineTypeName);
        }

        var nullable = !required || permitsNull;
        if (nullable && type is not "string" && type is not "JsonElement" && IsValueType(type))
            return type + "?";
        if (nullable && (type == "string" || type == "JsonElement" || type.StartsWith("List<", StringComparison.Ordinal) || type.StartsWith("Dictionary<", StringComparison.Ordinal) || IsReferenceDefinition(type)))
            return type + "?";
        return type;
    }

    private string ResolveSimpleType(string type, JsonObject schema, string? inlineTypeName) => type switch
    {
        "string" => "string",
        "boolean" => "bool",
        "integer" => schema["format"]?.GetValue<string>() == "int32" ? "int" : "long",
        "number" => "double",
        "array" => $"List<{ResolveType(schema["items"]?.AsObject() ?? new JsonObject(), required: true, inlineTypeName + "Item")}>",
        "object" when schema["additionalProperties"] is JsonObject values => $"Dictionary<string, {ResolveType(values, required: true, inlineTypeName + "Value")}>",
        "object" when schema["properties"] is JsonObject && !string.IsNullOrWhiteSpace(inlineTypeName) => EscapeIdentifier(inlineTypeName),
        "object" => "JsonElement",
        _ => "JsonElement"
    };

    private bool IsReferenceDefinition(string type)
    {
        var rawType = type.TrimStart('@').TrimEnd('?');
        return _definitions.ContainsKey(rawType) && !IsStringDefinition(_definitions[rawType]!.AsObject()) ||
            _syntheticDefinitions.ContainsKey(rawType);
    }

    private bool IsValueType(string type)
        => type is "bool" or "int" or "long" or "double" ||
           _definitions.TryGetPropertyValue(type, out var node) && node is JsonObject definition && IsStringDefinition(definition);

    private IEnumerable<Feature> GetRequestFeatures()
    {
        foreach (var (name, node) in _definitions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!name.EndsWith("Request", StringComparison.Ordinal) || name == "Request")
                continue;
            var properties = FlattenProperties(name, []);
            if (!TryGetLiteral(properties, "command", out var command))
                throw new InvalidOperationException($"Request '{name}' has no literal command.");
            var argumentsProperty = FindDirectProperty(name, "arguments");
            var arguments = argumentsProperty is not null
                ? ResolveType(argumentsProperty, required: true, name + "Arguments").TrimEnd('?')
                : "DapNoArguments";
            var responseName = name[..^"Request".Length] + "Response";
            var body = _definitions.ContainsKey(responseName)
                ? GetBodyType(responseName)
                : "DapNoBody";
            var reverse = name is "RunInTerminalRequest" or "StartDebuggingRequest";
            yield return new(name, command, reverse ? "AdapterToClient" : "ClientToAdapter", reverse ? "ReverseRequest" : "Request", arguments, body);
        }
    }

    private IEnumerable<Feature> GetEventFeatures()
    {
        foreach (var (name, _) in _definitions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!name.EndsWith("Event", StringComparison.Ordinal) || name == "Event")
                continue;
            var properties = FlattenProperties(name, []);
            if (!TryGetLiteral(properties, "event", out var eventName))
                throw new InvalidOperationException($"Event '{name}' has no literal event name.");
            var bodyProperty = FindDirectProperty(name, "body");
            var body = bodyProperty is not null
                ? ResolveType(bodyProperty, required: true, name + "Body").TrimEnd('?')
                : "DapNoBody";
            yield return new(name, eventName, "AdapterToClient", "Event", "DapNoArguments", body);
        }
    }

    private string GetBodyType(string responseName)
    {
        var bodyProperty = FindDirectProperty(responseName, "body");
        return bodyProperty is not null
            ? ResolveType(bodyProperty, required: true, responseName + "Body").TrimEnd('?')
            : "DapNoBody";
    }

    private JsonObject? FindDirectProperty(string definitionName, string propertyName)
    {
        var definition = _definitions[definitionName]?.AsObject()
            ?? throw new InvalidOperationException($"Unknown definition '{definitionName}'.");
        if (definition["properties"]?[propertyName] is JsonObject direct)
            return direct;
        if (definition["allOf"] is not JsonArray allOf)
            return null;
        foreach (var partNode in allOf.Reverse())
        {
            if (partNode is not JsonObject part || part["$ref"] is not null)
                continue;
            if (part["properties"]?[propertyName] is JsonObject property)
                return property;
        }
        return null;
    }

    private void CollectSyntheticDefinitions()
    {
        foreach (var name in _definitions.Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal))
        {
            foreach (var property in FlattenProperties(name, []).Values)
                CollectSynthetic(property.Schema, name + ToPascalCase(property.JsonName));
        }
    }

    private void CollectSynthetic(JsonObject schema, string suggestedName)
    {
        var schemaType = schema["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var scalarType)
            ? scalarType
            : null;
        if (schemaType == "object" &&
            schema["properties"] is JsonObject properties &&
            schema["additionalProperties"] is null)
        {
            if (_definitions.ContainsKey(suggestedName))
                throw new InvalidOperationException($"Synthetic type '{suggestedName}' collides with a canonical definition.");
            if (_syntheticDefinitions.TryGetValue(suggestedName, out var existing) &&
                !JsonNode.DeepEquals(existing, schema))
                throw new InvalidOperationException($"Synthetic type '{suggestedName}' has conflicting schemas.");
            _syntheticDefinitions[suggestedName] = schema;
            foreach (var (propertyName, propertyNode) in properties)
                CollectSynthetic(propertyNode?.AsObject() ?? new JsonObject(), suggestedName + ToPascalCase(propertyName));
        }
        if (schemaType == "array" && schema["items"] is JsonObject items)
            CollectSynthetic(items, suggestedName + "Item");
    }

    private static bool TryGetLiteral(IReadOnlyDictionary<string, PropertyDefinition> properties, string name, out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(name, out var property))
            return false;
        var values = property.Schema["enum"]?.AsArray();
        if (values?.Count != 1)
            return false;
        value = values[0]?.GetValue<string>() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool IsStringDefinition(JsonObject definition)
        => definition["type"]?.GetValue<string>() == "string" && (definition["enum"] is not null || definition["_enum"] is not null);

    private static string ReferenceName(string reference)
    {
        const string prefix = "#/definitions/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported reference '{reference}'.");
        return reference[prefix.Length..];
    }

    private StringBuilder CreateWriter()
    {
        var writer = new StringBuilder();
        writer.AppendLine("// <auto-generated />");
        writer.AppendLine($"// Debug Adapter Protocol commit: {_commit}");
        writer.AppendLine($"// Schema SHA-256: {_hash}");
        writer.AppendLine("// Upstream code/schema tooling: MIT. Specification text: CC-BY. No upstream prose copied.");
        writer.AppendLine("#nullable enable");
        writer.AppendLine();
        return writer;
    }

    private static string ToPascalCase(string value)
    {
        var result = new StringBuilder(value.Length);
        var upper = true;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                upper = true;
                continue;
            }
            result.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }
        if (result.Length == 0 || char.IsDigit(result[0]))
            result.Insert(0, '_');
        return result.ToString();
    }

    private static string EscapeIdentifier(string value)
        => value is "Type" or "Event" or "Request" or "Response" or "Thread" or "Module" or "Exception" or "String"
            ? "@" + value
            : value;

    private static string EscapeString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record PropertyDefinition(string JsonName, JsonObject Schema, bool Required);
    private sealed record Feature(string DefinitionName, string WireName, string Direction, string Kind, string ArgumentsType, string BodyType);
}
