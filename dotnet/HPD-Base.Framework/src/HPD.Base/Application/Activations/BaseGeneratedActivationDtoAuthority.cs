using System.Collections.Immutable;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal interface IBaseActivationInputDtoAuthority<TInput>
{
    JsonTypeInfo<TInput> CurrentInputTypeInfo { get; }
    IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> CurrentInputBindings { get; }
    ReadOnlyMemory<byte> CurrentInputChecksum { get; }
    void ValidateCanonicalMigrationInput(ReadOnlySpan<byte> bytes, bool providerInfluenced);
}

/// <summary>Contains opaque source-generated serializer and scalar authority for one activation DTO pair.</summary>
public sealed class BaseGeneratedActivationDtoAuthority<TInput, TResult> : IBaseSerializerMetadataSource, IBaseActivationInputDtoAuthority<TInput>
{
    private JsonTypeInfo<TInput>? _input;
    private JsonTypeInfo<TResult>? _result;

    internal BaseGeneratedActivationDtoAuthority(
        string id,
        int version,
        string owningModuleId,
        string inputTypeId,
        string resultTypeId,
        BaseSerializerContextRegistration registration,
        IReadOnlyList<BaseSerializerPropertyDeclaration> declarations,
        IReadOnlyList<BaseModuleDtoPropertyBinding> inputBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings,
        IReadOnlyList<string> serializerOptionsReceipt)
    {
        Id = new string(id.AsSpan());
        Version = version;
        OwningModuleId = new string(owningModuleId.AsSpan());
        InputTypeId = new string(inputTypeId.AsSpan());
        ResultTypeId = new string(resultTypeId.AsSpan());
        Registration = registration;
        Declarations = declarations.ToArray();
        InputBindings = Freeze(inputBindings, typeof(TInput));
        ResultBindings = Freeze(resultBindings, typeof(TResult));
        SerializerOptionsReceipt = serializerOptionsReceipt.Select(static value => new string(value.AsSpan())).ToArray();

        JsonSerializerContext context = registration.CreateOwned();
        _input = context.GetTypeInfo(typeof(TInput)) as JsonTypeInfo<TInput>
            ?? throw new InvalidOperationException("base.activation.dtoAuthorityInvalid");
        _result = context.GetTypeInfo(typeof(TResult)) as JsonTypeInfo<TResult>
            ?? throw new InvalidOperationException("base.activation.dtoAuthorityInvalid");
        InputDtoAuthorityChecksum = Compute(rootRole: 1, InputTypeId, typeof(TInput), _input, InputBindings);
        ResultDtoAuthorityChecksum = Compute(rootRole: 2, ResultTypeId, typeof(TResult), _result, ResultBindings);
        DtoAuthorityChecksum = PairChecksum();
        InputDisclosureChecksum = DisclosureChecksum(InputBindings.Values);
        ResultDisclosureChecksum = DisclosureChecksum(ResultBindings.Values);
    }

    /// <summary>Gets the stable DTO-authority identity.</summary>
    public string Id { get; }
    /// <summary>Gets the positive DTO-authority version.</summary>
    public int Version { get; }
    /// <summary>Gets the owning module identity.</summary>
    public string OwningModuleId { get; }
    /// <summary>Gets the stable input type identity.</summary>
    public string InputTypeId { get; }
    /// <summary>Gets the stable result type identity.</summary>
    public string ResultTypeId { get; }
    /// <summary>Gets the canonical input DTO-authority checksum.</summary>
    public ReadOnlyMemory<byte> InputDtoAuthorityChecksum { get; }
    /// <summary>Gets the canonical result DTO-authority checksum.</summary>
    public ReadOnlyMemory<byte> ResultDtoAuthorityChecksum { get; }
    /// <summary>Gets the canonical paired DTO-authority checksum.</summary>
    public ReadOnlyMemory<byte> DtoAuthorityChecksum { get; }

