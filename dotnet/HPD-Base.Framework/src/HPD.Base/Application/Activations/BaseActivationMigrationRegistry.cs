using System.Collections.Immutable;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

/// <summary>Declares graph-owned activation migration identity and authorization.</summary>
public sealed record BaseActivationMigrationDraft
{
    /// <summary>Gets the stable migration identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive migration version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the exact migration grant identity.</summary>
    public required string GrantId { get; init; }
}

/// <summary>Contains one graph-owned migration plus source-generated codec authority.</summary>
/// <typeparam name="TSource">The source activation input type.</typeparam>
/// <typeparam name="TTarget">The target activation input type.</typeparam>
public sealed class BaseActivationMigrationRegistration<TSource, TTarget>
{
    internal BaseActivationMigrationRegistration(
        BaseActivationMigrationDefinition definition,
        IBaseActivationInputDtoAuthority<TSource> sourceAuthority,
        IBaseActivationInputDtoAuthority<TTarget> targetAuthority,
        long maximumSourceBytes,
        long maximumTargetBytes)
    {
        Definition = definition;
        SourceAuthority = sourceAuthority;
        TargetAuthority = targetAuthority;
        MaximumSourceBytes = maximumSourceBytes;
        MaximumTargetBytes = maximumTargetBytes;
    }
    /// <summary>Gets the migration definition.</summary>
    public BaseActivationMigrationDefinition Definition { get; }
    internal IBaseActivationInputDtoAuthority<TSource> SourceAuthority { get; }
    internal IBaseActivationInputDtoAuthority<TTarget> TargetAuthority { get; }
    internal long MaximumSourceBytes { get; }
    internal long MaximumTargetBytes { get; }
}

/// <summary>Starts callback-free, typed activation migration construction.</summary>
public static class BaseActivationMigrationBuilder
{
    /// <summary>Selects the exact generated source activation and DTO authority.</summary>
    /// <typeparam name="TSource">The source activation input type.</typeparam>
    /// <typeparam name="TResult">The source activation result type.</typeparam>
    /// <param name="registration">The installed source worker registration.</param>
    /// <param name="authority">The generated DTO authority used by that registration.</param>
    /// <returns>A builder bound to the source activation.</returns>
    public static BaseActivationMigrationSourceBuilder<TSource> From<TSource, TResult>(
        BaseActivationHandlerRegistration<TSource, TResult> registration,
        BaseGeneratedActivationDtoAuthority<TSource, TResult> authority) =>
        Source(registration.Definition, registration.Identity, authority);

    /// <summary>Selects the exact generated source transactional activation and DTO authority.</summary>
    /// <typeparam name="TSource">The source activation input type.</typeparam>
    /// <typeparam name="TResult">The source activation result type.</typeparam>
    /// <param name="registration">The installed source transactional registration.</param>
    /// <param name="authority">The generated DTO authority used by that registration.</param>
    /// <returns>A builder bound to the source activation.</returns>
    public static BaseActivationMigrationSourceBuilder<TSource> From<TSource, TResult>(
        BaseTransactionalActivationRegistration<TSource, TResult> registration,
        BaseGeneratedActivationDtoAuthority<TSource, TResult> authority) =>
        Source(registration.Definition, registration.Identity, authority);

    private static BaseActivationMigrationSourceBuilder<TSource> Source<TSource, TResult>(
        BaseActivationDefinition definition,
        BaseActivationRegistrationIdentity<TSource, TResult> identity,
        BaseGeneratedActivationDtoAuthority<TSource, TResult> authority)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(authority);
        if (!identity.UsesAuthority(authority)) throw new InvalidOperationException("base.activation.migrationInvalid");
        return new(definition, authority);
    }
}

/// <summary>Continues typed migration construction with one exact target activation.</summary>
/// <typeparam name="TSource">The source activation input type.</typeparam>
public sealed class BaseActivationMigrationSourceBuilder<TSource>
{
    private readonly BaseActivationDefinition _source;
    private readonly IBaseActivationInputDtoAuthority<TSource> _sourceAuthority;

    internal BaseActivationMigrationSourceBuilder(BaseActivationDefinition source, IBaseActivationInputDtoAuthority<TSource> sourceAuthority)
    { _source = source; _sourceAuthority = sourceAuthority; }

