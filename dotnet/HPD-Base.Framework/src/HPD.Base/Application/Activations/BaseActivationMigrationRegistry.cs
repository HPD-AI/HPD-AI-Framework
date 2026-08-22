using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>Maps one target input property from one source property or one canonical constant.</summary>
public sealed record BaseActivationMigrationProperty
{
    /// <summary>Gets the target stable-property path.</summary>
    public required ImmutableArray<string> TargetPropertyPath { get; init; }
    /// <summary>Gets the optional source stable-property path.</summary>
    public ImmutableArray<string> SourcePropertyPath { get; init; }
    /// <summary>Gets optional canonical Base JSON used when no source path is selected.</summary>
    public ImmutableArray<byte> CanonicalConstant { get; init; }
}

/// <summary>Defines one graph-owned, callback-free activation input migration.</summary>
public sealed record BaseActivationMigrationDefinition
{
    /// <summary>Gets the stable migration identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive migration version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the exact source definition.</summary>
    public required BaseActivationDefinitionKey Source { get; init; }
    /// <summary>Gets the exact target definition.</summary>
    public required BaseActivationDefinitionKey Target { get; init; }
    /// <summary>Gets the exact migration grant identity.</summary>
    public required string GrantId { get; init; }
    /// <summary>Gets the complete closed property projection.</summary>
    public required ImmutableArray<BaseActivationMigrationProperty> Properties { get; init; }
    /// <summary>Gets the Runtime-owned checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one graph-owned migration plus source-generated codec authority.</summary>
public sealed record BaseActivationMigrationRegistration<TSource, TTarget>
{
    /// <summary>Gets the migration definition.</summary>
    public required BaseActivationMigrationDefinition Definition { get; init; }
    /// <summary>Gets source input metadata.</summary>
    public required JsonTypeInfo<TSource> SourceTypeInfo { get; init; }
    /// <summary>Gets target input metadata.</summary>
    public required JsonTypeInfo<TTarget> TargetTypeInfo { get; init; }
    /// <summary>Gets graph-owned source property bindings.</summary>
    public required IReadOnlyList<BaseModuleDtoPropertyBinding> SourceBindings { get; init; }
    /// <summary>Gets graph-owned target property bindings.</summary>
    public required IReadOnlyList<BaseModuleDtoPropertyBinding> TargetBindings { get; init; }
}

internal interface IBaseActivationMigrationRegistration
{
    BaseActivationMigrationDefinition Definition { get; }
    ImmutableArray<byte> Project(ReadOnlySpan<byte> source);
}

internal sealed class BaseInstalledActivationMigration<TSource, TTarget> : IBaseActivationMigrationRegistration
{
    private readonly BaseActivationMigrationRegistration<TSource, TTarget> _registration;
    private readonly IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> _source;
    private readonly IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> _target;
    public BaseActivationMigrationDefinition Definition { get; }

    internal BaseInstalledActivationMigration(BaseActivationMigrationRegistration<TSource, TTarget> registration)
    {
        _registration = registration;
        _source = registration.SourceBindings.ToDictionary(static binding => binding.PathKey, StringComparer.Ordinal);
        _target = registration.TargetBindings.ToDictionary(static binding => binding.PathKey, StringComparer.Ordinal);
        Definition = BaseActivationMigrationContract.Seal(registration.Definition, _source, _target);
    }

    public ImmutableArray<byte> Project(ReadOnlySpan<byte> source)
    {
        TSource? typedSource = JsonSerializer.Deserialize(source, _registration.SourceTypeInfo);
        if (typedSource is null) throw new JsonException("base.activation.migrationInvalid");
        JsonElement root = JsonSerializer.SerializeToElement(typedSource, _registration.SourceTypeInfo);
        var target = new JsonObject();
        foreach (BaseActivationMigrationProperty property in Definition.Properties)
        {
            JsonNode? value = property.SourcePropertyPath.IsDefaultOrEmpty
                ? JsonNode.Parse(property.CanonicalConstant.AsSpan())
                : JsonNode.Parse(Read(root, property.SourcePropertyPath).GetRawText());
            Write(target, property.TargetPropertyPath, value);
        }
        TTarget? typedTarget = JsonSerializer.Deserialize(target.ToJsonString(), _registration.TargetTypeInfo);
        if (typedTarget is null) throw new JsonException("base.activation.migrationInvalid");
        return JsonSerializer.SerializeToUtf8Bytes(typedTarget, _registration.TargetTypeInfo).ToImmutableArray();
    }

    private JsonElement Read(JsonElement current, ImmutableArray<string> path)
    {
        for (int index = 0; index < path.Length; index++)
        {
            string key = string.Join('\0', path.Take(index + 1));
            if (!_source.TryGetValue(key, out BaseModuleDtoPropertyBinding? binding)
                || !current.TryGetProperty(binding.ApplicationName, out current))
                throw new JsonException("base.activation.migrationInvalid");
        }
        return current;
    }

