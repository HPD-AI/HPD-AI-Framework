using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>Defines one graph-installed semantic activation identity.</summary>
public sealed record BaseSemanticActivationKeyDefinition
{
    /// <summary>Gets the stable definition identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning application identifier.</summary>
    public required string OwningApplicationId { get; init; }
    /// <summary>Gets the owning module identifier.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the module operation that ensures the semantic activation.</summary>
    public required BaseSemanticActivationModuleOperationIdentity EnsureOperation { get; init; }
    /// <summary>Gets the module operation permitted to retire the semantic activation.</summary>
    public required BaseSemanticActivationModuleOperationIdentity RetirementOperation { get; init; }
    /// <summary>Gets the installed activation definition created by ensure.</summary>
    public required BaseActivationDefinitionKey Activation { get; init; }
    /// <summary>Gets the protected subject-scope kind.</summary>
    public required BaseSubjectScopeKind ScopeKind { get; init; }
    /// <summary>Gets the exact grant required to ensure this identity.</summary>
    public required string EnsureGrantId { get; init; }
    /// <summary>Gets the exact grant required to retire this identity.</summary>
    public required string RetirementGrantId { get; init; }
    /// <summary>Gets the exact grant required for maintenance.</summary>
    public required string MaintenanceGrantId { get; init; }
    /// <summary>Gets the closed compaction contract.</summary>
    public required BaseSemanticActivationCompactionContract Compaction { get; init; }
    /// <summary>Gets the complete definition limits.</summary>
    public required BaseSemanticActivationLimits Limits { get; init; }
    /// <summary>Gets the exact graph-owned L44 request type identity.</summary>
    public required string RequestTypeId { get; init; }
    /// <summary>Gets the exact L44 request serializer checksum.</summary>
    public required ImmutableArray<byte> RequestSerializerChecksum { get; init; }
    /// <summary>Gets the checksum of the frozen closed key expression.</summary>
    public required ImmutableArray<byte> KeyExpressionChecksum { get; init; }
    /// <summary>Gets the canonical definition checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Base type for the closed semantic-activation compaction union.</summary>
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseSemanticActivationNoCompaction), "none")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseSemanticActivationSubjectRetirementCompaction), "subjectRetirement")]
public abstract record BaseSemanticActivationCompactionContract;

/// <summary>Retains terminal semantic authority permanently.</summary>
public sealed record BaseSemanticActivationNoCompaction : BaseSemanticActivationCompactionContract;

/// <summary>Permits bounded compaction after exact exported-subject retirement.</summary>
public sealed record BaseSemanticActivationSubjectRetirementCompaction : BaseSemanticActivationCompactionContract
{
    /// <summary>Gets the exact exported-subject contract.</summary>
    public required BaseSemanticActivationSubjectContractIdentity SubjectContract { get; init; }
    /// <summary>Gets the L44 request-property identity containing the subject reference.</summary>
    public required string SubjectReferenceRequestPropertyId { get; init; }
    /// <summary>Gets the exact lifecycle-retirement grant.</summary>
    public required string LifecycleRetirementGrantId { get; init; }
}

/// <summary>Identifies one exported-subject contract used by compaction.</summary>
public sealed record BaseSemanticActivationSubjectContractIdentity
{
    /// <summary>Gets the stable contract identifier.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the positive contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the exact contract checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
}

/// <summary>Defines installed semantic-slot and execution bounds.</summary>
public sealed record BaseSemanticActivationLimits
{
    /// <summary>Gets the maximum canonical-key bytes.</summary>
    public required int MaximumCanonicalKeyBytes { get; init; }
    /// <summary>Gets the maximum live slots.</summary>
    public required long MaximumLiveSlots { get; init; }
    /// <summary>Gets the maximum retired slots.</summary>
    public required long MaximumRetiredSlots { get; init; }
    /// <summary>Gets the maximum compact absence markers.</summary>
    public required long MaximumAbsenceMarkers { get; init; }
    /// <summary>Gets per-execution limits.</summary>
    public required BaseSemanticActivationExecutionLimits Execution { get; init; }
    /// <summary>Gets deadline capabilities.</summary>
    public required BaseSemanticActivationDeadlineCapability Deadlines { get; init; }
}

/// <summary>Defines exact per-execution semantic activation maxima.</summary>
public sealed record BaseSemanticActivationExecutionLimits
{
    /// <summary>Gets the maximum semantic operations.</summary>
    public required int MaximumOperations { get; init; }
    /// <summary>Gets the maximum scope-directory reads.</summary>
    public required int MaximumScopeDirectoryReads { get; init; }
    /// <summary>Gets the maximum slot reads.</summary>
    public required int MaximumSlotReads { get; init; }
    /// <summary>Gets the maximum activation reads.</summary>
    public required int MaximumActivationReads { get; init; }
    /// <summary>Gets the maximum read intervals.</summary>
    public required int MaximumReadIntervals { get; init; }
    /// <summary>Gets the maximum index operations.</summary>
    public required int MaximumIndexOperations { get; init; }
    /// <summary>Gets the maximum activation bytes.</summary>
    public required long MaximumActivationBytes { get; init; }
    /// <summary>Gets the maximum scope-directory bytes.</summary>
    public required long MaximumScopeDirectoryBytes { get; init; }
    /// <summary>Gets the maximum evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum receipt bytes.</summary>
    public required long MaximumReceiptBytes { get; init; }
    /// <summary>Gets the maximum retained transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
}