    /// <summary>Selects the exact generated target worker activation and DTO authority.</summary>
    /// <typeparam name="TTarget">The target activation input type.</typeparam>
    /// <typeparam name="TResult">The target activation result type.</typeparam>
    /// <param name="registration">The installed target worker registration.</param>
    /// <param name="authority">The generated DTO authority used by that registration.</param>
    /// <returns>A builder bound to both activation definitions.</returns>
    public BaseActivationMigrationProjectionBuilder<TSource, TTarget> To<TTarget, TResult>(
        BaseActivationHandlerRegistration<TTarget, TResult> registration,
        BaseGeneratedActivationDtoAuthority<TTarget, TResult> authority) => Target(registration.Definition, registration.Identity, authority);

    /// <summary>Selects the exact generated target transactional activation and DTO authority.</summary>
    /// <typeparam name="TTarget">The target activation input type.</typeparam>
    /// <typeparam name="TResult">The target activation result type.</typeparam>
    /// <param name="registration">The installed target transactional registration.</param>
    /// <param name="authority">The generated DTO authority used by that registration.</param>
    /// <returns>A builder bound to both activation definitions.</returns>
    public BaseActivationMigrationProjectionBuilder<TSource, TTarget> To<TTarget, TResult>(
        BaseTransactionalActivationRegistration<TTarget, TResult> registration,
        BaseGeneratedActivationDtoAuthority<TTarget, TResult> authority) => Target(registration.Definition, registration.Identity, authority);

    private BaseActivationMigrationProjectionBuilder<TSource, TTarget> Target<TTarget, TResult>(BaseActivationDefinition definition,
        BaseActivationRegistrationIdentity<TTarget, TResult> identity, BaseGeneratedActivationDtoAuthority<TTarget, TResult> authority)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(authority);
        if (!identity.UsesAuthority(authority)) throw new InvalidOperationException("base.activation.migrationInvalid");
        return new(_source, _sourceAuthority, definition, authority);
    }
}

/// <summary>Builds one complete closed activation input projection.</summary>
/// <typeparam name="TSource">The source activation input type.</typeparam>
/// <typeparam name="TTarget">The target activation input type.</typeparam>
public sealed class BaseActivationMigrationProjectionBuilder<TSource, TTarget>
{
    private readonly BaseActivationDefinition _source;
    private readonly IBaseActivationInputDtoAuthority<TSource> _sourceAuthority;
    private readonly BaseActivationDefinition _target;
    private readonly IBaseActivationInputDtoAuthority<TTarget> _targetAuthority;
    private readonly List<BaseActivationMigrationProperty> _properties = [];

    internal BaseActivationMigrationProjectionBuilder(BaseActivationDefinition source,
        IBaseActivationInputDtoAuthority<TSource> sourceAuthority, BaseActivationDefinition target,
        IBaseActivationInputDtoAuthority<TTarget> targetAuthority)
    { _source = source; _sourceAuthority = sourceAuthority; _target = target; _targetAuthority = targetAuthority; }

