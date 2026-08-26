using System.Collections.Immutable;

namespace HPD.Base;

internal sealed class BaseModuleMutationRegistry
{
    private readonly Dictionary<(string Id, int Version), BaseRegisteredModuleMutationDefinition> _operations;
    private readonly Dictionary<string, BaseModuleGenerationCellDefinition> _cells;
    private readonly Dictionary<(string Id, int Version), IBaseModuleMutationRegistration> _registrations;

    internal BaseModuleMutationRegistry(
        IEnumerable<BaseRegisteredModuleMutationDefinition> operations,
        IEnumerable<BaseModuleGenerationCellDefinition> cells,
        IEnumerable<IBaseModuleMutationRegistration>? registrations = null)
    {
        _operations = operations.ToDictionary(static value => (value.Id, value.Version));
        _cells = cells.ToDictionary(static value => value.Id, StringComparer.Ordinal);
        _registrations = (registrations ?? []).ToDictionary(static value => (value.Id, value.Version));
    }

    internal BaseRegisteredModuleMutationDefinition? Find(string id, int version) => _operations.GetValueOrDefault((id, version));
    internal BaseModuleGenerationCellDefinition? FindCell(string id) => _cells.GetValueOrDefault(id);
    internal IReadOnlyCollection<BaseRegisteredModuleMutationDefinition> Operations => _operations.Values;
    internal IReadOnlyCollection<BaseModuleGenerationCellDefinition> Cells => _cells.Values;
    internal IBaseModuleMutationRegistration? FindRegistration(string id, int version) => _registrations.GetValueOrDefault((id, version));
    internal IReadOnlyCollection<IBaseModuleMutationRegistration> Registrations => _registrations.Values;
}

internal interface IBaseModuleMutationRegistration
{
    string Id { get; }
    int Version { get; }
    BaseModuleMutationAudience Audience { get; }
    string GrantId { get; }
    string RequestTypeId { get; }
    string ResultTypeId { get; }
    System.Text.Json.Serialization.Metadata.JsonTypeInfo RequestTypeInfo { get; }
    System.Text.Json.Serialization.Metadata.JsonTypeInfo ResultTypeInfo { get; }
    IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> RequestBindings { get; }
    IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> ResultBindings { get; }
    IReadOnlyList<BaseSerializerPropertyDeclaration> SerializerDeclarations { get; }
    BaseMutationRequestIdentity CreateRequestIdentity(
        ReadOnlyMemory<byte> requestJson, string idempotencyKey, PrincipalContext principal);
    ValueTask<BaseResult<BaseUntypedModuleMutationExecutionResult>> ExecuteAsync(
        BaseSession session, ReadOnlyMemory<byte> requestJson, BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options, CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseUntypedModuleMutationExecutionResult>> ExecuteTransactionalAsync(
        BaseSession session, ReadOnlyMemory<byte> requestJson, BaseMutationRequestIdentity identity,
        BaseTransactionalActivationCandidate activation, CancellationToken cancellationToken);
}

internal sealed record BaseUntypedModuleMutationExecutionResult
{
    internal required BaseMutationRequestDisposition Disposition { get; init; }
    internal required BaseModuleMutationOutcome Outcome { get; init; }
    internal required byte[] CanonicalResultJson { get; init; }
}

internal sealed class BaseModuleMutationRegistration<TRequest, TResult>(
    BaseRegisteredModuleMutationDefinition definition,
    BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity) : IBaseModuleMutationRegistration
{
    public string Id => definition.Id;
    public int Version => definition.Version;
    public BaseModuleMutationAudience Audience => definition.Audience;
    public string GrantId => definition.GrantId;
    public string RequestTypeId => definition.RequestTypeId;
    public string ResultTypeId => definition.ResultTypeId;
    public System.Text.Json.Serialization.Metadata.JsonTypeInfo RequestTypeInfo => identity.RequestTypeInfo;
    public System.Text.Json.Serialization.Metadata.JsonTypeInfo ResultTypeInfo => identity.ResultTypeInfo;
    public IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> RequestBindings => identity.RequestBindings;
    public IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> ResultBindings => identity.ResultBindings;
    public IReadOnlyList<BaseSerializerPropertyDeclaration> SerializerDeclarations => identity.SerializerDeclarations;

    public BaseMutationRequestIdentity CreateRequestIdentity(
        ReadOnlyMemory<byte> requestJson, string idempotencyKey, PrincipalContext principal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(principal);
        TRequest? request = System.Text.Json.JsonSerializer.Deserialize(requestJson.Span, identity.RequestTypeInfo);
        if (request is null) throw new InvalidOperationException("base.moduleMutation.invalid");
        byte[] canonical = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request, identity.RequestTypeInfo);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        Add(hash, "base.moduleMutation.http.v1"u8); Add(hash, System.Text.Encoding.UTF8.GetBytes(Id));
        Span<byte> version = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(version, Version); Add(hash, version);
        Add(hash, canonical); Add(hash, System.Text.Encoding.UTF8.GetBytes(principal.SubjectId ?? string.Empty));
        Add(hash, System.Text.Encoding.UTF8.GetBytes(principal.CurrentTenantId ?? string.Empty));
        string scope = $"module:{Id}|tenant:{principal.CurrentTenantId ?? string.Empty}";
        return BaseMutationRequestIdentity.Create(scope, Id, idempotencyKey,
            BaseMutationRequestFingerprint.Create(hash.GetHashAndReset()));
    }

    public async ValueTask<BaseResult<BaseUntypedModuleMutationExecutionResult>> ExecuteAsync(
        BaseSession session, ReadOnlyMemory<byte> requestJson, BaseMutationRequestIdentity requestIdentity,
        BaseModuleMutationExecutionOptions? options, CancellationToken cancellationToken)
    {
        TRequest? request;
        try { request = System.Text.Json.JsonSerializer.Deserialize(requestJson.Span, identity.RequestTypeInfo); }
        catch { return Failure(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation); }
        if (request is null) return Failure(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation);
        BaseResult<BaseModuleMutationExecutionResult<TResult>> result = await session.ModuleMutations.Get(identity)
            .ExecuteAsync(request, requestIdentity, options, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<TResult>> failure)
            return new BaseFailure<BaseUntypedModuleMutationExecutionResult>(failure.Status, failure.Error, failure.Warnings, failure.Diagnostics);
        var success = (BaseSuccess<BaseModuleMutationExecutionResult<TResult>>)result;
        byte[] json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(success.Value.Result, identity.ResultTypeInfo);
        return new BaseSuccess<BaseUntypedModuleMutationExecutionResult>(new BaseUntypedModuleMutationExecutionResult
        {
            Disposition = success.Value.Disposition,
            Outcome = success.Value.Outcome,
            CanonicalResultJson = json,
        }, success.Status, success.Warnings, success.Revision, success.Events, success.Diagnostics);
    }

    public async ValueTask<BaseResult<BaseUntypedModuleMutationExecutionResult>> ExecuteTransactionalAsync(
        BaseSession session,
        ReadOnlyMemory<byte> requestJson,
        BaseMutationRequestIdentity requestIdentity,
        BaseTransactionalActivationCandidate activation,
        CancellationToken cancellationToken)
    {
        TRequest? request;
        try { request = System.Text.Json.JsonSerializer.Deserialize(requestJson.Span, identity.RequestTypeInfo); }
        catch { return Failure(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation); }
        if (request is null) return Failure(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation);
        if (session.Services.GetService(typeof(IBaseModuleMutationRuntime)) is not DefaultBaseModuleMutationRuntime runtime)
            return Failure(OperationStatus.Unsupported, BaseModuleMutationErrorCodes.CapabilityMissing, ErrorCategory.Unsupported);
        BaseResult<BaseModuleMutationExecutionResult<TResult>> result = await runtime.ExecuteTransactionalAsync(
            session, definition, identity, request, requestIdentity, activation, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<TResult>> failure)
            return new BaseFailure<BaseUntypedModuleMutationExecutionResult>(failure.Status, failure.Error, failure.Warnings, failure.Diagnostics);
        var success = (BaseSuccess<BaseModuleMutationExecutionResult<TResult>>)result;
        byte[] json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(success.Value.Result, identity.ResultTypeInfo);
        return new BaseSuccess<BaseUntypedModuleMutationExecutionResult>(new BaseUntypedModuleMutationExecutionResult
        {
            Disposition = success.Value.Disposition,
            Outcome = success.Value.Outcome,
            CanonicalResultJson = json,
        }, success.Status, success.Warnings, success.Revision, success.Events, success.Diagnostics);
    }