    private void Write(JsonObject root, ImmutableArray<string> path, JsonNode? value)
    {
        JsonObject current = root;
        for (int index = 0; index < path.Length; index++)
        {
            string key = string.Join('\0', path.Take(index + 1));
            if (!_target.TryGetValue(key, out BaseModuleDtoPropertyBinding? binding))
                throw new JsonException("base.activation.migrationInvalid");
            if (index == path.Length - 1) current[binding.ApplicationName] = value;
            else
            {
                if (current[binding.ApplicationName] is not JsonObject child)
                { child = new JsonObject(); current[binding.ApplicationName] = child; }
                current = child;
            }
        }
    }
}

/// <summary>Computes and freezes callback-free activation migration authority.</summary>
public static class BaseActivationMigrationContract
{
    /// <summary>Returns a deeply owned migration carrying its sole canonical checksum.</summary>
    public static BaseActivationMigrationDefinition Create(BaseActivationMigrationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Seal(definition, null, null);
    }

    internal static BaseActivationMigrationDefinition Seal(
        BaseActivationMigrationDefinition definition,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding>? source,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding>? target)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Version < 1
            || string.IsNullOrWhiteSpace(definition.OwningModuleId) || string.IsNullOrWhiteSpace(definition.GrantId)
            || definition.Source.Version < 1 || definition.Target.Version < 1
            || definition.Source.Checksum.Length != 32 || definition.Target.Checksum.Length != 32
            || definition.Properties.IsDefaultOrEmpty || definition.Properties.Length > 256)
            throw new InvalidOperationException("base.activation.migrationInvalid");
        BaseActivationMigrationProperty[] properties = definition.Properties
            .OrderBy(static item => string.Join('\0', item.TargetPropertyPath), StringComparer.Ordinal).ToArray();
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseActivationMigrationProperty property in properties)
        {
            string targetKey = string.Join('\0', property.TargetPropertyPath);
            bool hasSource = !property.SourcePropertyPath.IsDefaultOrEmpty;
            bool hasConstant = !property.CanonicalConstant.IsDefaultOrEmpty;
            if (property.TargetPropertyPath.IsDefaultOrEmpty || property.TargetPropertyPath.Length > 16
                || hasSource == hasConstant || !targets.Add(targetKey)
                || source is not null && hasSource && !source.ContainsKey(string.Join('\0', property.SourcePropertyPath))
                || target is not null && !target.ContainsKey(targetKey))
                throw new InvalidOperationException("base.activation.migrationInvalid");
            if (hasConstant) { try { using JsonDocument _ = JsonDocument.Parse(property.CanonicalConstant.ToArray()); } catch (JsonException) { throw new InvalidOperationException("base.activation.migrationInvalid"); } }
        }
        if (target is not null)
        {
            string[] leaves = target.Keys.Where(key => !target.Keys.Any(other => other.Length > key.Length && other.StartsWith(key + '\0', StringComparison.Ordinal))).ToArray();
            if (leaves.Length != targets.Count || leaves.Any(key => !targets.Contains(key)))
                throw new InvalidOperationException("base.activation.migrationInvalid");
        }
        BaseActivationMigrationDefinition owned = definition with
        {
            Properties = properties.Select(static property => property with
            {
                TargetPropertyPath = property.TargetPropertyPath.Select(static edge => new string(edge.AsSpan())).ToImmutableArray(),
                SourcePropertyPath = property.SourcePropertyPath.IsDefault ? [] : property.SourcePropertyPath.Select(static edge => new string(edge.AsSpan())).ToImmutableArray(),
                CanonicalConstant = property.CanonicalConstant.IsDefault ? [] : property.CanonicalConstant.ToArray().ToImmutableArray(),
            }).ToImmutableArray(),
        };
        byte[] checksum = Checksum(owned);
        if (!definition.Checksum.IsDefaultOrEmpty && (definition.Checksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(definition.Checksum.AsSpan(), checksum)))
            throw new InvalidOperationException("base.activation.migrationInvalid");
        return owned with { Checksum = checksum.ToImmutableArray() };
    }

    private static byte[] Checksum(BaseActivationMigrationDefinition definition)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append("base.activation.migration.v1"); Append(definition.Id); Append(definition.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(definition.OwningModuleId); Append(definition.GrantId); Definition(definition.Source); Definition(definition.Target);
        foreach (BaseActivationMigrationProperty property in definition.Properties)
        {
            Append(string.Join('\0', property.TargetPropertyPath));
            Append(property.SourcePropertyPath.IsDefaultOrEmpty ? string.Empty : string.Join('\0', property.SourcePropertyPath));
            hash.AppendData(property.CanonicalConstant.IsDefault ? [] : property.CanonicalConstant.AsSpan());
        }
        return hash.GetHashAndReset();
        void Definition(BaseActivationDefinitionKey value) { Append(value.Id); Append(value.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)); hash.AppendData(value.Checksum.AsSpan()); }
        void Append(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); hash.AppendData(BitConverter.GetBytes(bytes.Length).Reverse().ToArray()); hash.AppendData(bytes); }
    }
}

/// <summary>Provides immutable lookup over installed activation migrations.</summary>
public sealed class BaseActivationMigrationRegistry
{
    private readonly Dictionary<(string Id, int Version), IBaseActivationMigrationRegistration> _items;
    internal BaseActivationMigrationRegistry(IEnumerable<IBaseActivationMigrationRegistration> items) =>
        _items = items.ToDictionary(static item => (item.Definition.Id, item.Definition.Version));
    internal IBaseActivationMigrationRegistration? Find(string id, int version) => _items.GetValueOrDefault((id, version));
}