/// <summary>Defines bounded phase deadlines for semantic operations.</summary>
public sealed record BaseSemanticActivationDeadlineCapability
{
    /// <summary>Gets the acquisition timeout.</summary>
    public required TimeSpan AcquisitionTimeout { get; init; }
    /// <summary>Gets the transaction timeout.</summary>
    public required TimeSpan TransactionTimeout { get; init; }
    /// <summary>Gets the commit-observation timeout.</summary>
    public required TimeSpan CommitObservationTimeout { get; init; }
    /// <summary>Gets the receipt-resolution timeout.</summary>
    public required TimeSpan ReceiptResolutionTimeout { get; init; }
    /// <summary>Gets the maintenance timeout.</summary>
    public required TimeSpan MaintenanceTimeout { get; init; }
    /// <summary>Gets the quarantine-retention timeout.</summary>
    public required TimeSpan QuarantineRetentionTimeout { get; init; }
}

/// <summary>Contains one inert, unbound semantic key preimage.</summary>
public sealed class BaseSemanticActivationKey<TDefinition> : IBaseSemanticActivationKey
{
    private readonly byte[] _canonicalKey;
    private readonly byte[] _preimageChecksum;

    internal BaseSemanticActivationKey(string applicationId, string moduleId, long ownerGeneration, string definitionId, int definitionVersion, byte[] definitionChecksum, byte[] canonicalKey)
    {
        ApplicationId = applicationId;
        ModuleId = moduleId;
        OwnerGeneration = ownerGeneration;
        DefinitionId = definitionId;
        DefinitionVersion = definitionVersion;
        DefinitionChecksum = definitionChecksum;
        _canonicalKey = canonicalKey;
        _preimageChecksum = SHA256.HashData(canonicalKey);
    }

    internal string ApplicationId { get; }
    internal string ModuleId { get; }
    internal long OwnerGeneration { get; }
    internal string DefinitionId { get; }
    internal int DefinitionVersion { get; }
    internal byte[] DefinitionChecksum { get; }
    internal byte[] CopyCanonicalKey() => _canonicalKey.ToArray();

    string IBaseSemanticActivationKey.DefinitionId => DefinitionId;
    string IBaseSemanticActivationKey.ApplicationId => ApplicationId;
    string IBaseSemanticActivationKey.ModuleId => ModuleId;
    long IBaseSemanticActivationKey.OwnerGeneration => OwnerGeneration;
    int IBaseSemanticActivationKey.DefinitionVersion => DefinitionVersion;
    byte[] IBaseSemanticActivationKey.CopyDefinitionChecksum() => DefinitionChecksum.ToArray();
    byte[] IBaseSemanticActivationKey.CopyCanonicalKey() => CopyCanonicalKey();
    byte[] IBaseSemanticActivationKey.CopyPreimageChecksum() => _preimageChecksum.ToArray();
}

internal interface IBaseSemanticActivationKey
{
    string ApplicationId { get; }
    string ModuleId { get; }
    long OwnerGeneration { get; }
    string DefinitionId { get; }
    int DefinitionVersion { get; }
    byte[] CopyDefinitionChecksum();
    byte[] CopyCanonicalKey();
    byte[] CopyPreimageChecksum();
}

internal abstract record BaseSemanticActivationGuardedRequest
{
    internal required IBaseSemanticActivationKey Key { get; init; }
    internal required BaseOwnedSubjectScopeEvidence Scope { get; init; }
}

internal sealed record BaseSemanticActivationGuardedEnsureRequest : BaseSemanticActivationGuardedRequest
{
    internal required BaseActivationDefinitionKey Activation { get; init; }
    internal required ImmutableArray<byte> CanonicalInput { get; init; }
    internal required ImmutableArray<byte> InputChecksum { get; init; }
    internal required DateTimeOffset? DueAt { get; init; }
}

internal sealed record BaseSemanticActivationGuardedRetireRequest : BaseSemanticActivationGuardedRequest;

/// <summary>Base type for the closed semantic-key expression union.</summary>
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseSemanticActivationKeyTupleExpression), "tuple")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseSemanticActivationKeyPropertyExpression), "property")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseSemanticActivationKeyConstantExpression), "constant")]
public abstract record BaseSemanticActivationKeyExpression;

/// <summary>Concatenates an ordered, nonempty list of semantic-key elements.</summary>
public sealed record BaseSemanticActivationKeyTupleExpression : BaseSemanticActivationKeyExpression
{
    /// <summary>Gets ordered key elements.</summary>
    public required ImmutableArray<BaseSemanticActivationKeyExpression> Elements { get; init; }
}

/// <summary>Reads one L44-bound request property into a semantic key.</summary>
public sealed record BaseSemanticActivationKeyPropertyExpression : BaseSemanticActivationKeyExpression
{
    /// <summary>Gets the exact stable request-property path.</summary>
    public required BaseModuleRequestPropertyReference Property { get; init; }
    /// <summary>Gets the closed scalar encoding.</summary>
    public required BaseSemanticActivationKeyScalarKind ScalarKind { get; init; }
    /// <summary>Gets the maximum encoded value bytes.</summary>
    public required int MaximumValueBytes { get; init; }
    /// <summary>Gets whether explicit null is permitted.</summary>
    public required bool AllowNull { get; init; }
}