    internal ReadOnlyMemory<byte> InputDisclosureChecksum { get; }
    internal ReadOnlyMemory<byte> ResultDisclosureChecksum { get; }
    internal JsonTypeInfo<TInput> InputTypeInfo => _input ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    internal JsonTypeInfo<TResult> ResultTypeInfo => _result ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    internal IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> InputBindings { get; }
    internal IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> ResultBindings { get; }
    internal BaseSerializerContextRegistration SerializerRegistration => Registration;
    internal IReadOnlyList<BaseSerializerPropertyDeclaration> SerializerDeclarations => Declarations;
    private BaseSerializerContextRegistration Registration { get; }
    private IReadOnlyList<BaseSerializerPropertyDeclaration> Declarations { get; }
    private IReadOnlyList<string> SerializerOptionsReceipt { get; }
    JsonTypeInfo<TInput> IBaseActivationInputDtoAuthority<TInput>.CurrentInputTypeInfo => InputTypeInfo;
    IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> IBaseActivationInputDtoAuthority<TInput>.CurrentInputBindings => InputBindings;
    ReadOnlyMemory<byte> IBaseActivationInputDtoAuthority<TInput>.CurrentInputChecksum => InputDtoAuthorityChecksum;
    void IBaseActivationInputDtoAuthority<TInput>.ValidateCanonicalMigrationInput(
        ReadOnlySpan<byte> bytes, bool providerInfluenced) =>
        ValidateCanonicalMigrationInput(bytes, providerInfluenced);

    internal byte[] CanonicalInput(TInput input, bool providerInfluenced = false) =>
        Canonical(input, InputTypeInfo, InputBindings, providerInfluenced, "base.activation.inputInvalid");

    internal byte[] CanonicalResult(TResult result, bool providerInfluenced = false) =>
        Canonical(result, ResultTypeInfo, ResultBindings, providerInfluenced, "base.activation.handlerContractInvalid");