    private static BaseFailure<BaseUntypedModuleMutationExecutionResult> Failure(OperationStatus status, string code, ErrorCategory category) =>
        new(status, new BaseError { Code = code, Message = "The module mutation request is invalid.", Category = category }, null, null);

    private static void Add(System.Security.Cryptography.IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length); hash.AppendData(value);
    }
}

/// <summary>Opaque generated identity for one typed registered module mutation.</summary>
public sealed class BaseGeneratedModuleMutationIdentity<TRequest, TResult> : IBaseSerializerMetadataSource
{
    private System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest>? _request;
    private System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult>? _result;
    private readonly IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> _requestBindings;
    private readonly IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> _resultBindings;

    internal BaseGeneratedModuleMutationIdentity(
        string id,
        int version,
        byte[] checksum,
        BaseSerializerContextRegistration registration,
        IReadOnlyList<BaseSerializerPropertyDeclaration> declarations,
        IReadOnlyList<BaseModuleDtoPropertyBinding> requestBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings)
    {
        Id = id;
        Version = version;
        Checksum = checksum;
        Registration = registration;
        Declarations = declarations;
        _requestBindings = FreezeBindings(requestBindings);
        _resultBindings = FreezeBindings(resultBindings);
    }
    internal BaseGeneratedModuleMutationIdentity(
        string id,
        int version,
        byte[] checksum,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> request,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> result,
        IReadOnlyList<BaseModuleDtoPropertyBinding> requestBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings)
    {
        Id = id;
        Version = version;
        Checksum = checksum.ToArray();
        Registration = null!;
        Declarations = [];
        _request = request;
        _result = result;
        _requestBindings = FreezeBindings(requestBindings);
        _resultBindings = FreezeBindings(resultBindings);
    }
    internal string Id { get; }
    internal int Version { get; }
    internal byte[] Checksum { get; }
    internal System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> RequestTypeInfo =>
        _request ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    internal System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> ResultTypeInfo =>
        _result ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    internal IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> RequestBindings => _requestBindings;
    internal IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> ResultBindings => _resultBindings;
    private BaseSerializerContextRegistration Registration { get; }
    internal IReadOnlyList<BaseSerializerPropertyDeclaration> SerializerDeclarations => Declarations;
    private IReadOnlyList<BaseSerializerPropertyDeclaration> Declarations { get; }
    IReadOnlyList<System.Text.Json.Serialization.Metadata.JsonTypeInfo> IBaseSerializerMetadataSource.Roots => [];
    bool IBaseSerializerMetadataSource.Generated => true;
    BaseSerializerContextRegistration? IBaseSerializerMetadataSource.Registration => Registration;
    IReadOnlyList<Type> IBaseSerializerMetadataSource.RootTypes => [typeof(TRequest), typeof(TResult)];
    IReadOnlyList<BaseSerializerPropertyDeclaration>? IBaseSerializerMetadataSource.SerializerDeclarations => Declarations;
    CollectionDefinition? IBaseSerializerMetadataSource.CollectionDefinition => null;
    void IBaseSerializerMetadataSource.Bind(BaseSerializerMetadataOwner owner)
    {
        _request = owner.Resolve(this, typeof(TRequest)) as System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest>
            ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
        _result = owner.Resolve(this, typeof(TResult)) as System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult>
            ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    }

    private static IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> FreezeBindings(
        IReadOnlyList<BaseModuleDtoPropertyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var result = new Dictionary<string, BaseModuleDtoPropertyBinding>(StringComparer.Ordinal);
        foreach (BaseModuleDtoPropertyBinding binding in bindings)
        {
            foreach (string edge in binding.StablePropertyPath) BaseApplicationId.Validate(edge, nameof(bindings));
            if (binding.DeclaringType is null
                || string.IsNullOrWhiteSpace(binding.ApplicationName)
                || !result.TryAdd(binding.PathKey, new BaseModuleDtoPropertyBinding(
                    binding.StablePropertyPath,
                    binding.DeclaringType,
                    binding.PropertyType,
                    binding.Confidentiality,
                    binding.RecordDisclosure,
                    binding.Manifest,
                    new string(binding.ApplicationName.AsSpan()),
                    binding.WirePropertyPath)))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        return result;
    }
}

/// <summary>Contains generator-emitted inert scalar metadata that Base seals into opaque module authority.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class BaseGeneratedModuleScalarManifest
{
    /// <summary>Binds one generated collection field to its exact persisted scalar authority.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public BaseField<TRecord, TValue> BindField<TRecord, TValue>(
        BaseField<TRecord, TValue> field, string collectionId, string fieldId)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (Kind is BaseModuleValueKind.Revision or BaseModuleValueKind.FrozenArray)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        BaseScalarKind scalarKind = (BaseScalarKind)(int)Kind;
        BaseScalarCodecAuthority codec = CodecQualifier is null
            ? BaseGeneratedSchemaRegistration.ScalarCodec(scalarKind)
            : BaseGeneratedSchemaRegistration.ScalarCodec(scalarKind, CodecQualifier);
        BaseScalarConstraintChecksum checksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum(
            collectionId, fieldId, Presence, Nullability, codec, Constraints);
        field.BindModuleMutation(BaseModuleValueAuthorityContract.Create(
            Kind, Presence, Nullability, codec, Constraints, checksum, RecordTargetCollectionId));
        return field;
    }
    /// <summary>Creates one generated request-property handle from this inert manifest.</summary>
    /// <typeparam name="TRequest">The generated request type.</typeparam>
    /// <typeparam name="TValue">The exact generated property type.</typeparam>
    /// <param name="stablePropertyPath">The generator-owned stable property path.</param>
    /// <returns>An opaque request-property handle.</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public BaseModuleRequestProperty<TRequest, TValue> RequestProperty<TRequest, TValue>(params string[] stablePropertyPath) =>
        new(Seal(stablePropertyPath));

    /// <summary>Creates one generated result-property handle from this inert manifest.</summary>
    /// <typeparam name="TResult">The generated result type.</typeparam>
    /// <typeparam name="TValue">The exact generated property type.</typeparam>
    /// <param name="stablePropertyPath">The generator-owned stable property path.</param>
    /// <returns>An opaque result-property handle.</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public BaseModuleResultProperty<TResult, TValue> ResultProperty<TResult, TValue>(params string[] stablePropertyPath) =>
        new(Seal(stablePropertyPath));

    /// <summary>Creates exact unbounded built-in scalar metadata for a trusted manual proving declaration.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseGeneratedModuleScalarManifest Primitive<TValue>(
        BaseFieldPresence presence = BaseFieldPresence.Required,
        BaseFieldNullability nullability = BaseFieldNullability.NonNullable)
    {
        Type actual = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        BaseModuleValueKind kind = actual == typeof(string) ? BaseModuleValueKind.String
            : actual == typeof(bool) ? BaseModuleValueKind.Boolean
            : actual == typeof(int) ? BaseModuleValueKind.Int32
            : actual == typeof(long) ? BaseModuleValueKind.Int64
            : actual == typeof(uint) ? BaseModuleValueKind.UInt32
            : actual == typeof(ulong) ? BaseModuleValueKind.UInt64
            : actual == typeof(decimal) ? BaseModuleValueKind.Decimal
            : actual == typeof(Guid) ? BaseModuleValueKind.Guid
            : actual == typeof(DateTimeOffset) ? BaseModuleValueKind.UtcDateTime
            : actual == typeof(BaseModuleGeneration) ? BaseModuleValueKind.ModuleGeneration
            : actual == typeof(RevisionToken) ? BaseModuleValueKind.Revision
            : throw new InvalidOperationException("base.moduleMutation.invalid");
        return new(kind, presence, nullability, new BaseScalarConstraintSet());
    }

    /// <summary>Initializes generator-emitted scalar metadata.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public BaseGeneratedModuleScalarManifest(
        BaseModuleValueKind kind,
        BaseFieldPresence presence,
        BaseFieldNullability nullability,
        BaseScalarConstraintSet constraints,
        string? codecQualifier = null,
        string? recordTargetCollectionId = null)
    {
        Kind = kind;
        Presence = presence;
        Nullability = nullability;
        Constraints = BaseModuleValueAuthorityContract.Clone(constraints ?? throw new ArgumentNullException(nameof(constraints)));
        CodecQualifier = codecQualifier is null ? null : new string(codecQualifier.AsSpan());
        RecordTargetCollectionId = recordTargetCollectionId is null ? null : new string(recordTargetCollectionId.AsSpan());
    }

    internal BaseModuleValueKind Kind { get; }
    internal BaseFieldPresence Presence { get; }
    internal BaseFieldNullability Nullability { get; }
    internal BaseScalarConstraintSet Constraints { get; }
    internal string? CodecQualifier { get; }
    internal string? RecordTargetCollectionId { get; }

    internal BaseModuleDtoScalarAuthority Seal(IReadOnlyList<string> stablePropertyPath)
    {
        if (Kind == BaseModuleValueKind.Revision)
        {
            BaseModuleValueType revision = BaseModuleValueAuthorityContract.Create(
                Kind, Presence, Nullability, null, null, null);
            return BaseModuleValueAuthorityContract.CreateDto(stablePropertyPath, revision);
        }
        if (Kind == BaseModuleValueKind.FrozenArray || (int)Kind is < 0 or > (int)BaseModuleValueKind.ModuleGeneration)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        BaseScalarKind scalarKind = (BaseScalarKind)(int)Kind;
        BaseScalarCodecAuthority codec = CodecQualifier is null
            ? BaseGeneratedSchemaRegistration.ScalarCodec(scalarKind)
            : BaseGeneratedSchemaRegistration.ScalarCodec(scalarKind, CodecQualifier);
        string fieldId = stablePropertyPath[^1];
        BaseScalarConstraintChecksum checksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum(
            "hpd.base.module.dto", fieldId, Presence, Nullability, codec, Constraints);
        BaseModuleValueType value = BaseModuleValueAuthorityContract.Create(
            Kind, Presence, Nullability, codec, Constraints, checksum, RecordTargetCollectionId);
        return BaseModuleValueAuthorityContract.CreateDto(stablePropertyPath, value);
    }
}