    /// <summary>Maps one target leaf from one byte-compatible source leaf.</summary>
    /// <typeparam name="TValue">The generated leaf value type.</typeparam>
    /// <param name="target">The target authority-owned property handle.</param>
    /// <param name="source">The source authority-owned property handle.</param>
    /// <returns>This projection builder.</returns>
    public BaseActivationMigrationProjectionBuilder<TSource, TTarget> Map<TValue>(
        BaseActivationInputProperty<TTarget, TValue> target,
        BaseActivationInputProperty<TSource, TValue> source)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(source);
        if (_properties.Count >= 128
            || !TryResolve(_targetAuthority, target, out BaseModuleDtoPropertyBinding? resolvedTarget)
            || !TryResolve(_sourceAuthority, source, out BaseModuleDtoPropertyBinding? resolvedSource)
            || !BaseModuleValueAuthorityContract.StructurallyEquals(resolvedTarget!.ScalarAuthority.ValueType, resolvedSource!.ScalarAuthority.ValueType))
            throw new InvalidOperationException("base.activation.migrationInvalid");
        _properties.Add(new() { TargetPropertyPath = resolvedTarget!.ScalarAuthority.StablePropertyPath,
            SourcePropertyPath = resolvedSource!.ScalarAuthority.StablePropertyPath });
        return this;
    }

    /// <summary>Maps one target leaf from one typed canonical constant.</summary>
    /// <typeparam name="TValue">The generated leaf value type.</typeparam>
    /// <param name="target">The target authority-owned property handle.</param>
    /// <param name="value">The value to encode as a canonical constant.</param>
    /// <returns>This projection builder.</returns>
    public BaseActivationMigrationProjectionBuilder<TSource, TTarget> Constant<TValue>(
        BaseActivationInputProperty<TTarget, TValue> target, TValue value)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_properties.Count >= 128
            || !TryResolve(_targetAuthority, target, out BaseModuleDtoPropertyBinding? resolvedTarget))
            throw new InvalidOperationException("base.activation.migrationInvalid");
        _properties.Add(new() { TargetPropertyPath = resolvedTarget!.ScalarAuthority.StablePropertyPath,
            CanonicalConstant = BaseModuleConstantEncoder.Encode(resolvedTarget.ScalarAuthority.ValueType, value).ToImmutableArray() });
        return this;
    }

    /// <summary>Seals the complete generated migration registration.</summary>
    /// <param name="draft">The graph-owned migration identity and grant.</param>
    /// <returns>An opaque generated migration registration.</returns>
    public BaseActivationMigrationRegistration<TSource, TTarget> Create(BaseActivationMigrationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.OwningModuleId != _source.OwningModuleId || draft.OwningModuleId != _target.OwningModuleId
            || _properties.Count is < 1 or > 128)
            throw new InvalidOperationException("base.activation.migrationInvalid");
        var definition = new BaseActivationMigrationDefinition
        {
            Id = draft.Id, Version = draft.Version, OwningModuleId = draft.OwningModuleId, GrantId = draft.GrantId,
            Source = new() { Id = _source.Id, Version = _source.Version, Checksum = _source.Checksum },
            Target = new() { Id = _target.Id, Version = _target.Version, Checksum = _target.Checksum },
            Properties = _properties.ToImmutableArray(), Checksum = [],
        };
        return new(definition, _sourceAuthority, _targetAuthority,
            _source.Limits.MaximumInputBytes, _target.Limits.MaximumInputBytes);
    }

    private static bool TryResolve<TInput, TValue>(IBaseActivationInputDtoAuthority<TInput> authority,
        BaseActivationInputProperty<TInput, TValue> handle, out BaseModuleDtoPropertyBinding? resolved)
    {
        resolved = null;
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                authority.CurrentInputChecksum.Span, handle.OwnerChecksum.Span)
            || !authority.CurrentInputBindings.TryGetValue(string.Join('\0', handle.Authority.StablePropertyPath), out resolved)) return false;
        return resolved.ScalarAuthority.AuthorityChecksum == handle.Authority.AuthorityChecksum
            && BaseModuleValueAuthorityContract.StructurallyEquals(resolved.ScalarAuthority.ValueType, handle.Authority.ValueType);
    }
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
        _source = registration.SourceAuthority.CurrentInputBindings;
        _target = registration.TargetAuthority.CurrentInputBindings;
        Definition = BaseActivationMigrationContract.Seal(registration.Definition, _source, _target,
            registration.SourceAuthority.CurrentInputChecksum.ToArray().ToImmutableArray(),
            registration.TargetAuthority.CurrentInputChecksum.ToArray().ToImmutableArray());
    }

    public ImmutableArray<byte> Project(ReadOnlySpan<byte> source)
    {
        if (source.Length < 2 || source.Length > _registration.MaximumSourceBytes || source.Length > 4 * 1024 * 1024)
            throw new BaseActivationDtoContractException("base.activation.providerContractInvalid");
        long transientBytes = 0;
        ChargeTransient(ref transientBytes, checked(source.Length * 4L), providerInfluenced: true);
        ValidateBoundedJson(source, providerInfluenced: true);
        try
        {
            _registration.SourceAuthority.ValidateCanonicalMigrationInput(source, providerInfluenced: true);
        }
        catch (BaseActivationDtoContractException exception)
        { throw new BaseActivationDtoContractException("base.activation.providerContractInvalid", exception); }
        using JsonDocument sourceDocument = JsonDocument.Parse(source.ToArray());
        JsonElement root = sourceDocument.RootElement;
        Dictionary<string, BaseActivationMigrationProperty> projections = Definition.Properties
            .ToDictionary(static property => string.Join('\0', property.TargetPropertyPath), StringComparer.Ordinal);
        ChargeTransient(ref transientBytes, 4096, providerInfluenced: false);
        long remaining = 16L * 1024 * 1024 - transientBytes;
        int maximumTargetBytes = checked((int)Math.Min(
            Math.Min(_registration.MaximumTargetBytes, 4L * 1024 * 1024), remaining / 4));
        if (maximumTargetBytes < 2) throw new JsonException("base.activation.migrationInvalid");
        var target = new BaseBoundedByteBuffer(maximumTargetBytes);
        try
        {
            using var writer = new Utf8JsonWriter(target);
            writer.WriteStartObject();
            foreach (JsonPropertyInfo propertyInfo in _registration.TargetAuthority.CurrentInputTypeInfo.Properties)
            {
                BaseModuleDtoPropertyBinding binding = _target.Values.SingleOrDefault(
                    candidate => candidate.WirePropertyPath.Count == 1
                        && string.Equals(candidate.WirePropertyPath[0], propertyInfo.Name, StringComparison.Ordinal))
                    ?? throw new JsonException("base.activation.migrationInvalid");
                if (binding.WirePropertyPath.Count != 1
                    || !projections.TryGetValue(binding.PathKey, out BaseActivationMigrationProperty? property))
                    throw new JsonException("base.activation.migrationInvalid");
                if (property.SourcePropertyPath.IsDefaultOrEmpty)
                {
                    writer.WritePropertyName(binding.WirePropertyPath[0]);
                    writer.WriteRawValue(property.CanonicalConstant.AsSpan(), skipInputValidation: false);
                    continue;
                }
                (bool present, JsonElement sourceValue) = Read(root, property.SourcePropertyPath);
                if (!present) continue;
                writer.WritePropertyName(binding.WirePropertyPath[0]);
                sourceValue.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.Flush();
        }
        catch (BaseBoundedByteBufferException exception)
        { throw new JsonException("base.activation.migrationInvalid", exception); }
        ReadOnlySpan<byte> candidate = target.WrittenSpan;
        ChargeTransient(ref transientBytes, checked(candidate.Length * 4L), providerInfluenced: false);
        ValidateBoundedJson(candidate, providerInfluenced: false);
        try { _registration.TargetAuthority.ValidateCanonicalMigrationInput(candidate, providerInfluenced: false); }
        catch (BaseActivationDtoContractException exception) { throw new JsonException("base.activation.migrationInvalid", exception); }
        return candidate.ToImmutableArray();
    }

    internal static void ChargeTransient(ref long total, long bytes, bool providerInfluenced)
    {
        try { total = checked(total + bytes); }
        catch (OverflowException exception)
        {
            if (providerInfluenced) throw new BaseActivationDtoContractException("base.activation.providerContractInvalid", exception);
            throw new JsonException("base.activation.migrationInvalid", exception);
        }
        if (total <= 16L * 1024 * 1024) return;
        if (providerInfluenced) throw new BaseActivationDtoContractException("base.activation.providerContractInvalid");
        throw new JsonException("base.activation.migrationInvalid");
    }

    private static void ValidateBoundedJson(ReadOnlySpan<byte> bytes, bool providerInfluenced)
    {
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 8, CommentHandling = JsonCommentHandling.Disallow });
            int nodes = 0, properties = 0;
            while (reader.Read())
            {
                bool oversizedText = reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName
                    && (reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length) > 1024 * 1024;
                if (++nodes > 256 || reader.TokenType == JsonTokenType.PropertyName && ++properties > 128 || oversizedText)
                    throw new JsonException();
            }
        }
        catch (JsonException exception)
        {
            if (providerInfluenced) throw new BaseActivationDtoContractException("base.activation.providerContractInvalid", exception);
            throw new JsonException("base.activation.migrationInvalid", exception);
        }
    }

    private (bool Present, JsonElement Value) Read(JsonElement current, ImmutableArray<string> path)
    {
        if (!_source.TryGetValue(string.Join('\0', path), out BaseModuleDtoPropertyBinding? binding))
            throw new JsonException("base.activation.migrationInvalid");
        foreach (string wireName in binding.WirePropertyPath)
        {
            if (current.ValueKind != JsonValueKind.Object)
                throw new BaseActivationDtoContractException("base.activation.providerContractInvalid");
            if (!current.TryGetProperty(wireName, out current)) return (false, default);
        }
        return (true, current);
    }

}

