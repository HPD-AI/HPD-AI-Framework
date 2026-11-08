using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;

namespace HPD.Agent;

/// <summary>Resource limits applied to runtime extension schemas and payloads.</summary>
public sealed record RuntimeAgentExtensionLimits
{
    public int MaxPayloadBytes { get; init; } = 65_536;
    public int MaxSchemaBytes { get; init; } = 262_144;
    public int MaxDepth { get; init; } = 16;
    public int MaxNodes { get; init; } = 2_048;
    public int MaxProperties { get; init; } = 128;
    public int MaxArrayItems { get; init; } = 512;
    public int MaxStringLength { get; init; } = 32_768;
}

/// <summary>Thread-safe registry and validation authority for runtime extension schemas.</summary>
public sealed class RuntimeAgentExtensionRegistry
{
    private readonly ConcurrentDictionary<string, RuntimeAgentExtensionDescriptor> _schemas =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RuntimeAgentExtensionSchema> _definitions =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IRuntimeAgentInputHandler> _inputHandlers =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, JsonSchema> _compiledSchemas =
        new(StringComparer.Ordinal);
    private readonly RuntimeAgentExtensionLimits _limits;
    private readonly IRuntimeAgentExtensionSchemaStore? _store;

    /// <summary>Gets an empty catalog with its deterministic digest.</summary>
    public static RuntimeAgentExtensionCatalog EmptyCatalog { get; } =
        new(Array.Empty<RuntimeAgentExtensionDescriptor>(),
            ComputeCatalogDigest(Array.Empty<RuntimeAgentExtensionDescriptor>()));

    public RuntimeAgentExtensionRegistry(
        RuntimeAgentExtensionLimits? limits = null,
        IRuntimeAgentExtensionSchemaStore? store = null)
    {
        _limits = limits ?? new RuntimeAgentExtensionLimits();
        _store = store;
        if (_limits.MaxPayloadBytes <= 0 ||
            _limits.MaxSchemaBytes <= 0 ||
            _limits.MaxDepth <= 0 ||
            _limits.MaxNodes <= 0 ||
            _limits.MaxProperties <= 0 ||
            _limits.MaxArrayItems <= 0 ||
            _limits.MaxStringLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Runtime extension limits must be positive.");
        }
    }

    /// <summary>Registers one immutable schema version.</summary>
    public RuntimeAgentExtensionDescriptor Register(RuntimeAgentExtensionSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ValidateIdentity(schema);
        var compiledSchema = RuntimeAgentPayloadValidator.Compile(schema.PayloadSchema, _limits);
        var digest = CanonicalJson.Digest(schema.PayloadSchema);
        var descriptor = new RuntimeAgentExtensionDescriptor(
            schema.Key,
            schema.Namespace,
            schema.Name,
            schema.Version,
            schema.Usage,
            digest,
            schema.PayloadSchema.Clone(),
            schema.Description);
        var added = _schemas.TryAdd(schema.Key, descriptor);
        if (!added)
        {
            var existing = _schemas[schema.Key];
            if (existing.Usage != descriptor.Usage ||
                !string.Equals(existing.PayloadSchemaDigest, descriptor.PayloadSchemaDigest, StringComparison.Ordinal) ||
                !string.Equals(existing.Description, descriptor.Description, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Runtime extension schema '{schema.Key}' is already registered with different metadata.");
            }
            return existing;
        }
        _definitions[schema.Key] = schema;
        _compiledSchemas[schema.Key] = compiledSchema;
        return descriptor;
    }

    /// <summary>Registers and durably stores one immutable schema version.</summary>
    public async ValueTask<RuntimeAgentExtensionDescriptor> RegisterPersistentAsync(
        RuntimeAgentExtensionSchema schema,
        IRuntimeAgentExtensionSchemaStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var descriptor = Register(schema);
        try
        {
            await store.SaveAsync(schema, cancellationToken).ConfigureAwait(false);
            return descriptor;
        }
        catch
        {
            _schemas.TryRemove(
                new KeyValuePair<string, RuntimeAgentExtensionDescriptor>(schema.Key, descriptor));
            _definitions.TryRemove(
                new KeyValuePair<string, RuntimeAgentExtensionSchema>(schema.Key, schema));
            _compiledSchemas.TryRemove(schema.Key, out _);
            throw;
        }
    }

    /// <summary>Registers a schema and persists it when the registry has a configured store.</summary>
    public ValueTask<RuntimeAgentExtensionDescriptor> RegisterManagedAsync(
        RuntimeAgentExtensionSchema schema,
        CancellationToken cancellationToken = default) =>
        _store is null
            ? ValueTask.FromResult(Register(schema))
            : RegisterPersistentAsync(schema, _store, cancellationToken);

    /// <summary>Loads and registers every schema from durable storage.</summary>
    public async ValueTask<int> HydrateAsync(
        IRuntimeAgentExtensionSchemaStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var schemas = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var schema in schemas)
            Register(schema);
        return schemas.Count;
    }

