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
    IReadOnlyList<BaseSerializerPropertyDeclaration> SerializerDeclarations { get; }
    BaseMutationRequestIdentity CreateRequestIdentity(
        ReadOnlyMemory<byte> requestJson, string idempotencyKey, PrincipalContext principal);
    ValueTask<BaseResult<BaseUntypedModuleMutationExecutionResult>> ExecuteAsync(
        BaseSession session, ReadOnlyMemory<byte> requestJson, BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options, CancellationToken cancellationToken);
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
            BaseApplicationId.Validate(binding.StablePropertyId, nameof(bindings));
            if (binding.DeclaringType is null
                || string.IsNullOrWhiteSpace(binding.ApplicationName)
                || !result.TryAdd(binding.StablePropertyId, new BaseModuleDtoPropertyBinding(
                    new string(binding.StablePropertyId.AsSpan()),
                    binding.DeclaringType,
                    new string(binding.ApplicationName.AsSpan()))))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        return result;
    }
}

/// <summary>Binds one stable DTO property identity to exact graph-owned serializer metadata.</summary>
public sealed class BaseModuleDtoPropertyBinding
{
    internal BaseModuleDtoPropertyBinding(string stablePropertyId, Type declaringType, string applicationName)
    {
        StablePropertyId = stablePropertyId;
        DeclaringType = declaringType;
        ApplicationName = applicationName;
    }

    /// <summary>Gets the globally stable property edge identity.</summary>
    public string StablePropertyId { get; }
    internal Type DeclaringType { get; }
    /// <summary>Gets the exact application property identity.</summary>
    public string ApplicationName { get; }

    /// <summary>Creates an opaque binding to one exact DTO property.</summary>
    public static BaseModuleDtoPropertyBinding Create<T>(
        string stablePropertyId,
        string applicationName) =>
        new(stablePropertyId, typeof(T), applicationName);
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
        IReadOnlyDictionary<string, BaseModuleGenerationCellDefinition> cells)
    {
        BaseApplicationId.Validate(value.Id, nameof(value));
        BaseApplicationId.Validate(value.OwningModuleId, nameof(value));
        BaseApplicationId.Validate(value.GrantId, nameof(value));
        BaseApplicationId.Validate(value.RequestTypeId, nameof(value));
        BaseApplicationId.Validate(value.ResultTypeId, nameof(value));
        if (value.Version < 1 || !Enum.IsDefined(value.Audience)
            || value.ReceiptPolicy.FormatVersion != 1 || value.ReceiptPolicy.Lifetime <= TimeSpan.Zero
            || value.Checksum is null)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        string[] operationCollections = [.. value.SystemCollectionIds.Order(StringComparer.Ordinal)];
        if (!operationCollections.SequenceEqual(value.SystemCollectionIds, StringComparer.Ordinal)
            || operationCollections.Distinct(StringComparer.Ordinal).Count() != operationCollections.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
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
    }

    private static void ValidateLimits(BaseModuleMutationLimits value)
    {
        int[] counts =
        [
            value.MaximumCaptures, value.MaximumRecordCaptures, value.MaximumRelationTargetCaptures,
            value.MaximumGenerationCaptures, value.MaximumRecordMutations, value.MaximumGenerationReads,
            value.MaximumGenerationComparisons, value.MaximumGenerationIncrements, value.MaximumGuardNodes,
            value.MaximumGuardDepth, value.MaximumStatements, value.MaximumBranches, value.MaximumExpressionNodes,
            value.MaximumReadIntervals, value.MaximumSubjectValidations, value.MaximumAuthorityReads,
            value.MaximumRelationChecks, value.MaximumUniqueConstraintChecks,
        ];
        long[] bytes =
        [
            value.MaximumRequestBytes, value.MaximumSelectedBytes, value.MaximumGenerationBytes,
            value.MaximumEvidenceBytes, value.MaximumWrittenBytes, value.MaximumFactBytes,
            value.MaximumJournalBytes, value.MaximumReceiptBytes, value.MaximumResultBytes,
            value.MaximumTransientBytes,
        ];
        if (counts.Any(static item => item is < 1 or > 1_000_000)
            || bytes.Any(static item => item is < 1 or > 1_073_741_824)
            || value.Deadlines is null
            || new[] { value.Deadlines.AcquisitionTimeout, value.Deadlines.TransactionTimeout,
                value.Deadlines.CommitObservationTimeout, value.Deadlines.ReceiptResolutionTimeout }
                .Any(static item => item < TimeSpan.FromMilliseconds(10) || item > TimeSpan.FromMinutes(5)))
            throw new InvalidOperationException("base.moduleMutation.invalid");
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
                case BaseModuleRecordPresenceGuard value when captures.Contains(value.CaptureId): break;
                case BaseModuleRevisionEqualsGuard value when captures.Contains(value.CaptureId):
                    ValidateExpression(value.Expected, captures, guards, false, false, 1, limits, new()); break;
                case BaseModuleFieldEqualsGuard value when captures.Contains(value.Field.CaptureId):
                    ValidateExpression(value.Expected, captures, guards, false, false, 1, limits, new()); break;
                case BaseModuleFieldPresenceGuard value when captures.Contains(value.Field.CaptureId) && Enum.IsDefined(value.Test): break;
                case BaseModuleGenerationGuard value when captures.Contains(value.CaptureId) && Enum.IsDefined(value.Comparison):
                    if (value.Comparison == BaseModuleGenerationComparisonKind.MustEqual != (value.Expected is not null))
                        throw new InvalidOperationException("base.moduleMutation.invalid");
                    if (value.Expected is not null) ValidateExpression(value.Expected, captures, guards, false, false, 1, limits, new());
                    break;
                case BaseModuleLogicalGuard value when Enum.IsDefined(value.Kind)
                    && value.ChildGuardIds.Length is >= 1 and <= 16: break;
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
        foreach (BaseModuleStatement statement in statements)
        {
            BaseApplicationId.Validate(statement.Id, nameof(template));
            switch (statement)
            {
                case BaseModuleIfStatement value when guards.Contains(value.GuardId): break;
                case BaseModuleRequireStatement value when guards.Contains(value.GuardId):
                    BaseApplicationId.Validate(value.RequirementId, nameof(template)); break;
                case BaseModuleIncrementGenerationStatement value when captures.Contains(value.CaptureId): break;
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
        ValidateExpression(template.Result.Value, captures, guards, false, true, 1, limits, expressionIds);

        void ValidateWrite(BaseModuleValueExpression id, BaseModuleObjectExpression payload, BaseModuleValueExpression? revision)
        {
            ValidateExpression(id, captures, guards, false, false, 1, limits, expressionIds);
            ValidateExpression(payload, captures, guards, false, false, 1, limits, expressionIds);
            if (revision is not null) ValidateExpression(revision, captures, guards, false, false, 1, limits, expressionIds);
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
        HashSet<string> expressionIds)
    {
        if (value is null || depth > limits.MaximumGuardDepth || string.IsNullOrWhiteSpace(value.ResultTypeId))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        BaseApplicationId.Validate(value.Id, nameof(value));
        expressionIds.Add(value.Id);
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
            case BaseModuleCommittedRecordIdExpression when resultOnly: break;
            case BaseModuleCommittedRevisionExpression when resultOnly: break;
            case BaseModuleCommittedUpsertDispositionExpression when resultOnly: break;
            case BaseModuleResultingGenerationExpression generation when resultOnly && captures.Contains(generation.CaptureId): break;
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
        void Child(BaseModuleValueExpression child) => ValidateExpression(child, captures, guards, false, resultOnly, depth + 1, limits, expressionIds);
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