internal sealed class BaseBoundedByteBuffer : IBufferWriter<byte>
{
    private readonly int _maximumBytes;
    private byte[] _buffer;
    private int _written;

    internal BaseBoundedByteBuffer(int maximumBytes)
    {
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _maximumBytes = maximumBytes;
        _buffer = new byte[Math.Min(maximumBytes, 256)];
    }

    internal ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Advance(int count)
    {
        if (count < 0 || count > _buffer.Length - _written || _written + count > _maximumBytes)
            throw new BaseBoundedByteBufferException();
        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_written);
    }

    private void Ensure(int sizeHint)
    {
        if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
        int required = checked(_written + Math.Max(sizeHint, 1));
        int capacityCeiling = checked(_maximumBytes + 4096);
        if (required > capacityCeiling) throw new BaseBoundedByteBufferException();
        if (required <= _buffer.Length) return;
        int length = Math.Min(capacityCeiling, Math.Max(required, checked(_buffer.Length * 2)));
        Array.Resize(ref _buffer, length);
    }
}

internal sealed class BaseBoundedByteBufferException : Exception;

/// <summary>Computes and freezes callback-free activation migration authority.</summary>
public static class BaseActivationMigrationContract
{
    /// <summary>Returns a deeply owned migration carrying its sole canonical checksum.</summary>
    internal static BaseActivationMigrationDefinition Create(BaseActivationMigrationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Seal(definition, null, null, [], []);
    }

