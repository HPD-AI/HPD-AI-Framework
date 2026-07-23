using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>Marks a partial CLR type for reusable, source-generated AI input-contract generation.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class AIInputContractAttribute : Attribute;

/// <summary>Describes one reusable AI input contract.</summary>
public interface IAIInputContract
{
    /// <summary>Gets the CLR value type, or <see langword="null"/> for a data-driven contract.</summary>
    Type? BoundType { get; }

    /// <summary>Gets the immutable canonical JSON Schema.</summary>
    JsonElement JsonSchema { get; }

    /// <summary>Gets the stable SHA-256 fingerprint of the canonical schema.</summary>
    string CanonicalSchemaFingerprint { get; }

    /// <summary>Validates, binds, and canonicalizes one model argument object.</summary>
    AIFunctionBindingResult Bind(JsonElement arguments);
}

/// <summary>Describes one reusable AI input contract producing <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The generated CLR input type.</typeparam>
public interface IAIInputContract<T> : IAIInputContract;

/// <summary>Creates reusable generated AI input contracts without reflection.</summary>
public static class AIInputContract
{
    /// <summary>Combines a generated direct CLR binder with HPD canonical-schema validation and writing.</summary>
    /// <typeparam name="T">The generated CLR input type.</typeparam>
    /// <param name="schema">The generated HPD canonical schema.</param>
    /// <param name="binder">The generated reflection-free binder.</param>
    /// <returns>A reusable typed input contract.</returns>
    public static IAIInputContract<T> Create<T>(
        JsonElement schema,
        Func<JsonElement, T> binder) =>
        new GeneratedContract<T>(
            CanonicalJsonInputContract.Create(schema),
            binder ?? throw new ArgumentNullException(nameof(binder)));

    /// <summary>Writes a generated contract's canonical schema to a UTF-8 JSON writer.</summary>
    /// <param name="contract">The explicit generated input contract.</param>
    /// <param name="writer">The destination JSON writer.</param>
    public static void WriteSchema(IAIInputContract contract, Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(writer);
        contract.JsonSchema.WriteTo(writer);
    }