/// <summary>Binds one stable DTO property identity to exact graph-owned serializer metadata.</summary>
public sealed class BaseModuleDtoPropertyBinding
{
    internal BaseModuleDtoPropertyBinding(
        IReadOnlyList<string> stablePropertyPath,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] Type declaringType,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] Type? propertyType,
        BaseFieldConfidentiality confidentiality,
        BaseRecordDisclosure recordDisclosure,
        BaseGeneratedModuleScalarManifest manifest,
        string applicationName,
        IReadOnlyList<string>? wirePropertyPath = null)
    {
        StablePropertyPath = stablePropertyPath.Select(static edge => new string(edge.AsSpan())).ToArray();
        DeclaringType = declaringType;
        PropertyType = propertyType;
        Confidentiality = confidentiality;
        RecordDisclosure = recordDisclosure;
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        ScalarAuthority = Manifest.Seal(StablePropertyPath);
        ApplicationName = applicationName;
        WirePropertyPath = (wirePropertyPath ?? [applicationName]).Select(static edge => new string(edge.AsSpan())).ToArray();
        if (WirePropertyPath.Count != StablePropertyPath.Count || WirePropertyPath.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("base.moduleMutation.invalid");
    }

    /// <summary>Gets the globally stable property edge identity.</summary>
    public string StablePropertyId => StablePropertyPath[^1];
    internal IReadOnlyList<string> StablePropertyPath { get; }
    internal string PathKey => string.Join('\0', StablePropertyPath);
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    internal Type DeclaringType { get; }
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    internal Type? PropertyType { get; }
    /// <summary>Gets the exact L42 confidentiality class for this result edge.</summary>
    public BaseFieldConfidentiality Confidentiality { get; }
    /// <summary>Gets the exact installed L42 ordinary-record disclosure for this edge.</summary>
    public BaseRecordDisclosure RecordDisclosure { get; }
    internal BaseGeneratedModuleScalarManifest Manifest { get; }
    /// <summary>Gets the exact sealed scalar authority for this property.</summary>
    public BaseModuleDtoScalarAuthority ScalarAuthority { get; }
    /// <summary>Gets whether an explicitly present property may contain null.</summary>
    public BaseFieldNullability Nullability => ScalarAuthority.ValueType.Nullability;
    /// <summary>Gets whether the property may be absent.</summary>
    public BaseFieldPresence Presence => ScalarAuthority.ValueType.Presence;
    /// <summary>Gets the exact application property identity.</summary>
    public string ApplicationName { get; }
    /// <summary>Gets the exact frozen L44 wire-property path.</summary>
    public IReadOnlyList<string> WirePropertyPath { get; }

    /// <summary>Creates an exact opaque binding to one generated DTO property.</summary>
    public static BaseModuleDtoPropertyBinding Create<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TDeclaring,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TProperty>(
        string stablePropertyId,
        string applicationName,
        BaseGeneratedModuleScalarManifest manifest,
        BaseFieldConfidentiality confidentiality = BaseFieldConfidentiality.Public,
        BaseRecordDisclosure recordDisclosure = BaseRecordDisclosure.Include) =>
        new([stablePropertyId], typeof(TDeclaring), typeof(TProperty), confidentiality, recordDisclosure, manifest, applicationName, [applicationName]);

    /// <summary>Creates a generated binding with its exact frozen L44 wire name.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseModuleDtoPropertyBinding CreateWire<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TDeclaring,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TProperty>(
        string stablePropertyId, string applicationName, string wireName,
        BaseGeneratedModuleScalarManifest manifest,
        BaseFieldConfidentiality confidentiality = BaseFieldConfidentiality.Public,
        BaseRecordDisclosure recordDisclosure = BaseRecordDisclosure.Include) =>
        new([stablePropertyId], typeof(TDeclaring), typeof(TProperty), confidentiality, recordDisclosure, manifest, applicationName, [wireName]);

    /// <summary>Creates an exact opaque binding to one generated nested DTO property path.</summary>
    public static BaseModuleDtoPropertyBinding CreatePath<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TDeclaring,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TProperty>(
        IReadOnlyList<string> stablePropertyPath,
        string applicationName,
        BaseGeneratedModuleScalarManifest manifest,
        BaseFieldConfidentiality confidentiality = BaseFieldConfidentiality.Public,
        BaseRecordDisclosure recordDisclosure = BaseRecordDisclosure.Include) =>
        new(stablePropertyPath, typeof(TDeclaring), typeof(TProperty), confidentiality, recordDisclosure, manifest, applicationName, stablePropertyPath);

    /// <summary>Creates a generated nested binding with its exact frozen L44 wire path.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseModuleDtoPropertyBinding CreatePathWire<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TDeclaring,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TProperty>(
        IReadOnlyList<string> stablePropertyPath, string applicationName, IReadOnlyList<string> wirePropertyPath,
        BaseGeneratedModuleScalarManifest manifest,
        BaseFieldConfidentiality confidentiality = BaseFieldConfidentiality.Public,
        BaseRecordDisclosure recordDisclosure = BaseRecordDisclosure.Include) =>
        new(stablePropertyPath, typeof(TDeclaring), typeof(TProperty), confidentiality, recordDisclosure, manifest, applicationName, wirePropertyPath);
}