    internal TInput DecodeInput(ReadOnlySpan<byte> bytes, bool providerInfluenced)
    {
        BaseModuleProgramEvaluator<TInput, TResult>.ValidateDto(bytes, InputBindings, providerInfluenced);
        TInput? value = JsonSerializer.Deserialize(bytes, InputTypeInfo);
        if (value is null) throw new InvalidOperationException(providerInfluenced
            ? "base.activation.providerContractInvalid" : "base.activation.inputInvalid");
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(value, InputTypeInfo);
        if (!canonical.AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException(providerInfluenced
            ? "base.activation.providerContractInvalid" : "base.activation.inputInvalid");
        return value;
    }

    private void ValidateCanonicalMigrationInput(ReadOnlySpan<byte> bytes, bool providerInfluenced)
    {
        string failure = providerInfluenced
            ? "base.activation.providerContractInvalid"
            : "base.activation.migrationInvalid";
        try
        {
            BaseModuleProgramEvaluator<TInput, TResult>.ValidateDto(bytes, InputBindings, providerInfluenced);
            TInput? value = JsonSerializer.Deserialize(bytes, InputTypeInfo);
            if (value is null) throw new JsonException();

            using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var buffer = new System.Buffers.ArrayBufferWriter<byte>(bytes.Length);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (JsonPropertyInfo property in InputTypeInfo.Properties)
                {
                    if (!document.RootElement.TryGetProperty(property.Name, out JsonElement element)) continue;
                    writer.WritePropertyName(property.Name);
                    element.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            if (!buffer.WrittenSpan.SequenceEqual(bytes)) throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or BaseModuleScalarContractException)
        {
            throw new BaseActivationDtoContractException(failure, exception);
        }
    }

    internal TResult DecodeResult(ReadOnlySpan<byte> bytes, bool providerInfluenced)
    {
        BaseModuleProgramEvaluator<TInput, TResult>.ValidateDto(bytes, ResultBindings, providerInfluenced);
        TResult? value = JsonSerializer.Deserialize(bytes, ResultTypeInfo);
        if (value is null) throw new InvalidOperationException(providerInfluenced
            ? "base.activation.providerContractInvalid" : "base.activation.handlerContractInvalid");
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(value, ResultTypeInfo);
        if (!canonical.AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException(providerInfluenced
            ? "base.activation.providerContractInvalid" : "base.activation.handlerContractInvalid");
        return value;
    }

    IReadOnlyList<JsonTypeInfo> IBaseSerializerMetadataSource.Roots => [];
    bool IBaseSerializerMetadataSource.Generated => true;
    BaseSerializerContextRegistration? IBaseSerializerMetadataSource.Registration => Registration;
    IReadOnlyList<Type> IBaseSerializerMetadataSource.RootTypes => [typeof(TInput), typeof(TResult)];
    IReadOnlyList<BaseSerializerPropertyDeclaration>? IBaseSerializerMetadataSource.SerializerDeclarations => Declarations;
    CollectionDefinition? IBaseSerializerMetadataSource.CollectionDefinition => null;
    void IBaseSerializerMetadataSource.Bind(BaseSerializerMetadataOwner owner)
        => BindOwner(owner);

    internal void BindOwner(BaseSerializerMetadataOwner owner)
    {
        _input = owner.Resolve(this, typeof(TInput)) as JsonTypeInfo<TInput>
            ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
        _result = owner.Resolve(this, typeof(TResult)) as JsonTypeInfo<TResult>
            ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    }

    private static byte[] Canonical<T>(T value, JsonTypeInfo<T> typeInfo,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> bindings, bool providerInfluenced,
        string localFailureCode)
    {
        if (value is null)
            throw new BaseActivationDtoContractException(providerInfluenced
                ? "base.activation.providerContractInvalid" : localFailureCode);
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
            BaseModuleProgramEvaluator<TInput, TResult>.ValidateDto(bytes, bindings, providerInfluenced);
            return bytes;
        }
        catch (Exception exception) when (exception is JsonException or BaseModuleScalarContractException)
        {
            throw new BaseActivationDtoContractException(providerInfluenced
                ? "base.activation.providerContractInvalid" : localFailureCode, exception);
        }
    }

    private byte[] Compute(int rootRole, string typeId, Type rootType, JsonTypeInfo root,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> bindings)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, rootRole == 1 ? "base.activation.dto.input.v1\0" : "base.activation.dto.result.v1\0");
        Add(hash, Id); Add(hash, Version); Add(hash, OwningModuleId); Add(hash, InputTypeId); Add(hash, ResultTypeId); Add(hash, rootRole);
        Add(hash, BaseSerializerContract.CanonicalTypeIdentity(rootType));
        Add(hash, typeId);
        Add(hash, BaseSerializerContract.CanonicalTypeIdentity(Registration.ContextType));
        foreach (string option in SerializerOptionsReceipt.Order(StringComparer.Ordinal)) Add(hash, option);
        Add(hash, Convert.FromHexString(BaseSerializerContract.GraphFingerprint(root, Declarations)));
        foreach (BaseSerializerPropertyDeclaration declaration in Declarations
            .Where(value => value.DeclaringType == rootType)
            .OrderBy(static value => value.ApplicationName, StringComparer.Ordinal))
        {
            Add(hash, declaration.ApplicationName);
            Add(hash, BaseSerializerContract.CanonicalTypeIdentity(declaration.PropertyType));
            Add(hash, declaration.ExplicitWireName ?? string.Empty);
            Add(hash, declaration.Required ? 1 : 0);
            Add(hash, declaration.Nullable ? 1 : 0);
            Add(hash, declaration.Ignored ? 1 : 0);
            Add(hash, declaration.ExplicitNever ? 1 : 0);
            Add(hash, declaration.ConverterIdentity);
            Add(hash, declaration.ConverterType is null
                ? string.Empty : BaseSerializerContract.CanonicalTypeIdentity(declaration.ConverterType));
        }
        foreach (BaseModuleDtoPropertyBinding binding in bindings.Values.OrderBy(static value => value.PathKey, StringComparer.Ordinal))
        {
            Add(hash, binding.PathKey); Add(hash, binding.ApplicationName);
            Add(hash, BaseSerializerContract.CanonicalTypeIdentity(binding.DeclaringType));
            Add(hash, BaseSerializerContract.CanonicalTypeIdentity(binding.PropertyType ?? typeof(object)));
            Add(hash, binding.ScalarAuthority.AuthorityChecksum.ToArray());
            Add(hash, (int)binding.Confidentiality); Add(hash, (int)binding.RecordDisclosure);
        }
        return hash.GetHashAndReset();
    }