/// <summary>Adds one graph-owned canonical constant to a semantic key.</summary>
public sealed record BaseSemanticActivationKeyConstantExpression : BaseSemanticActivationKeyExpression
{
    /// <summary>Gets the closed scalar encoding.</summary>
    public required BaseSemanticActivationKeyScalarKind ScalarKind { get; init; }
    /// <summary>Gets strict base-json-v1 scalar bytes.</summary>
    public required ImmutableArray<byte> CanonicalBaseJson { get; init; }
    /// <summary>Gets the maximum encoded value bytes.</summary>
    public required int MaximumValueBytes { get; init; }
}

/// <summary>Classifies the finite scalar encodings permitted in semantic keys.</summary>
public enum BaseSemanticActivationKeyScalarKind
{
    /// <summary>Canonical UTF-8 string.</summary>
    String = 1,
    /// <summary>Signed 64-bit integer.</summary>
    Int64 = 2,
    /// <summary>Canonical 16-byte GUID.</summary>
    Guid = 3,
    /// <summary>Fixed or bounded binary value.</summary>
    Binary = 4,
    /// <summary>Typed BASE record identifier.</summary>
    RecordId = 5,
    /// <summary>Exported-subject incarnation.</summary>
    SubjectIncarnation = 6,
    /// <summary>Stable signed enum value.</summary>
    Enum = 7,
}

/// <summary>Provides generated key construction for one semantic definition.</summary>
public sealed class BaseSemanticActivationKeyIdentity<TRequest, TDefinition>
{
    private readonly Func<TRequest, ReadOnlyMemory<byte>> _create;
    private readonly byte[] _definitionChecksum;
    private readonly int _maximumCanonicalKeyBytes;
    private readonly string _applicationId;
    private readonly string _moduleId;
    private readonly byte[] _keyExpressionChecksum;
    private readonly Func<byte[]> _requestSerializerChecksum;

    internal BaseSemanticActivationKeyIdentity(string applicationId, string moduleId, string definitionId, int definitionVersion, ReadOnlySpan<byte> definitionChecksum, ReadOnlySpan<byte> keyExpressionChecksum, int maximumCanonicalKeyBytes, Func<byte[]> requestSerializerChecksum, Func<TRequest, ReadOnlyMemory<byte>> create)
    {
        BaseApplicationId.Validate(applicationId, nameof(applicationId));
        BaseApplicationId.Validate(moduleId, nameof(moduleId));
        BaseApplicationId.Validate(definitionId, nameof(definitionId));
        ArgumentOutOfRangeException.ThrowIfLessThan(definitionVersion, 1);
        if (definitionChecksum.Length != 32 || keyExpressionChecksum.Length != 32) throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        if (maximumCanonicalKeyBytes is < 1 or > 1024) throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(requestSerializerChecksum);
        DefinitionId = new string(definitionId.AsSpan());
        DefinitionVersion = definitionVersion;
        _definitionChecksum = definitionChecksum.ToArray();
        _maximumCanonicalKeyBytes = maximumCanonicalKeyBytes;
        _create = create;
        _applicationId = new string(applicationId.AsSpan());
        _moduleId = new string(moduleId.AsSpan());
        _keyExpressionChecksum = keyExpressionChecksum.ToArray();
        _requestSerializerChecksum = requestSerializerChecksum;
    }

    /// <summary>Gets the stable semantic definition ID.</summary>
    public string DefinitionId { get; }
    /// <summary>Gets the positive semantic definition version.</summary>
    public int DefinitionVersion { get; }
    internal BaseSemanticActivationKey<TDefinition> Create(TRequest request, long ownerGeneration)
    {
        byte[] canonical = _create(request).ToArray();
        if (canonical.Length is < 1 || canonical.Length > _maximumCanonicalKeyBytes)
            throw new InvalidOperationException("base.semanticActivation.keyInvalid");
        return new(_applicationId, _moduleId, ownerGeneration, DefinitionId, DefinitionVersion, _definitionChecksum.ToArray(), canonical);
    }

    internal ReadOnlySpan<byte> DefinitionChecksum => _definitionChecksum;
    internal int MaximumCanonicalKeyBytes => _maximumCanonicalKeyBytes;
    internal string ApplicationId => _applicationId;
    internal string ModuleId => _moduleId;
    internal ReadOnlySpan<byte> KeyExpressionChecksum => _keyExpressionChecksum;
    internal byte[] ActualRequestSerializerChecksum => _requestSerializerChecksum();
}

/// <summary>Registers one typed semantic definition and its key construction authority.</summary>
public sealed record BaseSemanticActivationRegistration<TRequest, TDefinition>
{
    /// <summary>Gets the semantic definition.</summary>
    public required BaseSemanticActivationKeyDefinition Definition { get; init; }
    /// <summary>Gets the graph-owned L44 request type identity.</summary>
    public required string RequestTypeId { get; init; }
    /// <summary>Gets the exact L44 request serializer checksum.</summary>
    public required ImmutableArray<byte> RequestSerializerChecksum { get; init; }
    /// <summary>Gets the generated or trusted-host key identity.</summary>
    public required BaseSemanticActivationKeyIdentity<TRequest, TDefinition> KeyIdentity { get; init; }
}