/// <summary>Infrastructure-only factory used by generated module mutation declarations.</summary>
public static class BaseGeneratedModuleMutations
{
    /// <summary>Creates one inert generated identity after generated contract validation.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseGeneratedModuleMutationIdentity<TRequest, TResult> Register<TRequest, TResult>(
        string id,
        int version,
        ReadOnlySpan<byte> checksum,
        BaseSerializerContextRegistration registration,
        IReadOnlyList<BaseSerializerPropertyDeclaration> declarations,
        IReadOnlyList<BaseModuleDtoPropertyBinding> requestBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(requestBindings);
        ArgumentNullException.ThrowIfNull(resultBindings);
        BaseApplicationId.Validate(id, nameof(id));
        if (version < 1 || checksum.Length != BaseModuleMutationChecksum.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        return new(new string(id.AsSpan()), version, checksum.ToArray(), registration, declarations.ToArray(), requestBindings.ToArray(), resultBindings.ToArray());
    }
}

internal static class BaseModuleMutationContractValidator
{
    internal static void ValidateCell(BaseModuleGenerationCellDefinition value)
    {
        BaseApplicationId.Validate(value.Id, nameof(value));
        BaseApplicationId.Validate(value.OwningModuleId, nameof(value));
        if (value.Version < 1 || !Enum.IsDefined(value.Scope)
            || value.MaximumKeyUtf8Bytes is < 1 or > 256
            || value.MaximumCellsPerOperation is < 1 or > 128)
            throw new InvalidOperationException("base.moduleMutation.invalid");
    }