    private byte[] PairChecksum()
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "base.activation.dto.pair.v1\0"); Add(hash, Id); Add(hash, Version); Add(hash, OwningModuleId);
        Add(hash, InputDtoAuthorityChecksum.Span); Add(hash, ResultDtoAuthorityChecksum.Span);
        return hash.GetHashAndReset();
    }

    private static byte[] DisclosureChecksum(IEnumerable<BaseModuleDtoPropertyBinding> bindings)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "base.activation.disclosure.v1\0");
        foreach (BaseModuleDtoPropertyBinding binding in bindings.OrderBy(static value => value.PathKey, StringComparer.Ordinal))
        {
            Add(hash, binding.PathKey); Add(hash, binding.ApplicationName);
            Add(hash, (int)binding.Confidentiality); Add(hash, (int)binding.RecordDisclosure);
            Add(hash, (int)binding.ScalarAuthority.ValueType.Nullability);
        }
        return hash.GetHashAndReset();
    }

    private static IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> Freeze(
        IReadOnlyList<BaseModuleDtoPropertyBinding> source, Type root)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new Dictionary<string, BaseModuleDtoPropertyBinding>(StringComparer.Ordinal);
        foreach (BaseModuleDtoPropertyBinding binding in source)
        {
            if (binding.DeclaringType != root || !result.TryAdd(binding.PathKey, binding))
                throw new InvalidOperationException("base.activation.dtoAuthorityInvalid");
        }
        if (result.Count is < 1 or > 128) throw new InvalidOperationException("base.activation.dtoAuthorityInvalid");
        return result;
    }

    private static void Add(IncrementalHash hash, string value) => Add(hash, Encoding.UTF8.GetBytes(value));
    private static void Add(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); hash.AppendData(bytes);
    }
    private static void Add(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length); hash.AppendData(value);
    }
}

internal sealed class BaseActivationDtoContractException(string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    internal string Code { get; } = code;
}

/// <summary>Infrastructure-only factory used by generated activation DTO declarations.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BaseGeneratedActivationDtos
{
    /// <summary>Creates one opaque generated activation DTO authority.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseGeneratedActivationDtoAuthority<TInput, TResult> Register<TInput, TResult>(
        string id, int version, string owningModuleId, string inputTypeId, string resultTypeId,
        BaseSerializerContextRegistration registration,
        IReadOnlyList<BaseSerializerPropertyDeclaration> declarations,
        IReadOnlyList<BaseModuleDtoPropertyBinding> inputBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings,
        IReadOnlyList<string> serializerOptionsReceipt)
    {
        BaseApplicationId.Validate(id, nameof(id)); BaseApplicationId.Validate(owningModuleId, nameof(owningModuleId));
        BaseApplicationId.Validate(inputTypeId, nameof(inputTypeId)); BaseApplicationId.Validate(resultTypeId, nameof(resultTypeId));
        if (version < 1) throw new InvalidOperationException("base.activation.dtoAuthorityInvalid");
        return new(id, version, owningModuleId, inputTypeId, resultTypeId, registration, declarations,
            inputBindings, resultBindings, serializerOptionsReceipt);
    }
}