internal interface IBaseSemanticActivationRegistration
{
    BaseSemanticActivationKeyDefinition Definition { get; }
    Type RequestType { get; }
    ImmutableArray<byte> ActualRequestSerializerChecksum { get; }
}

internal sealed class BaseInstalledSemanticActivationRegistration<TRequest, TDefinition> : IBaseSemanticActivationRegistration
{
    internal BaseInstalledSemanticActivationRegistration(BaseSemanticActivationRegistration<TRequest, TDefinition> registration)
    {
        Definition = BaseSemanticActivationDefinitionContract.Seal(registration.Definition);
        if (registration.Definition.Checksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(registration.Definition.Checksum.AsSpan(), Definition.Checksum.AsSpan()))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        if (string.IsNullOrWhiteSpace(registration.RequestTypeId) || registration.RequestSerializerChecksum.Length != 32)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        if (!string.Equals(Definition.Id, registration.KeyIdentity.DefinitionId, StringComparison.Ordinal)
            || Definition.Version != registration.KeyIdentity.DefinitionVersion
            || !string.Equals(Definition.OwningApplicationId, registration.KeyIdentity.ApplicationId, StringComparison.Ordinal)
            || !string.Equals(Definition.OwningModuleId, registration.KeyIdentity.ModuleId, StringComparison.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(Definition.Checksum.AsSpan(), registration.KeyIdentity.DefinitionChecksum)
            || !string.Equals(Definition.RequestTypeId, registration.RequestTypeId, StringComparison.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(Definition.RequestSerializerChecksum.AsSpan(), registration.RequestSerializerChecksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(Definition.KeyExpressionChecksum.AsSpan(), registration.KeyIdentity.KeyExpressionChecksum)
            || Definition.Limits.MaximumCanonicalKeyBytes != registration.KeyIdentity.MaximumCanonicalKeyBytes)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        KeyIdentity = registration.KeyIdentity;
        RequestTypeId = new string(registration.RequestTypeId.AsSpan());
        RequestSerializerChecksum = registration.RequestSerializerChecksum.ToArray().ToImmutableArray();
    }

    public BaseSemanticActivationKeyDefinition Definition { get; }
    public Type RequestType => typeof(TRequest);
    public ImmutableArray<byte> ActualRequestSerializerChecksum => KeyIdentity.ActualRequestSerializerChecksum.ToImmutableArray();
    internal BaseSemanticActivationKeyIdentity<TRequest, TDefinition> KeyIdentity { get; }
    internal string RequestTypeId { get; }
    internal ImmutableArray<byte> RequestSerializerChecksum { get; }
}

/// <summary>Infrastructure-only factory used by generated semantic-key declarations.</summary>
public static class BaseGeneratedSemanticActivations
{
    /// <summary>Creates one inert generated key identity after generated contract validation.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseSemanticActivationKeyIdentity<TRequest, TDefinition> Register<TRequest, TResult, TDefinition>(
        string definitionId,
        int definitionVersion,
        string applicationId,
        string moduleId,
        ReadOnlySpan<byte> definitionChecksum,
        int maximumCanonicalKeyBytes,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> requestAuthority,
        BaseSemanticActivationKeyExpression expression) =>
        BaseSemanticActivationKeyCompiler.Create<TRequest, TResult, TDefinition>(applicationId, moduleId,
            definitionId, definitionVersion, definitionChecksum, maximumCanonicalKeyBytes,
            requestAuthority, expression);
}

/// <summary>Provides trusted-host manual semantic-key construction through the same closed compiler as generated definitions.</summary>
public static class BaseSemanticActivations
{
    /// <summary>Creates one manual key identity from graph-owned L44/L50 request metadata and a closed key expression.</summary>
    public static BaseSemanticActivationKeyIdentity<TRequest, TDefinition> CreateKeyIdentity<TRequest, TResult, TDefinition>(
        string definitionId,
        int definitionVersion,
        string applicationId,
        string moduleId,
        ReadOnlySpan<byte> definitionChecksum,
        int maximumCanonicalKeyBytes,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> requestAuthority,
        BaseSemanticActivationKeyExpression expression) =>
        BaseSemanticActivationKeyCompiler.Create<TRequest, TResult, TDefinition>(applicationId, moduleId,
            definitionId, definitionVersion, definitionChecksum, maximumCanonicalKeyBytes,
            requestAuthority, expression);
}

internal static class BaseSemanticActivationKeyCompiler
{
    internal static BaseSemanticActivationKeyIdentity<TRequest, TDefinition> Create<TRequest, TResult, TDefinition>(
        string applicationId,
        string moduleId,
        string definitionId,
        int definitionVersion,
        ReadOnlySpan<byte> definitionChecksum,
        int maximumCanonicalKeyBytes,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> requestAuthority,
        BaseSemanticActivationKeyExpression expression)
    {
        ArgumentNullException.ThrowIfNull(requestAuthority);
        BaseSemanticActivationKeyExpression frozen = FreezeAndValidate(expression, requestAuthority.RequestBindings, 1);
        byte[] expressionChecksum = ExpressionChecksum(frozen);
        return new(applicationId, moduleId, definitionId, definitionVersion, definitionChecksum,
            expressionChecksum, maximumCanonicalKeyBytes,
            () => Convert.FromHexString(BaseSerializerContract.GraphFingerprint(
                requestAuthority.RequestTypeInfo, requestAuthority.SerializerDeclarations)),
            request => Encode(request, requestAuthority, frozen));
    }

    internal static byte[] ExpressionChecksum(BaseSemanticActivationKeyExpression expression)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.keyExpression.v1\0"u8);
        Add(expression); return hash.GetHashAndReset();
        void Text(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes); }
        void Int(int value) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); hash.AppendData(bytes); }
        void Add(BaseSemanticActivationKeyExpression value)
        {
            switch (value)
            {
                case BaseSemanticActivationKeyTupleExpression tuple:
                    hash.AppendData([1]); Int(tuple.Elements.Length); foreach (BaseSemanticActivationKeyExpression item in tuple.Elements) Add(item); break;
                case BaseSemanticActivationKeyPropertyExpression property:
                    hash.AppendData([2]); Int(property.Property.StablePropertyPath.Length); foreach (string edge in property.Property.StablePropertyPath) Text(edge);
                    Text(property.Property.DeclaredTypeId); Int((int)property.ScalarKind); Int(property.MaximumValueBytes); hash.AppendData([property.AllowNull ? (byte)1 : (byte)0]); break;
                case BaseSemanticActivationKeyConstantExpression constant:
                    hash.AppendData([3]); Int((int)constant.ScalarKind); Int(constant.MaximumValueBytes); Int(constant.CanonicalBaseJson.Length); hash.AppendData(constant.CanonicalBaseJson.AsSpan()); break;
                default: throw new InvalidOperationException("base.semanticActivation.contractInvalid");
            }
        }
    }

    private static BaseSemanticActivationKeyExpression FreezeAndValidate(
        BaseSemanticActivationKeyExpression expression,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> bindings,
        int depth)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (depth > 16) throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        return expression switch
        {
            BaseSemanticActivationKeyTupleExpression tuple when tuple.Elements.Length is >= 1 and <= 16 => tuple with
            {
                Elements = tuple.Elements.Select(value => FreezeAndValidate(value, bindings, depth + 1)).ToImmutableArray(),
            },
            BaseSemanticActivationKeyPropertyExpression property => FreezeProperty(property, bindings),
            BaseSemanticActivationKeyConstantExpression constant => FreezeConstant(constant),
            _ => throw new InvalidOperationException("base.semanticActivation.contractInvalid"),
        };
    }

    private static BaseSemanticActivationKeyPropertyExpression FreezeProperty(
        BaseSemanticActivationKeyPropertyExpression value,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> bindings)
    {
        if (value.Property.StablePropertyPath.IsDefaultOrEmpty || value.Property.StablePropertyPath.Length > 16
            || value.MaximumValueBytes is < 1 or > 1024 || !Enum.IsDefined(value.ScalarKind))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        string path = string.Join('\0', value.Property.StablePropertyPath);
        if (!bindings.TryGetValue(path, out BaseModuleDtoPropertyBinding? binding)
            || value.AllowNull && !binding.Nullable || !ScalarMatches(binding.PropertyType, value.ScalarKind))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        return value with
        {
            Property = value.Property with
            {
                StablePropertyPath = value.Property.StablePropertyPath.Select(static edge => new string(edge.AsSpan())).ToImmutableArray(),
                DeclaredTypeId = new string(value.Property.DeclaredTypeId.AsSpan()),
            },
        };
    }

    private static BaseSemanticActivationKeyConstantExpression FreezeConstant(BaseSemanticActivationKeyConstantExpression value)
    {
        if (value.CanonicalBaseJson.IsDefaultOrEmpty || value.MaximumValueBytes is < 1 or > 1024 || !Enum.IsDefined(value.ScalarKind))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        using JsonDocument document = JsonDocument.Parse(value.CanonicalBaseJson.ToArray());
        EncodeScalar(document.RootElement, value.ScalarKind, value.MaximumValueBytes, allowNull: false);
        return value with { CanonicalBaseJson = value.CanonicalBaseJson.ToArray().ToImmutableArray() };
    }

    private static ReadOnlyMemory<byte> Encode<TRequest, TResult>(
        TRequest request,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> authority,
        BaseSemanticActivationKeyExpression expression)
    {
        JsonElement root = JsonSerializer.SerializeToElement(request, authority.RequestTypeInfo);
        using var stream = new MemoryStream();
        Write(expression, root, authority, stream);
        return stream.ToArray();
    }

    private static void Write<TRequest, TResult>(BaseSemanticActivationKeyExpression expression, JsonElement root,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> authority, Stream output)
    {
        switch (expression)
        {
            case BaseSemanticActivationKeyTupleExpression tuple:
                output.WriteByte(0x10); WriteLength(output, tuple.Elements.Length);
                foreach (BaseSemanticActivationKeyExpression child in tuple.Elements) Write(child, root, authority, output);
                break;
            case BaseSemanticActivationKeyPropertyExpression property:
                JsonElement current = root; var path = new List<string>();
                foreach (string stableId in property.Property.StablePropertyPath)
                {
                    path.Add(stableId);
                    if (!authority.RequestBindings.TryGetValue(string.Join('\0', path), out BaseModuleDtoPropertyBinding? binding))
                        throw new InvalidOperationException("base.semanticActivation.contractInvalid");
                    string wireName = binding.WirePropertyPath[path.Count - 1];
                    if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(wireName, out current))
                        throw new InvalidOperationException("base.semanticActivation.keyInvalid");
                }
                WriteFramed(output, EncodeScalar(current, property.ScalarKind, property.MaximumValueBytes, property.AllowNull));
                break;
            case BaseSemanticActivationKeyConstantExpression constant:
                using (JsonDocument document = JsonDocument.Parse(constant.CanonicalBaseJson.ToArray()))
                    WriteFramed(output, EncodeScalar(document.RootElement, constant.ScalarKind, constant.MaximumValueBytes, false));
                break;
            default: throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        }
    }

    private static byte[] EncodeScalar(JsonElement value, BaseSemanticActivationKeyScalarKind kind, int maximum, bool allowNull)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!allowNull) throw new InvalidOperationException("base.semanticActivation.keyInvalid");
            return [0];
        }
        byte[] payload = kind switch
        {
            BaseSemanticActivationKeyScalarKind.String or BaseSemanticActivationKeyScalarKind.RecordId =>
                Encoding.UTF8.GetBytes(value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidOperationException("base.semanticActivation.keyInvalid")),
            BaseSemanticActivationKeyScalarKind.Int64 or BaseSemanticActivationKeyScalarKind.Enum => EncodeInt64(value),
            BaseSemanticActivationKeyScalarKind.Guid => value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out Guid guid)
                ? guid.ToByteArray(bigEndian: true) : throw new InvalidOperationException("base.semanticActivation.keyInvalid"),
            BaseSemanticActivationKeyScalarKind.Binary => value.ValueKind == JsonValueKind.String
                ? Convert.FromBase64String(value.GetString()!) : throw new InvalidOperationException("base.semanticActivation.keyInvalid"),
            BaseSemanticActivationKeyScalarKind.SubjectIncarnation => value.ValueKind == JsonValueKind.String
                ? BaseSubjectReferenceEncoding.Decode(value.GetString()!, 24)
                : throw new InvalidOperationException("base.semanticActivation.keyInvalid"),
            _ => throw new InvalidOperationException("base.semanticActivation.keyInvalid"),
        };
        if (payload.Length > maximum) throw new InvalidOperationException("base.semanticActivation.keyInvalid");
        return [(byte)kind, .. payload];
    }

    private static byte[] EncodeInt64(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long number))
            throw new InvalidOperationException("base.semanticActivation.keyInvalid");
        byte[] bytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, number); return bytes;
    }

    private static bool ScalarMatches(Type? type, BaseSemanticActivationKeyScalarKind kind)
    {
        type = Nullable.GetUnderlyingType(type ?? typeof(void)) ?? type;
        return kind switch
        {
            BaseSemanticActivationKeyScalarKind.String => type == typeof(string),
            BaseSemanticActivationKeyScalarKind.Int64 => type is not null && (type == typeof(long) || type == typeof(int) || type == typeof(short) || type == typeof(byte)),
            BaseSemanticActivationKeyScalarKind.Guid => type == typeof(Guid),
            BaseSemanticActivationKeyScalarKind.Binary => type == typeof(byte[]) || type == typeof(BaseBinary),
            BaseSemanticActivationKeyScalarKind.RecordId => type?.IsGenericType == true && type.GetGenericTypeDefinition() == typeof(BaseRecordId<>),
            BaseSemanticActivationKeyScalarKind.SubjectIncarnation => type == typeof(BaseSubjectIncarnation),
            BaseSemanticActivationKeyScalarKind.Enum => type?.IsEnum == true,
            _ => false,
        };
    }

    private static void WriteFramed(Stream output, byte[] bytes) { WriteLength(output, bytes.Length); output.Write(bytes); }
    private static void WriteLength(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); output.Write(bytes);
    }
}