    internal static void ValidateDefinition(
        BaseRegisteredModuleMutationDefinition value,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IReadOnlyDictionary<string, BaseModuleGenerationCellDefinition> cells,
        IBaseModuleMutationRegistration? registration = null)
    {
        BaseApplicationId.Validate(value.Id, nameof(value));
        BaseApplicationId.Validate(value.OwningModuleId, nameof(value));
        BaseApplicationId.Validate(value.GrantId, nameof(value));
        BaseApplicationId.Validate(value.RequestTypeId, nameof(value));
        BaseApplicationId.Validate(value.ResultTypeId, nameof(value));
        if (value.Version < 1 || !Enum.IsDefined(value.Audience)
            || value.ReceiptPolicy.FormatVersion != 1 || value.ReceiptPolicy.Lifetime <= TimeSpan.Zero
            || value.Checksum is null
            || !value.Checksum.Equals(BaseModuleMutationContract.ComputeChecksum(value)))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        string[] operationCollections = [.. value.SystemCollectionIds.Order(StringComparer.Ordinal)];
        if (!operationCollections.SequenceEqual(value.SystemCollectionIds, StringComparer.Ordinal)
            || operationCollections.Distinct(StringComparer.Ordinal).Count() != operationCollections.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        if (value.SystemSourceGrants.Length != operationCollections.Length
            || !value.SystemSourceGrants.Select(static source => source.CollectionId)
                .SequenceEqual(operationCollections, StringComparer.Ordinal)
            || value.SystemSourceGrants.Select(static source => source.CollectionId).Distinct(StringComparer.Ordinal).Count() != operationCollections.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (BaseModuleSystemSourceGrant source in value.SystemSourceGrants)
        {
            BaseApplicationId.Validate(source.CollectionId, nameof(value));
            BaseApplicationId.Validate(source.GrantId, nameof(value));
        }
        foreach (string id in operationCollections)
        {
            if (!collections.TryGetValue(id, out CollectionDefinition? collection)
                || !collection.System
                || !string.Equals(collection.SystemOwnerModuleId, value.OwningModuleId, StringComparison.Ordinal))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        string[] operationCells = [.. value.GenerationCellIds.Order(StringComparer.Ordinal)];
        if (!operationCells.SequenceEqual(value.GenerationCellIds, StringComparer.Ordinal)
            || operationCells.Distinct(StringComparer.Ordinal).Count() != operationCells.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (string id in operationCells)
        {
            if (!cells.TryGetValue(id, out BaseModuleGenerationCellDefinition? cell)
                || !string.Equals(cell.OwningModuleId, value.OwningModuleId, StringComparison.Ordinal))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        string[] subjects = [.. value.ImportedSubjectContractIds.Order(StringComparer.Ordinal)];
        if (!subjects.SequenceEqual(value.ImportedSubjectContractIds, StringComparer.Ordinal)
            || subjects.Distinct(StringComparer.Ordinal).Count() != subjects.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        ValidateLimits(value.Limits);
        ValidateTemplate(value.Template, value.Limits, collections, cells);
        if (registration is not null)
        {
            if (registration.RequestTypeId != value.RequestTypeId || registration.ResultTypeId != value.ResultTypeId)
                throw new InvalidOperationException("base.moduleMutation.invalid");
            ValidateGraphBindings(value.Template, collections, registration);
        }
    }

    private static void ValidateGraphBindings(
        BaseModuleMutationTemplate template,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IBaseModuleMutationRegistration registration)
    {
        Dictionary<string, BaseModuleRecordCapture> recordCaptures = template.Captures.OfType<BaseModuleRecordCapture>()
            .ToDictionary(static value => value.Id, StringComparer.Ordinal);
        foreach (BaseModuleValueExpression expression in Expressions(template))
        {
            if (expression is BaseModuleRequestPropertyExpression request)
            {
                string pathKey = string.Join('\0', request.Property.StablePropertyPath);
                if (!registration.RequestBindings.TryGetValue(pathKey, out BaseModuleDtoPropertyBinding? binding)
                    || request.Property.StablePropertyPath.Length is < 1 or > 16
                    || !request.Property.Authority.AuthorityChecksum.Equals(binding.ScalarAuthority.AuthorityChecksum)
                    || !BaseModuleValueAuthorityContract.StructurallyEquals(request.Property.Authority.ValueType, expression.ResultType)
                    || string.IsNullOrWhiteSpace(binding.ApplicationName)
                    || binding.PropertyType is null || !TypeMatches(binding.PropertyType, binding.ScalarAuthority.ValueType))
                    throw new InvalidOperationException("base.moduleMutation.invalid");
            }
            if (expression is BaseModuleCapturedFieldExpression captured)
                ValidateCapturedField(captured.Field);
            if (expression is BaseModuleBinaryNumericExpression numeric)
            {
                bool decimalOperation = numeric.Operator is BaseModuleNumericOperator.DecimalAddChecked
                    or BaseModuleNumericOperator.DecimalSubtractChecked or BaseModuleNumericOperator.DecimalMultiplyChecked;
                if (decimalOperation != (numeric.Decimal is not null)
                    || numeric.Decimal is { Precision: < 1 or > 38 }
                    || numeric.Decimal is { Scale: < 0 } decimalContext && decimalContext.Scale > decimalContext.Precision
                    || numeric.Decimal is not null && !Enum.IsDefined(numeric.Decimal.Rounding))
                    throw new InvalidOperationException("base.moduleMutation.invalid");
                if (!BaseModuleValueAuthorityContract.StructurallyEquals(numeric.Left.ResultType, numeric.Right.ResultType)
                    || !BaseModuleValueAuthorityContract.StructurallyEquals(numeric.ResultType, numeric.Left.ResultType))
                    throw new InvalidOperationException("base.moduleMutation.invalid");
            }
            if (expression is BaseModuleConditionalExpression conditional
                && (!BaseModuleValueAuthorityContract.StructurallyEquals(conditional.ResultType, conditional.WhenTrue.ResultType)
                    || !BaseModuleValueAuthorityContract.StructurallyEquals(conditional.ResultType, conditional.WhenFalse.ResultType)))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            if (expression is BaseModuleCoalesceExpression coalesce
                && coalesce.Values.Any(value => !SameUnderlyingType(coalesce.ResultType, value.ResultType)))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            if (expression is BaseModuleConstantExpression constant && !ConstantCanonicalMatches(constant))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }

        if (template.Result.Value.Properties.Length != registration.ResultBindings.Count
            || !template.Result.Value.Properties.Select(static property => property.StablePropertyId)
                .SequenceEqual(registration.ResultBindings.Values.Select(static binding => binding.StablePropertyId), StringComparer.Ordinal))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (BaseModuleObjectPropertyExpression property in template.Result.Value.Properties)
            if (!registration.ResultBindings.TryGetValue(property.StablePropertyId, out BaseModuleDtoPropertyBinding? resultBinding)
                || resultBinding.PropertyType is not null && (!TypeMatches(resultBinding.PropertyType, resultBinding.ScalarAuthority.ValueType)
                    || !BaseModuleValueAuthorityContract.ValueCompatible(property.Value.ResultType, resultBinding.ScalarAuthority.ValueType)))
                throw new InvalidOperationException("base.moduleMutation.invalid");

        var statements = new List<BaseModuleStatement>();
        Collect(template.Body, statements);
        foreach (BaseModuleStatement statement in statements)
        {
            BaseModuleValueExpression? recordId = statement switch
            {
                BaseModuleCreateStatement value => value.RecordId,
                BaseModulePatchStatement value => value.RecordId,
                BaseModuleReplaceStatement value => value.RecordId,
                BaseModuleDeleteStatement value => value.RecordId,
                BaseModuleUpsertStatement value => value.RecordId,
                _ => null,
            };
            BaseModuleValueExpression? revision = statement switch
            {
                BaseModulePatchStatement value => value.ExpectedRevision,
                BaseModuleReplaceStatement value => value.ExpectedRevision,
                BaseModuleDeleteStatement value => value.ExpectedRevision,
                BaseModuleUpsertStatement value => value.ExpectedRevision,
                _ => null,
            };
            if ((recordId is not null && recordId.ResultType?.Kind is not (BaseModuleValueKind.RecordId or BaseModuleValueKind.String))
                || (revision is not null && revision.ResultType?.Kind != BaseModuleValueKind.Revision))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            switch (statement)
            {
                case BaseModuleCreateStatement create: ValidatePayload(create.CollectionId, create.Payload, complete: true); break;
                case BaseModuleReplaceStatement replace: ValidatePayload(replace.CollectionId, replace.Payload, complete: true); break;
                case BaseModulePatchStatement patch: ValidatePayload(patch.CollectionId, patch.Patch, complete: false); break;
                case BaseModuleUpsertStatement upsert:
                    ValidatePayload(upsert.CollectionId, upsert.Create, complete: true);
                    ValidatePayload(upsert.CollectionId, upsert.Update, complete: upsert.UpdateMode == RecordUpsertUpdateMode.Replace);
                    break;
            }
        }

        void ValidateCapturedField(BaseModuleCapturedFieldReference reference)
        {
            if (!recordCaptures.TryGetValue(reference.CaptureId, out BaseModuleRecordCapture? capture)
                || !collections.TryGetValue(capture.CollectionId, out CollectionDefinition? collection)
                || collection.Fields?.SingleOrDefault(field => field.Id == reference.StableFieldId) is not { } field
                || !BaseModuleValueAuthorityContract.StructurallyEquals(BaseModuleValueAuthorityContract.FromField(field), reference.Authority))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }

        void ValidatePayload(string collectionId, BaseModuleObjectExpression payload, bool complete)
        {
            if (!collections.TryGetValue(collectionId, out CollectionDefinition? collection) || collection.Fields is null)
                throw new InvalidOperationException("base.moduleMutation.invalid");
            HashSet<string> supplied = payload.Properties.Select(static value => value.StablePropertyId).ToHashSet(StringComparer.Ordinal);
            string[] expectedOrder = collection.Fields.Where(field => supplied.Contains(field.Id)).Select(static field => field.Id).ToArray();
            if (supplied.Count != payload.Properties.Length
                || !payload.Properties.Select(static property => property.StablePropertyId).SequenceEqual(expectedOrder, StringComparer.Ordinal)
                || payload.Properties.Any(property => !collection.Fields.Any(field => field.Id == property.StablePropertyId
                    && !field.ReadOnly && BaseModuleValueAuthorityContract.ValueCompatible(
                        property.Value.ResultType, BaseModuleValueAuthorityContract.FromField(field))))
                || complete && collection.Fields.Any(field => field.Presence == BaseFieldPresence.Required && !field.ReadOnly && !supplied.Contains(field.Id)))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }

        static bool TypeMatches(Type type, BaseModuleValueType authority)
        {
            Type? nullable = Nullable.GetUnderlyingType(type);
            Type actual = nullable ?? type;
            if ((nullable is not null) != (authority.Nullability == BaseFieldNullability.Nullable) && actual.IsValueType) return false;
            return authority.Kind switch
            {
                BaseModuleValueKind.String => actual == typeof(string),
                BaseModuleValueKind.Boolean => actual == typeof(bool),
                BaseModuleValueKind.Int32 => actual == typeof(int),
                BaseModuleValueKind.Int64 => actual == typeof(long),
                BaseModuleValueKind.UInt32 => actual == typeof(uint),
                BaseModuleValueKind.UInt64 => actual == typeof(ulong),
                BaseModuleValueKind.Decimal => actual == typeof(decimal),
                BaseModuleValueKind.Guid => actual == typeof(Guid),
                BaseModuleValueKind.UtcDateTime => actual == typeof(DateTimeOffset),
                BaseModuleValueKind.Binary => actual == typeof(BaseBinary),
                BaseModuleValueKind.CanonicalJson => actual == typeof(BaseCanonicalJson),
                BaseModuleValueKind.ClosedEnum => actual.IsEnum,
                BaseModuleValueKind.ModuleGeneration => actual == typeof(BaseModuleGeneration),
                BaseModuleValueKind.Revision => actual == typeof(RevisionToken),
                BaseModuleValueKind.RecordId => actual == typeof(RecordId)
                    || actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(BaseRecordId<>),
                _ => false,
            };
        }

        static bool SameUnderlyingType(BaseModuleValueType? left, BaseModuleValueType? right) =>
            left is not null && right is not null && left.Kind == right.Kind
            && left.RecordTargetCollectionId == right.RecordTargetCollectionId
            && left.Codec?.CodecChecksum.Equals(right.Codec?.CodecChecksum) == true
            && left.ConstraintChecksum?.Equals(right.ConstraintChecksum) == true;

    }

    private static bool ConstantCanonicalMatches(BaseModuleConstantExpression value)
    {
        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(value.CanonicalBaseJson.ToArray());
            if (value.ResultType is not { } authority) return false;
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Null)
                return authority.Nullability == BaseFieldNullability.Nullable;
            if (authority.Kind == BaseModuleValueKind.Revision)
                return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.String
                    && TryRevision(document.RootElement.GetString());
            if (authority.Kind == BaseModuleValueKind.ModuleGeneration)
                return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.String
                    && TryGeneration(document.RootElement.GetString());
            var field = new FieldDefinition
            {
                Id = "value", ApplicationName = "value", WireName = "value", Type = "value",
                Presence = authority.Presence, Nullability = authority.Nullability,
                ScalarKind = (BaseScalarKind)(int)authority.Kind,
                ScalarCodec = authority.Codec, ScalarConstraints = authority.Constraints,
                ScalarConstraintChecksum = authority.ConstraintChecksum,
            };
            return BaseCanonicalRecordValidator.Validate(field, document.RootElement) is null;
        }
        catch { return false; }

        static bool TryGeneration(string? text)
        {
            try { _ = BaseModuleGeneration.ParseCanonical(text ?? string.Empty); return true; }
            catch { return false; }
        }

        static bool TryRevision(string? text)
        {
            try { _ = new RevisionToken(text ?? string.Empty); return true; }
            catch { return false; }
        }
    }

    private static IEnumerable<BaseModuleValueExpression> Expressions(BaseModuleMutationTemplate template)
    {
        foreach (BaseModuleCapture capture in template.Captures)
            foreach (BaseModuleValueExpression value in capture switch
            {
                BaseModuleRecordCapture record => Walk(record.RecordId),
                BaseModuleGenerationCapture { Key: { } key } => Walk(key),
                _ => [],
            }) yield return value;
        foreach (BaseModuleGuard guard in template.Guards)
            foreach (BaseModuleValueExpression value in guard switch
            {
                BaseModuleRevisionEqualsGuard item => Walk(item.Expected),
                BaseModuleFieldEqualsGuard item => Walk(item.Expected).Prepend(new BaseModuleCapturedFieldExpression { Id = item.Id + ".field", ResultType = item.Field.Authority, Field = item.Field }),
                BaseModuleFieldComparisonGuard item => Walk(item.Expected).Prepend(new BaseModuleCapturedFieldExpression { Id = item.Id + ".field", ResultType = item.Field.Authority, Field = item.Field }),
                BaseModuleGenerationGuard { Expected: { } expected } => Walk(expected),
                _ => [],
            }) yield return value;
        var statements = new List<BaseModuleStatement>(); Collect(template.Body, statements);
        foreach (BaseModuleStatement statement in statements)
        {
            IEnumerable<BaseModuleValueExpression> roots = statement switch
            {
                BaseModuleCreateStatement item => [item.RecordId, item.Payload],
                BaseModulePatchStatement item => item.ExpectedRevision is null ? [item.RecordId, item.Patch] : [item.RecordId, item.Patch, item.ExpectedRevision],
                BaseModuleReplaceStatement item => item.ExpectedRevision is null ? [item.RecordId, item.Payload] : [item.RecordId, item.Payload, item.ExpectedRevision],
                BaseModuleDeleteStatement item => item.ExpectedRevision is null ? [item.RecordId] : [item.RecordId, item.ExpectedRevision],
                BaseModuleUpsertStatement item => item.ExpectedRevision is null ? [item.RecordId, item.Create, item.Update] : [item.RecordId, item.Create, item.Update, item.ExpectedRevision],
                _ => [],
            };
            foreach (BaseModuleValueExpression root in roots)
                foreach (BaseModuleValueExpression value in Walk(root)) yield return value;
        }
        foreach (BaseModuleValueExpression value in Walk(template.Result.Value)) yield return value;

        static IEnumerable<BaseModuleValueExpression> Walk(BaseModuleValueExpression root)
        {
            yield return root;
            IEnumerable<BaseModuleValueExpression> children = root switch
            {
                BaseModuleCoalesceExpression item => item.Values,
                BaseModuleConditionalExpression item => [item.WhenTrue, item.WhenFalse],
                BaseModuleBinaryNumericExpression item => [item.Left, item.Right],
                BaseModuleObjectExpression item => item.Properties.Select(static property => property.Value),
                _ => [],
            };
            foreach (BaseModuleValueExpression child in children)
                foreach (BaseModuleValueExpression value in Walk(child)) yield return value;
        }
    }

    internal static void ValidateLimits(BaseModuleMutationLimits value)
    {
        if (!Within(value.MaximumCaptures, 256)
            || !Within(value.MaximumRecordCaptures, 256)
            || !Within(value.MaximumRelationTargetCaptures, 512)
            || !Within(value.MaximumGenerationCaptures, 128)
            || !Within(value.MaximumRecordMutations, 256)
            || !Within(value.MaximumGenerationReads, 128)
            || !Within(value.MaximumGenerationComparisons, 128)
            || !Within(value.MaximumGenerationIncrements, 128)
            || !Within(value.MaximumGuardNodes, 1_024)
            || !Within(value.MaximumGuardDepth, 32)
            || !Within(value.MaximumStatements, 512)
            || !Within(value.MaximumBranches, 64)
            || !Within(value.MaximumExpressionNodes, 2_048)
            || !Within(value.MaximumReadIntervals, 1_024)
            || !Within(value.MaximumSubjectValidations, 1_024)
            || !Within(value.MaximumAuthorityReads, 2_048)
            || !Within(value.MaximumRelationChecks, 4_096)
            || !Within(value.MaximumUniqueConstraintChecks, 4_096)
            || !WithinBytes(value.MaximumRequestBytes, 1_048_576)
            || !WithinBytes(value.MaximumSelectedBytes, 16_777_216)
            || !WithinBytes(value.MaximumGenerationBytes, 1_048_576)
            || !WithinBytes(value.MaximumEvidenceBytes, 16_777_216)
            || !WithinBytes(value.MaximumWrittenBytes, 16_777_216)
            || !WithinBytes(value.MaximumFactBytes, 16_777_216)
            || !WithinBytes(value.MaximumJournalBytes, 16_777_216)
            || !WithinBytes(value.MaximumReceiptBytes, 16_777_216)
            || !WithinBytes(value.MaximumResultBytes, 1_048_576)
            || !WithinBytes(value.MaximumTransientBytes, 32_000_000)
            || value.Deadlines is null
            || !WithinDeadline(value.Deadlines.AcquisitionTimeout, TimeSpan.FromSeconds(5))
            || !WithinDeadline(value.Deadlines.TransactionTimeout, TimeSpan.FromSeconds(30))
            || !WithinDeadline(value.Deadlines.CommitObservationTimeout, TimeSpan.FromSeconds(30))
            || !WithinDeadline(value.Deadlines.ReceiptResolutionTimeout, TimeSpan.FromSeconds(30)))
            throw new InvalidOperationException("base.moduleMutation.invalid");

        static bool Within(int actual, int maximum) => actual is >= 1 && actual <= maximum;
        static bool WithinBytes(long actual, long maximum) => actual is >= 1 && actual <= maximum;
        static bool WithinDeadline(TimeSpan actual, TimeSpan maximum) => actual > TimeSpan.Zero && actual <= maximum;
    }

    private static void ValidateTemplate(
        BaseModuleMutationTemplate template,
        BaseModuleMutationLimits limits,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IReadOnlyDictionary<string, BaseModuleGenerationCellDefinition> cells)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Captures.Length > limits.MaximumCaptures || template.Guards.Length > limits.MaximumGuardNodes
            || template.Captures.OfType<BaseModuleRecordCapture>().Count() > limits.MaximumRecordCaptures
            || template.Captures.OfType<BaseModuleGenerationCapture>().Count() > limits.MaximumGenerationCaptures)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        if (template.Captures.Select(static value => value.Id).Distinct(StringComparer.Ordinal).Count() != template.Captures.Length
            || template.Guards.Select(static value => value.Id).Distinct(StringComparer.Ordinal).Count() != template.Guards.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        HashSet<string> captures = template.Captures.Select(static value => value.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> recordCaptures = template.Captures.OfType<BaseModuleRecordCapture>().Select(static value => value.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> generationCaptures = template.Captures.OfType<BaseModuleGenerationCapture>().Select(static value => value.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> guards = template.Guards.Select(static value => value.Id).ToHashSet(StringComparer.Ordinal);
        foreach (BaseModuleCapture capture in template.Captures)
        {
            BaseApplicationId.Validate(capture.Id, nameof(template));
            switch (capture)
            {
                case BaseModuleRecordCapture record when collections.ContainsKey(record.CollectionId) && Enum.IsDefined(record.Presence):
                    ValidateExpression(record.RecordId, captures, guards, captureKey: true, resultOnly: false, 1, limits, new());
                    break;
                case BaseModuleGenerationCapture generation when cells.ContainsKey(generation.CellId) && Enum.IsDefined(generation.Absence):
                    if (generation.Key is not null)
                        ValidateExpression(generation.Key, captures, guards, captureKey: true, resultOnly: false, 1, limits, new());
                    break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }
        foreach (BaseModuleLogicalGuard guard in template.Guards.OfType<BaseModuleLogicalGuard>())
            if (guard.ChildGuardIds.Any(id => !guards.Contains(id))) throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (BaseModuleGuard guard in template.Guards)
        {
            BaseApplicationId.Validate(guard.Id, nameof(template));
            switch (guard)
            {
                case BaseModuleRecordPresenceGuard value when recordCaptures.Contains(value.CaptureId): break;
                case BaseModuleRevisionEqualsGuard value when recordCaptures.Contains(value.CaptureId):
                    ValidateExpression(value.Expected, captures, guards, false, false, 1, limits, new()); break;
                case BaseModuleFieldEqualsGuard value when recordCaptures.Contains(value.Field.CaptureId):
                    ValidateExpression(value.Expected, captures, guards, false, false, 1, limits, new()); break;
                case BaseModuleFieldComparisonGuard value when recordCaptures.Contains(value.Field.CaptureId)
                    && Enum.IsDefined(value.Comparison)
                    && OrderedScalar(value.Field.Authority.Kind)
                    && BaseModuleValueAuthorityContract.StructurallyEquals(value.Field.Authority, value.Expected.ResultType):
                    ValidateExpression(value.Expected, captures, guards, false, false, 1, limits, new()); break;
                case BaseModuleFieldPresenceGuard value when recordCaptures.Contains(value.Field.CaptureId) && Enum.IsDefined(value.Test): break;
                case BaseModuleGenerationGuard value when generationCaptures.Contains(value.CaptureId) && Enum.IsDefined(value.Comparison):
                    if (value.Comparison == BaseModuleGenerationComparisonKind.MustEqual != (value.Expected is not null))
                        throw new InvalidOperationException("base.moduleMutation.invalid");
                    if (value.Expected is not null) ValidateExpression(value.Expected, captures, guards, false, false, 1, limits, new());
                    break;
                case BaseModuleSemanticActivationStateGuard value when Enum.IsDefined(value.Test): break;
                case BaseModuleLogicalGuard value when Enum.IsDefined(value.Kind)
                    && ((value.Kind is BaseModuleLogicalGuardKind.And or BaseModuleLogicalGuardKind.Or
                            && value.ChildGuardIds.Length is >= 2 and <= 64)
                        || (value.Kind == BaseModuleLogicalGuardKind.Not && value.ChildGuardIds.Length == 1)): break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }
        ValidateGuardCycles(template.Guards, guards, limits.MaximumGuardDepth);
        var statements = new List<BaseModuleStatement>();
        Collect(template.Body, statements);
        if (statements.Count > limits.MaximumStatements
            || statements.OfType<BaseModuleIfStatement>().Count() > limits.MaximumBranches
            || statements.Count(static value => value is BaseModuleCreateStatement or BaseModulePatchStatement
                or BaseModuleReplaceStatement or BaseModuleDeleteStatement or BaseModuleUpsertStatement) > limits.MaximumRecordMutations
            || statements.Select(static value => value.Id).Distinct(StringComparer.Ordinal).Count() != statements.Count)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        HashSet<string> expressionIds = new(StringComparer.Ordinal);
        HashSet<string> incrementedCaptures = new(StringComparer.Ordinal);
        foreach (BaseModuleStatement statement in statements)
        {
            BaseApplicationId.Validate(statement.Id, nameof(template));
            switch (statement)
            {
                case BaseModuleIfStatement value when guards.Contains(value.GuardId): break;
                case BaseModuleRequireStatement value when guards.Contains(value.GuardId):
                    BaseApplicationId.Validate(value.RequirementId, nameof(template)); break;
                case BaseModuleIncrementGenerationStatement value when generationCaptures.Contains(value.CaptureId)
                    && incrementedCaptures.Add(value.CaptureId): break;
                case BaseModuleCreateStatement value when collections.ContainsKey(value.CollectionId):
                    ValidateExpression(value.RecordId, captures, guards, false, false, 1, limits, expressionIds);
                    ValidateExpression(value.Payload, captures, guards, false, false, 1, limits, expressionIds); break;
                case BaseModulePatchStatement value when collections.ContainsKey(value.CollectionId):
                    ValidateWrite(value.RecordId, value.Patch, value.ExpectedRevision); break;
                case BaseModuleReplaceStatement value when collections.ContainsKey(value.CollectionId):
                    ValidateWrite(value.RecordId, value.Payload, value.ExpectedRevision); break;
                case BaseModuleDeleteStatement value when collections.ContainsKey(value.CollectionId):
                    ValidateExpression(value.RecordId, captures, guards, false, false, 1, limits, expressionIds);
                    if (value.ExpectedRevision is not null) ValidateExpression(value.ExpectedRevision, captures, guards, false, false, 1, limits, expressionIds); break;
                case BaseModuleUpsertStatement value when collections.ContainsKey(value.CollectionId) && Enum.IsDefined(value.UpdateMode):
                    ValidateExpression(value.RecordId, captures, guards, false, false, 1, limits, expressionIds);
                    ValidateExpression(value.Create, captures, guards, false, false, 1, limits, expressionIds);
                    ValidateExpression(value.Update, captures, guards, false, false, 1, limits, expressionIds);
                    if (value.ExpectedRevision is not null) ValidateExpression(value.ExpectedRevision, captures, guards, false, false, 1, limits, expressionIds); break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }
        List<(Dictionary<string, bool> Decisions, HashSet<string> Statements, HashSet<string> Generations)> paths = ExecutionPaths(template.Body);
        HashSet<string> resultStatements = statements.Where(static statement => statement is BaseModuleCreateStatement or BaseModulePatchStatement
            or BaseModuleReplaceStatement or BaseModuleDeleteStatement or BaseModuleUpsertStatement)
            .Select(static statement => statement.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> resultGenerations = statements.OfType<BaseModuleIncrementGenerationStatement>()
            .Select(static statement => statement.CaptureId).ToHashSet(StringComparer.Ordinal);
        ValidateExpression(template.Result.Value, captures, guards, false, true, 1, limits, expressionIds,
            resultStatements, resultGenerations);
        ValidateResultPaths(template.Result.Value, paths);

        void ValidateWrite(BaseModuleValueExpression id, BaseModuleObjectExpression payload, BaseModuleValueExpression? revision)
        {
            ValidateExpression(id, captures, guards, false, false, 1, limits, expressionIds);
            ValidateExpression(payload, captures, guards, false, false, 1, limits, expressionIds);
            if (revision is not null) ValidateExpression(revision, captures, guards, false, false, 1, limits, expressionIds);
        }

        List<(Dictionary<string, bool> Decisions, HashSet<string> Statements, HashSet<string> Generations)> ExecutionPaths(BaseModuleMutationBlock block)
        {
            List<(Dictionary<string, bool> Decisions, HashSet<string> Statements, HashSet<string> Generations)> current =
                [(new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(StringComparer.Ordinal))];
            foreach (BaseModuleStatement statement in block.Statements)
            {
                if (statement is BaseModuleIfStatement branch)
                {
                    var expanded = new List<(Dictionary<string, bool>, HashSet<string>, HashSet<string>)>();
                    foreach (var path in current)
                    {
                        Expand(path, branch.WhenTrue, branch.GuardId, true, expanded);
                        Expand(path, branch.WhenFalse, branch.GuardId, false, expanded);
                    }
                    current = expanded;
                    if (current.Count > 4_096) throw new InvalidOperationException("base.moduleMutation.invalid");
                }
                else foreach (var path in current) Assign(path, statement);
            }
            return current;

            void Expand(
                (Dictionary<string, bool> Decisions, HashSet<string> Statements, HashSet<string> Generations) source,
                BaseModuleMutationBlock selected,
                string guardId,
                bool decision,
                List<(Dictionary<string, bool>, HashSet<string>, HashSet<string>)> destination)
            {
                if (source.Decisions.TryGetValue(guardId, out bool existing) && existing != decision) return;
                foreach (var nested in ExecutionPaths(selected))
                {
                    var decisions = new Dictionary<string, bool>(source.Decisions, StringComparer.Ordinal) { [guardId] = decision };
                    bool compatible = true;
                    foreach ((string key, bool value) in nested.Decisions)
                        if (decisions.TryGetValue(key, out bool prior) && prior != value) { compatible = false; break; }
                        else decisions[key] = value;
                    if (!compatible) continue;
                    var assigned = new HashSet<string>(source.Statements, StringComparer.Ordinal); assigned.UnionWith(nested.Statements);
                    var generations = new HashSet<string>(source.Generations, StringComparer.Ordinal); generations.UnionWith(nested.Generations);
                    destination.Add((decisions, assigned, generations));
                }
            }

            static void Assign(
                (Dictionary<string, bool> Decisions, HashSet<string> Statements, HashSet<string> Generations) path,
                BaseModuleStatement statement)
            {
                if (statement is BaseModuleIncrementGenerationStatement increment) path.Generations.Add(increment.CaptureId);
                else if (statement is BaseModuleCreateStatement or BaseModulePatchStatement or BaseModuleReplaceStatement
                    or BaseModuleDeleteStatement or BaseModuleUpsertStatement) path.Statements.Add(statement.Id);
            }
        }

        void ValidateResultPaths(
            BaseModuleValueExpression expression,
            IReadOnlyList<(Dictionary<string, bool> Decisions, HashSet<string> Statements, HashSet<string> Generations)> applicable)
        {
            if (applicable.Count == 0) return;
            switch (expression)
            {
                case BaseModuleCommittedRecordIdExpression record when applicable.Any(path => !path.Statements.Contains(record.StatementId)):
                    throw new InvalidOperationException("base.moduleMutation.invalid");
                case BaseModuleCommittedRevisionExpression revision when applicable.Any(path => !path.Statements.Contains(revision.StatementId)):
                    throw new InvalidOperationException("base.moduleMutation.invalid");
                case BaseModuleCommittedUpsertDispositionExpression upsert when applicable.Any(path => !path.Statements.Contains(upsert.StatementId)):
                    throw new InvalidOperationException("base.moduleMutation.invalid");
                case BaseModuleResultingGenerationExpression generation when applicable.Any(path => !path.Generations.Contains(generation.CaptureId)):
                    throw new InvalidOperationException("base.moduleMutation.invalid");
                case BaseModuleConditionalExpression conditional:
                    ValidateResultPaths(conditional.WhenTrue, applicable.Where(path => !path.Decisions.TryGetValue(conditional.GuardId, out bool value) || value).ToArray());
                    ValidateResultPaths(conditional.WhenFalse, applicable.Where(path => !path.Decisions.TryGetValue(conditional.GuardId, out bool value) || !value).ToArray());
                    break;
                case BaseModuleCoalesceExpression coalesce:
                    foreach (BaseModuleValueExpression child in coalesce.Values) ValidateResultPaths(child, applicable);
                    break;
                case BaseModuleBinaryNumericExpression numeric:
                    ValidateResultPaths(numeric.Left, applicable); ValidateResultPaths(numeric.Right, applicable); break;
                case BaseModuleObjectExpression value:
                    foreach (BaseModuleObjectPropertyExpression property in value.Properties) ValidateResultPaths(property.Value, applicable);
                    break;
            }
        }
    }

    private static bool OrderedScalar(BaseModuleValueKind kind) => kind is BaseModuleValueKind.Int64
        or BaseModuleValueKind.Decimal or BaseModuleValueKind.UtcDateTime;

    private static void ValidateGuardCycles(ImmutableArray<BaseModuleGuard> values, HashSet<string> ids, int maximumDepth)
    {
        Dictionary<string, BaseModuleGuard> map = values.ToDictionary(static value => value.Id, StringComparer.Ordinal);
        foreach (string id in ids) Visit(id, [], 1);
        void Visit(string id, HashSet<string> active, int depth)
        {
            if (depth > maximumDepth || !active.Add(id)) throw new InvalidOperationException("base.moduleMutation.invalid");
            if (map[id] is BaseModuleLogicalGuard logical)
                foreach (string child in logical.ChildGuardIds) Visit(child, active, depth + 1);
            active.Remove(id);
        }
    }

    private static void ValidateExpression(
        BaseModuleValueExpression value,
        HashSet<string> captures,
        HashSet<string> guards,
        bool captureKey,
        bool resultOnly,
        int depth,
        BaseModuleMutationLimits limits,
        HashSet<string> expressionIds,
        HashSet<string>? definiteStatements = null,
        HashSet<string>? definiteGenerations = null)
    {
        if (value is null || depth > limits.MaximumGuardDepth
            || value is not BaseModuleObjectExpression && value.ResultType is null
            || value is BaseModuleObjectExpression && value.ResultType is not null)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        BaseApplicationId.Validate(value.Id, nameof(value));
        if (!expressionIds.Add(value.Id)) throw new InvalidOperationException("base.moduleMutation.invalid");
        if (captureKey && value is not (BaseModuleRequestPropertyExpression or BaseModuleConstantExpression))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        if (!resultOnly && value is BaseModuleCommittedRecordIdExpression or BaseModuleCommittedRevisionExpression
            or BaseModuleCommittedUpsertDispositionExpression or BaseModuleResultingGenerationExpression
            or BaseModuleSemanticActivationDispositionExpression or BaseModuleSemanticActivationIdExpression
            or BaseModuleSemanticActivationWasMaterializedExpression or BaseModuleSemanticActivationRetirementDispositionExpression)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        switch (value)
        {
            case BaseModuleRequestPropertyExpression request when !request.Property.StablePropertyPath.IsDefaultOrEmpty: break;
            case BaseModuleConstantExpression constant when !constant.CanonicalBaseJson.IsDefault
                && ConstantCanonicalMatches(constant): break;
            case BaseModuleCapturedRecordIdExpression captured when captures.Contains(captured.CaptureId): break;
            case BaseModuleCapturedRevisionExpression captured when captures.Contains(captured.CaptureId): break;
            case BaseModuleCapturedFieldExpression captured when captures.Contains(captured.Field.CaptureId): break;
            case BaseModuleCapturedGenerationExpression captured when captures.Contains(captured.CaptureId): break;
            case BaseModuleCommittedRecordIdExpression committed when resultOnly && definiteStatements?.Contains(committed.StatementId) == true: break;
            case BaseModuleCommittedRevisionExpression committed when resultOnly && definiteStatements?.Contains(committed.StatementId) == true: break;
            case BaseModuleCommittedUpsertDispositionExpression committed when resultOnly && definiteStatements?.Contains(committed.StatementId) == true: break;
            case BaseModuleResultingGenerationExpression generation when resultOnly
                && definiteGenerations?.Contains(generation.CaptureId) == true: break;
            case BaseModuleSemanticActivationDispositionExpression when resultOnly: break;
            case BaseModuleSemanticActivationIdExpression when resultOnly: break;
            case BaseModuleSemanticActivationWasMaterializedExpression when resultOnly: break;
            case BaseModuleSemanticActivationRetirementDispositionExpression when resultOnly: break;
            case BaseModuleCoalesceExpression coalesce when coalesce.Values.Length is >= 2 and <= 16:
                foreach (BaseModuleValueExpression child in coalesce.Values) Child(child); break;
            case BaseModuleConditionalExpression conditional when guards.Contains(conditional.GuardId):
                Child(conditional.WhenTrue); Child(conditional.WhenFalse); break;
            case BaseModuleBinaryNumericExpression numeric when Enum.IsDefined(numeric.Operator):
                Child(numeric.Left); Child(numeric.Right); break;
            case BaseModuleObjectExpression obj when obj.Properties.Length <= limits.MaximumExpressionNodes:
                if (obj.Properties.Select(static item => item.StablePropertyId).Distinct(StringComparer.Ordinal).Count() != obj.Properties.Length)
                    throw new InvalidOperationException("base.moduleMutation.invalid");
                foreach (BaseModuleObjectPropertyExpression property in obj.Properties) Child(property.Value); break;
            default: throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        if (expressionIds.Count > limits.MaximumExpressionNodes)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        void Child(BaseModuleValueExpression child) => ValidateExpression(child, captures, guards, false, resultOnly,
            depth + 1, limits, expressionIds, definiteStatements, definiteGenerations);
    }

    private static void Collect(BaseModuleMutationBlock block, List<BaseModuleStatement> output)
    {
        if (block.Statements.IsDefaultOrEmpty) throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (BaseModuleStatement statement in block.Statements)
        {
            output.Add(statement);
            if (statement is BaseModuleIfStatement branch) { Collect(branch.WhenTrue, output); Collect(branch.WhenFalse, output); }
        }
    }
}
