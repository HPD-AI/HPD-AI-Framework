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
                    binding.Nullable,
                    new string(binding.ApplicationName.AsSpan()))))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        return result;
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
        bool nullable,
        string applicationName)
    {
        StablePropertyPath = stablePropertyPath.Select(static edge => new string(edge.AsSpan())).ToArray();
        DeclaringType = declaringType;
        PropertyType = propertyType;
        Confidentiality = confidentiality;
        RecordDisclosure = recordDisclosure;
        Nullable = nullable;
        ApplicationName = applicationName;
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
    /// <summary>Gets whether the exact L44 property node permits null.</summary>
    public bool Nullable { get; }
    /// <summary>Gets the exact application property identity.</summary>
    public string ApplicationName { get; }

    /// <summary>Creates an exact opaque binding to one generated DTO property.</summary>
    public static BaseModuleDtoPropertyBinding Create<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TDeclaring,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TProperty>(
        string stablePropertyId,
        string applicationName,
        BaseFieldConfidentiality confidentiality = BaseFieldConfidentiality.Public,
        BaseRecordDisclosure recordDisclosure = BaseRecordDisclosure.Include,
        bool nullable = false) =>
        new([stablePropertyId], typeof(TDeclaring), typeof(TProperty), confidentiality, recordDisclosure, nullable, applicationName);

    /// <summary>Creates an exact opaque binding to one generated nested DTO property path.</summary>
    public static BaseModuleDtoPropertyBinding CreatePath<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TDeclaring,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TProperty>(
        IReadOnlyList<string> stablePropertyPath,
        string applicationName,
        BaseFieldConfidentiality confidentiality = BaseFieldConfidentiality.Public,
        BaseRecordDisclosure recordDisclosure = BaseRecordDisclosure.Include,
        bool nullable = false) =>
        new(stablePropertyPath, typeof(TDeclaring), typeof(TProperty), confidentiality, recordDisclosure, nullable, applicationName);
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
                    || request.Property.DeclaredTypeId != expression.ResultTypeId
                    || string.IsNullOrWhiteSpace(binding.ApplicationName)
                    || binding.PropertyType is null || !TypeMatches(binding.PropertyType, binding.Nullable, expression.ResultTypeId))
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
                if (!string.Equals(numeric.Left.ResultTypeId, numeric.Right.ResultTypeId, StringComparison.Ordinal)
                    || !string.Equals(numeric.ResultTypeId, numeric.Left.ResultTypeId, StringComparison.Ordinal))
                    throw new InvalidOperationException("base.moduleMutation.invalid");
            }
            if (expression is BaseModuleConditionalExpression conditional
                && (!string.Equals(conditional.ResultTypeId, conditional.WhenTrue.ResultTypeId, StringComparison.Ordinal)
                    || !string.Equals(conditional.ResultTypeId, conditional.WhenFalse.ResultTypeId, StringComparison.Ordinal)))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            if (expression is BaseModuleCoalesceExpression coalesce
                && coalesce.Values.Any(value => !SameUnderlyingType(coalesce.ResultTypeId, value.ResultTypeId)))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            if (expression is BaseModuleConstantExpression constant && !ConstantMatches(constant))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }

        if (template.Result.Value.Properties.Length != registration.ResultBindings.Count
            || !template.Result.Value.Properties.Select(static property => property.StablePropertyId)
                .SequenceEqual(registration.ResultBindings.Values.Select(static binding => binding.StablePropertyId), StringComparer.Ordinal))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (BaseModuleObjectPropertyExpression property in template.Result.Value.Properties)
            if (!registration.ResultBindings.TryGetValue(property.StablePropertyId, out BaseModuleDtoPropertyBinding? resultBinding)
                || resultBinding.PropertyType is not null && !TypeMatches(resultBinding.PropertyType, resultBinding.Nullable, property.Value.ResultTypeId))
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
            if ((recordId is not null && recordId.ResultTypeId is not ("id" or "string"))
                || (revision is not null && revision.ResultTypeId != "revision"))
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
                || field.Type != reference.DeclaredTypeId)
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
                    && !field.ReadOnly && field.Type == property.Value.ResultTypeId))
                || complete && collection.Fields.Any(field => field.Required && !field.ReadOnly && !supplied.Contains(field.Id)))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }

        static bool TypeMatches(Type type, bool nullableNode, string typeId)
        {
            Type? nullable = Nullable.GetUnderlyingType(type);
            Type actual = nullable ?? type;
            bool declaredNullable = typeId.EndsWith("?", StringComparison.Ordinal);
            string node = declaredNullable ? typeId[..^1] : typeId;
            if (declaredNullable != nullableNode || actual.IsValueType && nullableNode != (nullable is not null)) return false;
            if (actual == typeof(string)) return node == "string";
            if (actual == typeof(bool)) return node == "boolean";
            if (actual == typeof(long) || actual == typeof(int) || actual == typeof(short) || actual == typeof(byte))
                return node == "int64";
            if (actual == typeof(decimal)) return node == "decimal";
            if (actual == typeof(BaseModuleGeneration)) return node == "base.moduleGeneration";
            if (actual == typeof(RevisionToken)) return node == "revision";
            if (actual == typeof(RecordId) || actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(BaseRecordId<>))
                return node == "id";
            return false;
        }

        static bool SameUnderlyingType(string left, string right) =>
            string.Equals(left.TrimEnd('?'), right.TrimEnd('?'), StringComparison.Ordinal);

        static bool ConstantMatches(BaseModuleConstantExpression value)
        {
            try
            {
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(value.CanonicalBaseJson.ToArray());
                string node = value.ResultTypeId.TrimEnd('?');
                return document.RootElement.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Null => value.ResultTypeId.EndsWith("?", StringComparison.Ordinal),
                    System.Text.Json.JsonValueKind.String when node == "base.moduleGeneration" =>
                        TryGeneration(document.RootElement.GetString()),
                    System.Text.Json.JsonValueKind.String => node is "string" or "id" or "revision",
                    System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => node == "boolean",
                    System.Text.Json.JsonValueKind.Number when node == "int64" => document.RootElement.TryGetInt64(out _),
                    System.Text.Json.JsonValueKind.Number when node == "decimal" => document.RootElement.TryGetDecimal(out _),
                    _ => false,
                };
            }
            catch { return false; }

            static bool TryGeneration(string? text)
            {
                try { _ = BaseModuleGeneration.ParseCanonical(text ?? string.Empty); return true; }
                catch { return false; }
            }
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
                BaseModuleFieldEqualsGuard item => Walk(item.Expected).Prepend(new BaseModuleCapturedFieldExpression { Id = item.Id + ".field", ResultTypeId = item.Field.DeclaredTypeId, Field = item.Field }),
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
                case BaseModuleFieldPresenceGuard value when recordCaptures.Contains(value.Field.CaptureId) && Enum.IsDefined(value.Test): break;
                case BaseModuleGenerationGuard value when generationCaptures.Contains(value.CaptureId) && Enum.IsDefined(value.Comparison):
                    if (value.Comparison == BaseModuleGenerationComparisonKind.MustEqual != (value.Expected is not null))
                        throw new InvalidOperationException("base.moduleMutation.invalid");
                    if (value.Expected is not null) ValidateExpression(value.Expected, captures, guards, false, false, 1, limits, new());
                    break;
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
        if (value is null || depth > limits.MaximumGuardDepth || string.IsNullOrWhiteSpace(value.ResultTypeId))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        BaseApplicationId.Validate(value.Id, nameof(value));
        if (!expressionIds.Add(value.Id)) throw new InvalidOperationException("base.moduleMutation.invalid");
        if (captureKey && value is not (BaseModuleRequestPropertyExpression or BaseModuleConstantExpression))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        if (!resultOnly && value is BaseModuleCommittedRecordIdExpression or BaseModuleCommittedRevisionExpression
            or BaseModuleCommittedUpsertDispositionExpression or BaseModuleResultingGenerationExpression)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        switch (value)
        {
            case BaseModuleRequestPropertyExpression request when !request.Property.StablePropertyPath.IsDefaultOrEmpty: break;
            case BaseModuleConstantExpression constant when !constant.CanonicalBaseJson.IsDefault: break;
            case BaseModuleCapturedRecordIdExpression captured when captures.Contains(captured.CaptureId): break;
            case BaseModuleCapturedRevisionExpression captured when captures.Contains(captured.CaptureId): break;
            case BaseModuleCapturedFieldExpression captured when captures.Contains(captured.Field.CaptureId): break;
            case BaseModuleCapturedGenerationExpression captured when captures.Contains(captured.CaptureId): break;
            case BaseModuleCommittedRecordIdExpression committed when resultOnly && definiteStatements?.Contains(committed.StatementId) == true: break;
            case BaseModuleCommittedRevisionExpression committed when resultOnly && definiteStatements?.Contains(committed.StatementId) == true: break;
            case BaseModuleCommittedUpsertDispositionExpression committed when resultOnly && definiteStatements?.Contains(committed.StatementId) == true: break;
            case BaseModuleResultingGenerationExpression generation when resultOnly
                && definiteGenerations?.Contains(generation.CaptureId) == true: break;
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