/// <summary>Provides immutable lookup over one finalized semantic-definition owner.</summary>
public sealed class BaseSemanticActivationRegistry
{
    private static long _nextOwnerGeneration;
    private readonly Dictionary<(string Id, int Version), IBaseSemanticActivationRegistration> _registrations;

    internal BaseSemanticActivationRegistry(IEnumerable<IBaseSemanticActivationRegistration> registrations)
    {
        _registrations = new();
        foreach (IBaseSemanticActivationRegistration registration in registrations)
            if (_registrations.Keys.Any(key => string.Equals(key.Id, registration.Definition.Id, StringComparison.Ordinal))
                || !_registrations.TryAdd((registration.Definition.Id, registration.Definition.Version), registration))
                throw new InvalidOperationException("base.semanticActivation.registrationConflict");
        foreach (IBaseSemanticActivationRegistration registration in _registrations.Values)
            if (!CryptographicOperations.FixedTimeEquals(
                    registration.Definition.RequestSerializerChecksum.AsSpan(),
                    registration.ActualRequestSerializerChecksum.AsSpan()))
                throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        OwnerGeneration = Interlocked.Increment(ref _nextOwnerGeneration);
        if (OwnerGeneration <= 0) throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.definitionSet.v1\0"u8);
        foreach (BaseSemanticActivationKeyDefinition definition in Definitions)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(definition.Id));
            hash.AppendData(definition.Checksum.AsSpan());
        }
        DefinitionSetChecksum = hash.GetHashAndReset().ToImmutableArray();
    }

    /// <summary>Finds one exact installed semantic activation definition.</summary>
    public BaseSemanticActivationKeyDefinition? Find(string id, int version) =>
        _registrations.TryGetValue((id, version), out IBaseSemanticActivationRegistration? value) ? value.Definition with { } : null;

    internal BaseSemanticActivationKey<TDefinition> CreateKey<TRequest, TDefinition>(
        BaseSemanticActivationKeyIdentity<TRequest, TDefinition> identity,
        TRequest request)
    {
        if (!_registrations.TryGetValue((identity.DefinitionId, identity.DefinitionVersion), out IBaseSemanticActivationRegistration? installed)
            || installed is not BaseInstalledSemanticActivationRegistration<TRequest, TDefinition> typed
            || !ReferenceEquals(typed.KeyIdentity, identity))
            throw new InvalidOperationException("base.semanticActivation.graphChanged");
        return identity.Create(request, OwnerGeneration);
    }

    internal IReadOnlyList<BaseSemanticActivationKeyDefinition> Definitions => _registrations.Values
        .Select(static value => value.Definition).OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version).ToArray();
    internal long OwnerGeneration { get; }
    internal ImmutableArray<byte> DefinitionSetChecksum { get; }

    internal void ValidatePolicyAuthority(BasePolicyAuthorityOwner owner)
    {
        foreach (BaseSemanticActivationKeyDefinition definition in Definitions)
        {
            string[] grants = [definition.EnsureGrantId, definition.RetirementGrantId, definition.MaintenanceGrantId];
            if (grants.Distinct(StringComparer.Ordinal).Count() != grants.Length
                || grants.Any(grant => owner.Grants.Count(value => string.Equals(value.Definition.Id, grant, StringComparison.Ordinal)) != 1))
                throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        }
    }
}