    /// <summary>Attaches the trusted handler for one input schema.</summary>
    public void RegisterInputHandler(string schemaKey, IRuntimeAgentInputHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaKey);
        ArgumentNullException.ThrowIfNull(handler);
        var descriptor = Require(schemaKey);
        if (!descriptor.Usage.HasFlag(RuntimeAgentExtensionUsage.Input))
            throw new InvalidOperationException($"Runtime schema '{schemaKey}' is not an input schema.");
        if (!_inputHandlers.TryAdd(schemaKey, handler) &&
            !ReferenceEquals(_inputHandlers[schemaKey], handler))
        {
            throw new InvalidOperationException($"Runtime input schema '{schemaKey}' already has a handler.");
        }
    }

    /// <summary>Validates one payload against its registered schema.</summary>
    public bool TryValidate(
        string schemaKey,
        JsonElement payload,
        out string? error)
    {
        if (!_definitions.ContainsKey(schemaKey))
            throw new KeyNotFoundException($"Runtime schema '{schemaKey}' is not registered.");
        if (!_compiledSchemas.TryGetValue(schemaKey, out var compiledSchema))
            throw new InvalidOperationException($"Runtime schema '{schemaKey}' has no compiled validator.");
        return RuntimeAgentPayloadValidator.TryValidate(
            compiledSchema,
            payload,
            _limits,
            out error);
    }

    /// <summary>Creates a validated live-only runtime event.</summary>
    public RuntimeAgentLiveEvent CreateLiveEvent(string schemaKey, JsonElement payload)
    {
        var descriptor = RequireUsage(schemaKey, RuntimeAgentExtensionUsage.LiveOutput);
        ValidatePayload(schemaKey, payload);
        return new RuntimeAgentLiveEvent
        {
            ExtensionNamespace = descriptor.Namespace,
            SchemaName = descriptor.Name,
            SchemaVersion = descriptor.Version,
            SchemaDigest = descriptor.PayloadSchemaDigest,
            Payload = payload.Clone()
        };
    }

    /// <summary>Creates a validated journal-admitted runtime event.</summary>
    public RuntimeAgentDurableEvent CreateDurableEvent(string schemaKey, JsonElement payload)
    {
        var descriptor = RequireUsage(schemaKey, RuntimeAgentExtensionUsage.DurableOutput);
        ValidatePayload(schemaKey, payload);
        return new RuntimeAgentDurableEvent
        {
            ExtensionNamespace = descriptor.Namespace,
            SchemaName = descriptor.Name,
            SchemaVersion = descriptor.Version,
            SchemaDigest = descriptor.PayloadSchemaDigest,
            Payload = payload.Clone()
        };
    }

    /// <summary>Creates a validated public runtime input for trusted handler dispatch.</summary>
    public RuntimeAgentInputEvent CreateInput(string schemaKey, JsonElement payload)
    {
        var descriptor = RequireUsage(schemaKey, RuntimeAgentExtensionUsage.Input);
        ValidatePayload(schemaKey, payload);
        return new RuntimeAgentInputEvent
        {
            ExtensionNamespace = descriptor.Namespace,
            SchemaName = descriptor.Name,
            SchemaVersion = descriptor.Version,
            SchemaDigest = descriptor.PayloadSchemaDigest,
            Payload = payload.Clone()
        };
    }

    /// <summary>Gets the trusted handler for a registered input envelope.</summary>
    public bool TryGetInputHandler(
        RuntimeAgentInputEvent input,
        out IRuntimeAgentInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(input);
        var key = $"{input.ExtensionNamespace}:{input.SchemaName}@{input.SchemaVersion}";
        if (!TryGetDefinition(key, out var schema))
        {
            handler = null!;
            return false;
        }
        var digest = CanonicalJson.Digest(schema.PayloadSchema);
        if (!string.Equals(digest, input.SchemaDigest, StringComparison.Ordinal))
        {
            handler = null!;
            return false;
        }
        return _inputHandlers.TryGetValue(key, out handler!);
    }

    /// <summary>Gets a deterministic snapshot of all registered schemas.</summary>
    public RuntimeAgentExtensionCatalog GetCatalog()
    {
        var schemas = _schemas.Values
            .OrderBy(static value => value.Key, StringComparer.Ordinal)
            .ToArray();
        return new RuntimeAgentExtensionCatalog(
            Array.AsReadOnly(schemas),
            ComputeCatalogDigest(schemas));
    }

    private RuntimeAgentExtensionDescriptor Require(string schemaKey) =>
        _schemas.TryGetValue(schemaKey, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Runtime schema '{schemaKey}' is not registered.");

    private RuntimeAgentExtensionDescriptor RequireUsage(
        string schemaKey,
        RuntimeAgentExtensionUsage usage)
    {
        var descriptor = Require(schemaKey);
        if (!descriptor.Usage.HasFlag(usage))
            throw new InvalidOperationException($"Runtime schema '{schemaKey}' does not allow {usage}.");
        return descriptor;
    }

    private bool TryGetDefinition(
        string schemaKey,
        out RuntimeAgentExtensionSchema schema) =>
        _definitions.TryGetValue(schemaKey, out schema!);

    private void ValidatePayload(string schemaKey, JsonElement payload)
    {
        if (!TryValidate(schemaKey, payload, out var error))
            throw new InvalidOperationException(error);
    }

    private static void ValidateIdentity(RuntimeAgentExtensionSchema schema)
    {
        if (!IsIdentifier(schema.Namespace) ||
            schema.Namespace.StartsWith("hpd.agent", StringComparison.OrdinalIgnoreCase) ||
            schema.Namespace.StartsWith("hpd.core", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Runtime extension namespaces must be lowercase identifiers outside reserved hpd namespaces.",
                nameof(schema));
        }
        if (!IsIdentifier(schema.Name))
            throw new ArgumentException("Runtime extension names must be lowercase identifiers.", nameof(schema));
        if (schema.Version <= 0)
            throw new ArgumentOutOfRangeException(nameof(schema), "Runtime extension versions must be positive.");
        if (schema.Usage == 0 || (schema.Usage & ~(RuntimeAgentExtensionUsage.LiveOutput |
                                                  RuntimeAgentExtensionUsage.DurableOutput |
                                                  RuntimeAgentExtensionUsage.Input)) != 0)
        {
            throw new ArgumentException("Runtime extension usage flags are invalid.", nameof(schema));
        }
    }

    private static bool IsIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        char.IsAsciiLetter(value[0]) &&
        value.All(static character => !char.IsLetter(character) || char.IsLower(character)) &&
        value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static string ComputeCatalogDigest(
        IReadOnlyList<RuntimeAgentExtensionDescriptor> schemas)
    {
        var canonical = new StringBuilder("runtime-agent-extensions-v1");
        foreach (var schema in schemas)
        {
            canonical.Append('\n').Append(schema.Key).Append('|')
                .Append((int)schema.Usage).Append('|').Append(schema.PayloadSchemaDigest);
        }
        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}

internal static class CanonicalJson
{
    public static string Digest(JsonElement value)
    {
        var canonical = Canonicalize(value);
        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string Canonicalize(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(static item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                value.WriteTo(writer);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("Runtime extension JSON contains an unsupported value.");
        }
    }
}