    /// <summary>Writes a generated contract's canonical schema as UTF-8 JSON.</summary>
    /// <param name="contract">The explicit generated input contract.</param>
    /// <param name="destination">The writable destination stream.</param>
    /// <param name="cancellationToken">Cancels asynchronous flushing.</param>
    public static async ValueTask WriteSchemaAsync(
        IAIInputContract contract,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        using var writer = new Utf8JsonWriter(destination);
        WriteSchema(contract, writer);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Atomically exports a generated contract to a deterministic canonical-schema sidecar.</summary>
    /// <param name="contract">The explicit generated input contract.</param>
    /// <param name="outputPath">The destination schema path.</param>
    /// <param name="cancellationToken">Cancels schema writing.</param>
    public static async ValueTask ExportSchemaAsync(
        IAIInputContract contract,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new ArgumentException("The output path has no parent directory.", nameof(outputPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            "." + Path.GetFileName(fullOutputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await WriteSchemaAsync(contract, stream, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed class GeneratedContract<T>(
        CanonicalJsonInputContract canonical,
        Func<JsonElement, T> binder) : IAIInputContract<T>
    {
        public Type? BoundType => typeof(T);
        public JsonElement JsonSchema => canonical.JsonSchema;
        public string CanonicalSchemaFingerprint => canonical.CanonicalSchemaFingerprint;

        public AIFunctionBindingResult Bind(JsonElement arguments)
        {
            var normalized = canonical.Bind(arguments);
            if (normalized.Errors.Count != 0) return normalized;
            try
            {
                return AIFunctionBindingResult.Success(binder(normalized.EffectiveJson), normalized.EffectiveJson);
            }
            catch (HPDToolArgumentException exception)
            {
                return AIFunctionBindingResult.Failure(new ValidationError
                {
                    Property = exception.PropertyName,
                    ErrorMessage = exception.Message,
                    ErrorCode = exception.ErrorCode
                });
            }
        }
    }
}

internal sealed class CanonicalJsonInputContract : IAIInputContract
{
    private const int MaximumSchemaBytes = 262_144;
    private const int MaximumDepth = 32;
    private const int MaximumNodes = 2_048;
    private const int MaximumProperties = 512;
    private readonly JsonElement _schema;
    private readonly ContractNode _root;

    private CanonicalJsonInputContract(JsonElement schema, ContractNode root)
    {
        _schema = schema;
        _root = root;
        CanonicalSchemaFingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(schema.GetRawText()))).ToLowerInvariant();
    }

    public Type? BoundType => null;
    public JsonElement JsonSchema => _schema;
    public string CanonicalSchemaFingerprint { get; }

    public static CanonicalJsonInputContract Create(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("A script input schema is required.");
        var normalized = Normalize(schema);
        var nodeCount = 0;
        return new(normalized, ContractNode.Compile(normalized, "$", isRoot: true, depth: 0, ref nodeCount));
    }

    private static JsonElement Normalize(JsonElement schema)
    {
        if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > MaximumSchemaBytes)
            throw new InvalidDataException($"Script input schemas cannot exceed {MaximumSchemaBytes} UTF-8 bytes.");
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteNormalized(schema, writer);
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteNormalized(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = value.EnumerateObject().ToArray();
                if (properties.Select(static property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new InvalidDataException("Script input schemas cannot contain duplicate object properties.");
                foreach (var property in properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteNormalized(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteNormalized(item, writer);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    public AIFunctionBindingResult Bind(JsonElement arguments)
    {
        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
                _root.WriteEffective(arguments, writer, string.Empty);
            using var document = JsonDocument.Parse(buffer.WrittenMemory);
            var effective = document.RootElement.Clone();
            return AIFunctionBindingResult.Success(effective, effective);
        }
        catch (HPDToolArgumentException exception)
        {
            return AIFunctionBindingResult.Failure(new ValidationError
            {
                Property = exception.PropertyName,
                ErrorMessage = exception.Message,
                ErrorCode = exception.ErrorCode
            });
        }
    }

    private abstract class ContractNode
    {
        protected ContractNode(JsonElement? defaultValue) => DefaultValue = defaultValue?.Clone();
        public JsonElement? DefaultValue { get; }
        public abstract void WriteEffective(JsonElement value, Utf8JsonWriter writer, string path);

        public static ContractNode Compile(
            JsonElement schema,
            string path,
            bool isRoot,
            int depth,
            ref int nodeCount)
        {
            if (depth > MaximumDepth)
                throw new InvalidDataException($"Schema '{path}' exceeds the maximum depth of {MaximumDepth}.");
            if (++nodeCount > MaximumNodes)
                throw new InvalidDataException($"A script input schema cannot contain more than {MaximumNodes} contract nodes.");
            if (schema.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Script input schema '{path}' must be an object.");
            JsonElement? defaultValue = schema.TryGetProperty("default", out var defaultJson) ? defaultJson : null;
            if (schema.TryGetProperty("oneOf", out var oneOf))
            {
                ValidateKeywords(schema, path, "oneOf", "description", "default");
                if (oneOf.ValueKind != JsonValueKind.Array || oneOf.GetArrayLength() == 0)
                    throw new InvalidDataException($"Schema '{path}.oneOf' must contain at least one branch.");
                var branches = new List<ContractNode>();
                var branchIndex = 0;
                foreach (var branch in oneOf.EnumerateArray())
                    branches.Add(Compile(branch, $"{path}.oneOf[{branchIndex++}]", isRoot, depth + 1, ref nodeCount));
                return ValidateDefault(new UnionNode(branches.ToArray(), defaultValue), path);
            }

            var types = ReadTypes(schema, path);
            var allowsNull = types.Remove("null");
            if (types.Count != 1)
                throw new InvalidDataException($"Schema '{path}' must declare exactly one non-null type.");
            var type = types.Single();
            if (isRoot && type != "object")
                throw new InvalidDataException("A script input schema root must be an object.");
            var contract = type switch
            {
                "object" => CompileObject(schema, path, defaultValue, allowsNull, isRoot, depth, ref nodeCount),
                "array" => CompileArray(schema, path, defaultValue, allowsNull, depth, ref nodeCount),
                "string" => CompileString(schema, path, defaultValue, allowsNull),
                "boolean" => CompileBoolean(schema, path, defaultValue, allowsNull),
                "integer" => CompileNumber(schema, path, integer: true, defaultValue, allowsNull),
                "number" => CompileNumber(schema, path, integer: false, defaultValue, allowsNull),
                _ => throw new InvalidDataException($"Schema '{path}' uses unsupported type '{type}'.")
            };
            return ValidateDefault(contract, path);
        }

        private static ContractNode CompileArray(
            JsonElement schema,
            string path,
            JsonElement? defaultValue,
            bool allowsNull,
            int depth,
            ref int nodeCount)
        {
            ValidateKeywords(schema, path, "type", "items", "description", "default", "minItems", "maxItems");
            if (!schema.TryGetProperty("items", out var items))
                throw new InvalidDataException($"Schema '{path}' requires 'items'.");
            var minimum = ReadNonNegativeInteger(schema, "minItems", path);
            var maximum = ReadNonNegativeInteger(schema, "maxItems", path);
            if (minimum is { } min && maximum is { } max && min > max)
                throw new InvalidDataException($"Schema '{path}' has minItems greater than maxItems.");
            return new ArrayNode(
                Compile(items, path + ".items", false, depth + 1, ref nodeCount),
                defaultValue,
                allowsNull,
                minimum,
                maximum);
        }

        private static ContractNode ValidateDefault(ContractNode contract, string path)
        {
            if (contract.DefaultValue is not { } defaultValue)
                return contract;
            try
            {
                var buffer = new ArrayBufferWriter<byte>();
                using var writer = new Utf8JsonWriter(buffer);
                contract.WriteEffective(defaultValue, writer, path);
            }
            catch (HPDToolArgumentException exception)
            {
                throw new InvalidDataException($"Schema '{path}' has an invalid default value: {exception.Message}", exception);
            }
            return contract;
        }

        private static ContractNode CompileObject(
            JsonElement schema,
            string path,
            JsonElement? defaultValue,
            bool allowsNull,
            bool isRoot,
            int depth,
            ref int nodeCount)
        {
            ValidateKeywords(schema, path, "type", "properties", "required", "additionalProperties", "description", "default");
            if (!schema.TryGetProperty("additionalProperties", out var additional))
                throw new InvalidDataException($"Object schema '{path}' must declare 'additionalProperties'.");
            ContractNode? additionalContract = null;
            if (additional.ValueKind == JsonValueKind.Object && !isRoot)
                additionalContract = Compile(additional, path + ".additionalProperties", false, depth + 1, ref nodeCount);
            else if (additional.ValueKind != JsonValueKind.False)
                throw new InvalidDataException($"Object schema '{path}' must be closed{(isRoot ? "" : " or declare a typed additionalProperties contract")}.");
            var required = schema.TryGetProperty("required", out var requiredJson)
                ? requiredJson.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var properties = new List<PropertyNode>();
            if (schema.TryGetProperty("properties", out var propertySchemas))
            {
                if (propertySchemas.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"Schema '{path}.properties' must be an object.");
                if (propertySchemas.EnumerateObject().Count() > MaximumProperties)
                    throw new InvalidDataException($"Schema '{path}' cannot declare more than {MaximumProperties} properties.");
                foreach (var property in propertySchemas.EnumerateObject())
                    properties.Add(new(property.Name, Compile(property.Value, path + "." + property.Name, false, depth + 1, ref nodeCount), required.Contains(property.Name)));
            }
            if (required.Except(properties.Select(property => property.Name), StringComparer.Ordinal).Any())
                throw new InvalidDataException($"Schema '{path}' requires an undeclared property.");
            return new ObjectNode(properties.ToArray(), additionalContract, defaultValue, allowsNull);
        }

        private static ContractNode CompileString(JsonElement schema, string path, JsonElement? defaultValue, bool allowsNull)
        {
            ValidateKeywords(schema, path, "type", "enum", "const", "description", "default", "minLength", "maxLength", "format");
            if (schema.TryGetProperty("format", out var format) &&
                format.GetString() is not ("date-time" or "date" or "time" or "duration" or "uuid" or "uri"))
                throw new InvalidDataException($"Schema '{path}.format' is not an HPD-supported annotation.");
            var constant = ReadConst(schema);
            if (constant is { ValueKind: not JsonValueKind.String })
                throw new InvalidDataException($"Schema '{path}.const' supports string literals only.");
            var minimum = ReadNonNegativeInteger(schema, "minLength", path);
            var maximum = ReadNonNegativeInteger(schema, "maxLength", path);
            if (minimum is { } min && maximum is { } max && min > max)
                throw new InvalidDataException($"Schema '{path}' has minLength greater than maxLength.");
            return new ScalarNode(
                JsonValueKind.String,
                ReadStrings(schema, "enum", path),
                constant,
                defaultValue,
                allowsNull,
                minimum,
                maximum);
        }

        private static ContractNode CompileBoolean(JsonElement schema, string path, JsonElement? defaultValue, bool allowsNull)
        {
            ValidateKeywords(schema, path, "type", "description", "default");
            return new ScalarNode(JsonValueKind.True, null, null, defaultValue, allowsNull, null, null);
        }

        private static ContractNode CompileNumber(
            JsonElement schema,
            string path,
            bool integer,
            JsonElement? defaultValue,
            bool allowsNull)
        {
            ValidateKeywords(schema, path, "type", "description", "default", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum");
            var minimum = ReadDecimal(schema, "minimum", path);
            var maximum = ReadDecimal(schema, "maximum", path);
            var exclusiveMinimum = ReadDecimal(schema, "exclusiveMinimum", path);
            var exclusiveMaximum = ReadDecimal(schema, "exclusiveMaximum", path);
            if (minimum is { } min && maximum is { } max && min > max)
                throw new InvalidDataException($"Schema '{path}' has minimum greater than maximum.");
            return new NumberNode(
                integer,
                defaultValue,
                allowsNull,
                minimum,
                maximum,
                exclusiveMinimum,
                exclusiveMaximum);
        }

        private static HashSet<string> ReadTypes(JsonElement schema, string path)
        {
            if (!schema.TryGetProperty("type", out var type))
                throw new InvalidDataException($"Schema '{path}' requires 'type'.");
            return type.ValueKind switch
            {
                JsonValueKind.String => new(StringComparer.Ordinal) { type.GetString()! },
                JsonValueKind.Array => type.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal),
                _ => throw new InvalidDataException($"Schema '{path}.type' must be a string or string array.")
            };
        }

        private static HashSet<string>? ReadStrings(JsonElement schema, string name, string path)
        {
            if (!schema.TryGetProperty(name, out var values)) return null;
            if (values.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Schema '{path}.{name}' must be an array.");
            return values.EnumerateArray().Select(value =>
                value.ValueKind == JsonValueKind.String ? value.GetString()! :
                throw new InvalidDataException($"Schema '{path}.{name}' supports string values only."))
                .ToHashSet(StringComparer.Ordinal);
        }

        private static JsonElement? ReadConst(JsonElement schema) =>
            schema.TryGetProperty("const", out var value) ? value : null;

        private static int? ReadNonNegativeInteger(JsonElement schema, string name, string path)
        {
            if (!schema.TryGetProperty(name, out var value)) return null;
            if (!value.TryGetInt32(out var result) || result < 0)
                throw new InvalidDataException($"Schema '{path}.{name}' must be a non-negative 32-bit integer.");
            return result;
        }

        private static decimal? ReadDecimal(JsonElement schema, string name, string path)
        {
            if (!schema.TryGetProperty(name, out var value)) return null;
            if (!value.TryGetDecimal(out var result))
                throw new InvalidDataException($"Schema '{path}.{name}' must be a finite decimal number.");
            return result;
        }

        private static void ValidateKeywords(JsonElement schema, string path, params string[] allowed)
        {
            var names = allowed.ToHashSet(StringComparer.Ordinal);
            foreach (var property in schema.EnumerateObject())
                if (!names.Contains(property.Name))
                    throw new InvalidDataException($"Schema '{path}' uses unsupported keyword '{property.Name}'.");
        }

        protected static void WriteRaw(JsonElement value, Utf8JsonWriter writer) => value.WriteTo(writer);
        protected static void NullOrThrow(JsonElement value, string path, bool allowsNull, Utf8JsonWriter writer)
        {
            if (value.ValueKind != JsonValueKind.Null) return;
            if (!allowsNull) throw HPDGeneratedToolArgumentBinder.Error(path, "null_not_allowed", "Null is not allowed.");
            writer.WriteNullValue();
        }
    }

    private sealed class ObjectNode(
        PropertyNode[] properties,
        ContractNode? additionalContract,
        JsonElement? defaultValue,
        bool allowsNull) : ContractNode(defaultValue)
    {
        public override void WriteEffective(JsonElement value, Utf8JsonWriter writer, string path)
        {
            if (value.ValueKind == JsonValueKind.Null) { NullOrThrow(value, path, allowsNull, writer); return; }
            HPDGeneratedToolArgumentBinder.RequireObject(value, path);
            if (additionalContract is null)
                HPDGeneratedToolArgumentBinder.ValidateProperties(value, path, properties.Select(property => property.Name).ToArray());
            else
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                    if (!seen.Add(property.Name))
                        throw HPDGeneratedToolArgumentBinder.Error(
                            HPDGeneratedToolArgumentBinder.Append(path, property.Name),
                            "duplicate_property",
                            $"Property '{property.Name}' occurs more than once.");
            }
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                var found = HPDGeneratedToolArgumentBinder.TryGetOptionalProperty(value, property.Name, path, out var supplied);
                if (!found && property.Required)
                    throw HPDGeneratedToolArgumentBinder.Error(HPDGeneratedToolArgumentBinder.Append(path, property.Name), "missing_required_property", $"Required property '{property.Name}' is missing.");
                if (!found && property.Contract.DefaultValue is null) continue;
                writer.WritePropertyName(property.Name);
                property.Contract.WriteEffective(found ? supplied : property.Contract.DefaultValue!.Value, writer, HPDGeneratedToolArgumentBinder.Append(path, property.Name));
            }
            if (additionalContract is not null)
            {
                var declared = properties.Select(static property => property.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject().Where(property => !declared.Contains(property.Name)).OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    additionalContract.WriteEffective(property.Value, writer, HPDGeneratedToolArgumentBinder.Append(path, property.Name));
                }
            }
            writer.WriteEndObject();
        }
    }

    private sealed record PropertyNode(string Name, ContractNode Contract, bool Required);

    private sealed class ArrayNode(
        ContractNode item,
        JsonElement? defaultValue,
        bool allowsNull,
        int? minimumItems,
        int? maximumItems) : ContractNode(defaultValue)
    {
        public override void WriteEffective(JsonElement value, Utf8JsonWriter writer, string path)
        {
            if (value.ValueKind == JsonValueKind.Null) { NullOrThrow(value, path, allowsNull, writer); return; }
            HPDGeneratedToolArgumentBinder.RequireArray(value, path);
            var length = value.GetArrayLength();
            if (minimumItems is { } minimum && length < minimum)
                throw HPDGeneratedToolArgumentBinder.Error(path, "array_too_short", $"Expected at least {minimum} items.");
            if (maximumItems is { } maximum && length > maximum)
                throw HPDGeneratedToolArgumentBinder.Error(path, "array_too_long", $"Expected at most {maximum} items.");
            writer.WriteStartArray();
            var index = 0;
            foreach (var element in value.EnumerateArray())
                item.WriteEffective(element, writer, HPDGeneratedToolArgumentBinder.AppendIndex(path, index++));
            writer.WriteEndArray();
        }
    }

    private sealed class ScalarNode(
        JsonValueKind kind,
        HashSet<string>? allowed,
        JsonElement? constant,
        JsonElement? defaultValue,
        bool allowsNull,
        int? minimumLength,
        int? maximumLength) : ContractNode(defaultValue)
    {
        public override void WriteEffective(JsonElement value, Utf8JsonWriter writer, string path)
        {
            if (value.ValueKind == JsonValueKind.Null) { NullOrThrow(value, path, allowsNull, writer); return; }
            var validKind = kind == JsonValueKind.True
                ? value.ValueKind is JsonValueKind.True or JsonValueKind.False
                : value.ValueKind == kind;
            if (!validKind) throw HPDGeneratedToolArgumentBinder.Error(path, "invalid_json_kind", $"Expected {kind.ToString().ToLowerInvariant()}.");
            if (kind == JsonValueKind.String)
            {
                var length = value.GetString()!.Length;
                if (minimumLength is { } minimum && length < minimum)
                    throw HPDGeneratedToolArgumentBinder.Error(path, "string_too_short", $"Expected at least {minimum} characters.");
                if (maximumLength is { } maximum && length > maximum)
                    throw HPDGeneratedToolArgumentBinder.Error(path, "string_too_long", $"Expected at most {maximum} characters.");
            }
            if (allowed is not null && !allowed.Contains(value.GetString()!))
                throw HPDGeneratedToolArgumentBinder.Error(path, "invalid_enum_value", "Unsupported enum value.");
            if (constant is { } expected && value.GetRawText() != expected.GetRawText())
                throw HPDGeneratedToolArgumentBinder.Error(path, "invalid_const_value", "Value does not match the required constant.");
            WriteRaw(value, writer);
        }
    }

    private sealed class NumberNode(
        bool integer,
        JsonElement? defaultValue,
        bool allowsNull,
        decimal? minimum,
        decimal? maximum,
        decimal? exclusiveMinimum,
        decimal? exclusiveMaximum) : ContractNode(defaultValue)
    {
        public override void WriteEffective(JsonElement value, Utf8JsonWriter writer, string path)
        {
            if (value.ValueKind == JsonValueKind.Null) { NullOrThrow(value, path, allowsNull, writer); return; }
            if (value.ValueKind != JsonValueKind.Number || integer && !value.TryGetInt64(out _))
                throw HPDGeneratedToolArgumentBinder.Error(path, "invalid_json_kind", integer ? "Expected an integer." : "Expected a number.");
            if (!value.TryGetDecimal(out var number))
                throw HPDGeneratedToolArgumentBinder.Error(path, "number_out_of_range", "Number is outside the supported decimal range.");
            if (minimum is { } min && number < min || maximum is { } max && number > max ||
                exclusiveMinimum is { } exclusiveMin && number <= exclusiveMin ||
                exclusiveMaximum is { } exclusiveMax && number >= exclusiveMax)
                throw HPDGeneratedToolArgumentBinder.Error(path, "number_out_of_range", "Number is outside the declared range.");
            WriteRaw(value, writer);
        }
    }

    private sealed class UnionNode(ContractNode[] branches, JsonElement? defaultValue) : ContractNode(defaultValue)
    {
        public override void WriteEffective(JsonElement value, Utf8JsonWriter writer, string path)
        {
            byte[]? selected = null;
            var matches = 0;
            foreach (var branch in branches)
            {
                try
                {
                    var buffer = new ArrayBufferWriter<byte>();
                    using (var candidateWriter = new Utf8JsonWriter(buffer))
                        branch.WriteEffective(value, candidateWriter, path);
                    selected = buffer.WrittenSpan.ToArray();
                    matches++;
                }
                catch (HPDToolArgumentException) { }
            }
            if (selected is null)
                throw HPDGeneratedToolArgumentBinder.Error(path, "unknown_union_discriminator", "Input does not match any union branch.");
            if (matches != 1)
                throw HPDGeneratedToolArgumentBinder.Error(path, "ambiguous_union_branch", "Input matches more than one union branch.");
            writer.WriteRawValue(selected);
        }
    }
}