internal sealed class BaseSemanticActivationRemovalRegistry
{
    private readonly Dictionary<(string Id, int Version), BaseSemanticActivationRemovalAuthority> _authorities;
    internal BaseSemanticActivationRemovalRegistry(IEnumerable<BaseSemanticActivationRemovalAuthority> authorities) =>
        _authorities = authorities.Select(BaseSemanticActivationRemovalAuthorityContract.Seal).ToDictionary(
            static value => (value.From.Id, value.From.Version));
    internal BaseSemanticActivationRemovalAuthority? Find(BaseSemanticActivationDefinitionKey definition) =>
        _authorities.TryGetValue((definition.Id, definition.Version), out BaseSemanticActivationRemovalAuthority? value)
        && CryptographicOperations.FixedTimeEquals(value.From.Checksum.AsSpan(), definition.Checksum.AsSpan())
            ? BaseSemanticActivationRemovalAuthorityContract.Seal(value) : null;
    internal IReadOnlyList<BaseSemanticActivationRemovalAuthority> Authorities => _authorities.Values
        .OrderBy(static value => value.From.Id, StringComparer.Ordinal).ThenBy(static value => value.From.Version)
        .Select(BaseSemanticActivationRemovalAuthorityContract.Seal).ToArray();
}

internal static class BaseSemanticActivationDefinitionContract
{
    internal static BaseSemanticActivationKeyDefinition Seal(BaseSemanticActivationKeyDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        BaseApplicationId.Validate(source.Id, nameof(source.Id));
        BaseApplicationId.Validate(source.OwningApplicationId, nameof(source.OwningApplicationId));
        BaseApplicationId.Validate(source.OwningModuleId, nameof(source.OwningModuleId));
        if (source.Version <= 0)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        ValidateId(source.EnsureGrantId); ValidateId(source.RetirementGrantId); ValidateId(source.MaintenanceGrantId);
        ValidateLimits(source.Limits);
        BaseApplicationId.Validate(source.RequestTypeId, nameof(source.RequestTypeId));
        if (source.RequestSerializerChecksum.Length != 32 || source.KeyExpressionChecksum.Length != 32)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        ValidateOperation(source.EnsureOperation);
        ValidateOperation(source.RetirementOperation);
        if (source.EnsureOperation == source.RetirementOperation || source.Activation.Checksum.Length != 32)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseSemanticActivationKeyDefinition normalized = source with
        {
            Checksum = [],
            Activation = source.Activation with { Checksum = source.Activation.Checksum.ToArray().ToImmutableArray() },
            RequestTypeId = new string(source.RequestTypeId.AsSpan()),
            RequestSerializerChecksum = source.RequestSerializerChecksum.ToArray().ToImmutableArray(),
            KeyExpressionChecksum = source.KeyExpressionChecksum.ToArray().ToImmutableArray(),
            Compaction = SealCompaction(source.Compaction),
        };
        return normalized with { Checksum = ComputeChecksum(normalized).ToImmutableArray() };
    }