    internal static BaseActivationMigrationDefinition Seal(
        BaseActivationMigrationDefinition definition,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding>? source,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding>? target,
        ImmutableArray<byte> sourceDtoChecksum,
        ImmutableArray<byte> targetDtoChecksum)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Version < 1
            || string.IsNullOrWhiteSpace(definition.OwningModuleId) || string.IsNullOrWhiteSpace(definition.GrantId)
            || definition.Source.Version < 1 || definition.Target.Version < 1
            || definition.Source.Checksum.Length != 32 || definition.Target.Checksum.Length != 32
            || source is not null && (sourceDtoChecksum.Length != 32 || targetDtoChecksum.Length != 32)
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
        byte[] checksum = Checksum(owned, sourceDtoChecksum, targetDtoChecksum);
        if (!definition.Checksum.IsDefaultOrEmpty && (definition.Checksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(definition.Checksum.AsSpan(), checksum)))
            throw new InvalidOperationException("base.activation.migrationInvalid");
        return owned with { Checksum = checksum.ToImmutableArray() };
    }

    private static byte[] Checksum(BaseActivationMigrationDefinition definition,
        ImmutableArray<byte> sourceDtoChecksum, ImmutableArray<byte> targetDtoChecksum)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append("base.activation.migration.v2"); Append(definition.Id); Append(definition.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(definition.OwningModuleId); Append(definition.GrantId); Definition(definition.Source); Definition(definition.Target);
        hash.AppendData(sourceDtoChecksum.IsDefault ? [] : sourceDtoChecksum.AsSpan());
        hash.AppendData(targetDtoChecksum.IsDefault ? [] : targetDtoChecksum.AsSpan());
        foreach (BaseActivationMigrationProperty property in definition.Properties)
        {
            Append(string.Join('\0', property.TargetPropertyPath));
            Append(property.SourcePropertyPath.IsDefaultOrEmpty ? string.Empty : string.Join('\0', property.SourcePropertyPath));
            AppendBytes(property.CanonicalConstant.IsDefault ? [] : property.CanonicalConstant.AsSpan());
        }
        return hash.GetHashAndReset();
        void Definition(BaseActivationDefinitionKey value) { Append(value.Id); Append(value.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)); hash.AppendData(value.Checksum.AsSpan()); }
        void Append(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); hash.AppendData(BitConverter.GetBytes(bytes.Length).Reverse().ToArray()); hash.AppendData(bytes); }
        void AppendBytes(ReadOnlySpan<byte> value) { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value); }
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