    private static void ValidateOperation(BaseSemanticActivationModuleOperationIdentity value)
    {
        BaseApplicationId.Validate(value.OperationId, nameof(value));
        if (value.OperationVersion <= 0 || value.OperationChecksum.Length != 64
            || value.OperationChecksum.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
    }

    private static byte[] ComputeChecksum(BaseSemanticActivationKeyDefinition value)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void AddText(string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes); }
        void AddInt(int number) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, number); hash.AppendData(bytes); }
        void AddLong(long number) { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, number); hash.AppendData(bytes); }
        AddText("base.semanticActivation.definition.v1\0"); AddText(value.Id); AddInt(value.Version); AddText(value.OwningApplicationId); AddText(value.OwningModuleId);
        AddText(value.Activation.Id); AddInt(value.Activation.Version); hash.AppendData(value.Activation.Checksum.AsSpan());
        AddText(value.EnsureOperation.OperationId); AddInt(value.EnsureOperation.OperationVersion); AddText(value.EnsureOperation.OperationChecksum);
        AddText(value.RetirementOperation.OperationId); AddInt(value.RetirementOperation.OperationVersion); AddText(value.RetirementOperation.OperationChecksum);
        AddText(value.RequestTypeId); hash.AppendData(value.RequestSerializerChecksum.AsSpan()); hash.AppendData(value.KeyExpressionChecksum.AsSpan());
        AddInt((int)value.ScopeKind); AddText(value.EnsureGrantId); AddText(value.RetirementGrantId); AddText(value.MaintenanceGrantId);
        AddInt(value.Limits.MaximumCanonicalKeyBytes); AddLong(value.Limits.MaximumLiveSlots); AddLong(value.Limits.MaximumRetiredSlots); AddLong(value.Limits.MaximumAbsenceMarkers);
        AddInt(value.Limits.Execution.MaximumOperations); AddInt(value.Limits.Execution.MaximumScopeDirectoryReads); AddInt(value.Limits.Execution.MaximumSlotReads); AddInt(value.Limits.Execution.MaximumActivationReads); AddInt(value.Limits.Execution.MaximumReadIntervals); AddInt(value.Limits.Execution.MaximumIndexOperations);
        AddLong(value.Limits.Execution.MaximumActivationBytes); AddLong(value.Limits.Execution.MaximumScopeDirectoryBytes); AddLong(value.Limits.Execution.MaximumEvidenceBytes); AddLong(value.Limits.Execution.MaximumReceiptBytes); AddLong(value.Limits.Execution.MaximumTransientBytes);
        AddLong(value.Limits.Deadlines.AcquisitionTimeout.Ticks); AddLong(value.Limits.Deadlines.TransactionTimeout.Ticks); AddLong(value.Limits.Deadlines.CommitObservationTimeout.Ticks); AddLong(value.Limits.Deadlines.ReceiptResolutionTimeout.Ticks); AddLong(value.Limits.Deadlines.MaintenanceTimeout.Ticks); AddLong(value.Limits.Deadlines.QuarantineRetentionTimeout.Ticks);
        switch (value.Compaction)
        {
            case BaseSemanticActivationNoCompaction: AddInt(0); break;
            case BaseSemanticActivationSubjectRetirementCompaction subject:
                AddInt(1); AddText(subject.SubjectContract.ContractId); AddInt(subject.SubjectContract.ContractVersion); hash.AppendData(subject.SubjectContract.ContractChecksum.AsSpan()); AddText(subject.SubjectReferenceRequestPropertyId); AddText(subject.LifecycleRetirementGrantId); break;
            default: throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        }
        return hash.GetHashAndReset();
    }

    private static void ValidateId(string value) => BaseApplicationId.Validate(value, nameof(value));

    private static BaseSemanticActivationCompactionContract SealCompaction(BaseSemanticActivationCompactionContract value) => value switch
    {
        BaseSemanticActivationNoCompaction => new BaseSemanticActivationNoCompaction(),
        BaseSemanticActivationSubjectRetirementCompaction subject when subject.SubjectContract.ContractVersion > 0 && subject.SubjectContract.ContractChecksum.Length == 32 => subject with
        {
            SubjectContract = subject.SubjectContract with { ContractChecksum = subject.SubjectContract.ContractChecksum.ToArray().ToImmutableArray() },
        },
        _ => throw new InvalidOperationException("base.semanticActivation.contractInvalid"),
    };

    private static void ValidateLimits(BaseSemanticActivationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaximumCanonicalKeyBytes is < 1 or > 1024 || limits.MaximumLiveSlots < 1 || limits.MaximumRetiredSlots < 1 || limits.MaximumAbsenceMarkers < 1
            || limits.Execution.MaximumOperations < 1 || limits.Execution.MaximumScopeDirectoryReads < 1 || limits.Execution.MaximumSlotReads < 1 || limits.Execution.MaximumActivationReads < 1
            || limits.Execution.MaximumReadIntervals < 1 || limits.Execution.MaximumIndexOperations < 1 || limits.Execution.MaximumActivationBytes < 1 || limits.Execution.MaximumScopeDirectoryBytes < 1
            || limits.Execution.MaximumEvidenceBytes < 1 || limits.Execution.MaximumReceiptBytes < 1 || limits.Execution.MaximumTransientBytes < 1
            || limits.Deadlines.AcquisitionTimeout <= TimeSpan.Zero || limits.Deadlines.TransactionTimeout <= TimeSpan.Zero || limits.Deadlines.CommitObservationTimeout <= TimeSpan.Zero
            || limits.Deadlines.ReceiptResolutionTimeout <= TimeSpan.Zero || limits.Deadlines.MaintenanceTimeout <= TimeSpan.Zero || limits.Deadlines.QuarantineRetentionTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
    }
}
