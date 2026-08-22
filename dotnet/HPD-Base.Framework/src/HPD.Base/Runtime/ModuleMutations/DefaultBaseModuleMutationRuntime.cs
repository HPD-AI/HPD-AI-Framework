using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed class DefaultBaseModuleMutationRuntime(
    IRecordStoreRegistry stores,
    BaseCollectionRegistry collections,
    BaseModuleMutationRegistry registry,
    IBaseSchemaValidator schemaValidator,
    IBasePolicyOrchestrator policy,
    IBaseResultNormalizer normalizer,
    BaseSubjectContractRegistry subjects,
    TimeProvider timeProvider,
    BaseSubjectLifecycleRegistry? lifecycleRegistry = null,
    BaseSubjectRetirementRegistry? retirementRegistry = null) : IBaseModuleMutationRuntime
{
    private readonly BaseSubjectLifecycleRegistry lifecycleConsumers = lifecycleRegistry ?? new([], subjects);
    private readonly BaseSubjectRetirementRegistry retirement = retirementRegistry ?? new([], [], lifecycleRegistry ?? new([], subjects));
    public ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(session, definition, generatedIdentity, request, identity, options, null, cancellationToken);

    internal ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteTransactionalAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseTransactionalActivationCandidate activation,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(session, definition, generatedIdentity, request, identity, null, activation, cancellationToken);

    private async ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteCoreAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options,
        BaseTransactionalActivationCandidate? transactionalActivation,
        CancellationToken cancellationToken)
    {
        if (!AudienceAllowed(session, definition)
            || options?.MaximumWait is { } wait && (wait <= TimeSpan.Zero || wait > definition.Limits.Deadlines.CommitObservationTimeout))
            return Failure<TResult>(OperationStatus.PolicyDenied, BaseModuleMutationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        OperationContext moduleOperation = session.Operation(BaseOperationKind.ModuleMutation, definition.Id);
        CollectionDefinition policyResource = new()
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, System = true,
                SystemOwnerModuleId = definition.OwningModuleId,
            };
        OperationResult<BasePolicyEvaluation> operationPolicy = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = session.Principal, Operation = moduleOperation, Collection = policyResource,
            ResourceKind = PolicyResourceKind.ModuleMutation,
        }, cancellationToken).ConfigureAwait(false);
        if (!operationPolicy.IsSuccess() || operationPolicy.Value?.Authority is null
            || !BaseSystemCollectionGate.HasExactModuleGrant(operationPolicy, definition.GrantId,
                definition.OwningModuleId, session.Principal, moduleOperation))
            return Failure<TResult>(OperationStatus.PolicyDenied, BaseModuleMutationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        if (!await AuthorizeDeclaredAuthorityAsync(
                session, definition, moduleOperation,
                cancellationToken).ConfigureAwait(false))
            return Failure<TResult>(OperationStatus.PolicyDenied, BaseModuleMutationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        byte[] requestBytes;
        try { requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, generatedIdentity.RequestTypeInfo); }
        catch { return Failure<TResult>(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation); }
        if (requestBytes.LongLength > definition.Limits.MaximumRequestBytes)
            return Failure<TResult>(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation);

        IReadOnlyDictionary<string, CollectionDefinition> installed = collections.Collections;
        var requestEvaluator = new BaseModuleProgramEvaluator<TRequest, TResult>(definition, generatedIdentity, request, null, installed);
        BaseModuleMutationCaptureExtension extension;
        CollectionDefinition[] authorityCollections;
        try
        {
            extension = BuildCaptureExtension(definition, requestEvaluator, session, registry, installed, requestBytes);
            authorityCollections = extension.Records.Select(static value => value.Collection)
                .Concat(extension.RelationTargets.Select(static value => value.TargetCollection))
                .Concat(definition.SystemCollectionIds.Select(id => installed[id]))
                .DistinctBy(static value => value.Id, StringComparer.Ordinal)
                .OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        }
        catch { return Failure<TResult>(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation); }

        IAtomicRecordStore? atomicStore = ResolveOneStore(authorityCollections);
        if (atomicStore is null || !BaseModuleMutationCapabilityContract.Supports(definition.Limits, atomicStore.Capabilities.ModuleMutation))
            return Failure<TResult>(OperationStatus.Unsupported, BaseModuleMutationErrorCodes.CapabilityMissing, ErrorCategory.Unsupported);
        BaseAtomicMutationExecutionLimits limits = ResolveExecutionLimits(definition.Limits);
        OperationResult<BaseAtomicMutationAuthorityRequirement> authority = await atomicStore
            .CaptureAtomicMutationAuthorityRequirementAsync(session.ApplicationId, [.. authorityCollections], limits, cancellationToken)
            .ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return Failure<TResult>(authority.Status, authority.Error ?? Error(BaseModuleMutationErrorCodes.AuthorityChanged, ErrorCategory.Conflict));

        string intentDigest = Digest("base.moduleMutation.intent.v1", extension.RequestDigest, authority.Value.ApplicationId);
        var intent = new BaseAtomicMutationIntent
        {
            IntentDigest = intentDigest,
            Authority = authority.Value,
            Items = [],
        };
        var processor = new BaseModuleMutationProcessor<TRequest, TResult>(
            definition, generatedIdentity, request, intent, extension, options?.ActivationGuard,
            options?.ActivationCreation, limits, installed,
            session.Principal, moduleOperation, operationPolicy.Value,
            schemaValidator, policy, normalizer, subjects, lifecycleConsumers, retirement, transactionalActivation);
        var executionRequest = new RecordMutationExecutionRequest
        {
            AcquisitionTimeout = definition.Limits.Deadlines.AcquisitionTimeout,
            TransactionTimeout = definition.Limits.Deadlines.TransactionTimeout,
            CommitCompletionTimeout = options?.MaximumWait ?? definition.Limits.Deadlines.CommitObservationTimeout,
            AtomicRequest = new BaseAtomicMutationExecutionRequest
            {
                Identity = identity,
                StructuralDigest = SHA256.HashData(Encoding.UTF8.GetBytes($"base.moduleMutation.receipt.v1\0{definition.Id}\0{definition.Version}\0{Convert.ToHexString(definition.Checksum.ToArray())}\0{Convert.ToHexString(requestBytes)}")),
                ExpiresAt = timeProvider.GetUtcNow().Add(definition.ReceiptPolicy.Lifetime),
                MaxReceiptBytes = checked((int)Math.Min(definition.Limits.MaximumReceiptBytes, int.MaxValue)),
            },
        };
        RecordMutationExecutionResult execution;
        try { execution = await atomicStore.ExecuteAtomicAsync(processor, executionRequest, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.Cancelled, ErrorCategory.Store); }
        catch { return Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.StoreError, ErrorCategory.Store); }
        if (execution.Outcome == RecordMutationExecutionOutcome.Indeterminate)
            return Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.CommitIndeterminate, ErrorCategory.Store);
        if (execution.Outcome != RecordMutationExecutionOutcome.Committed || processor.Result is null)
            return Failure<TResult>(execution.Processing?.Error is { } error ? OperationStatus.StoreError : OperationStatus.Conflict,
                execution.Processing?.Error ?? execution.Error ?? Error(BaseModuleMutationErrorCodes.GenerationConflict, ErrorCategory.Conflict));
        return new BaseSuccess<BaseModuleMutationExecutionResult<TResult>>(
            processor.Result with
            {
                Disposition = execution.RequestDisposition,
                Outcome = execution.RequestDisposition == BaseMutationRequestDisposition.Duplicate
                    ? BaseModuleMutationOutcome.Duplicate : BaseModuleMutationOutcome.Committed,
            },
            OperationStatus.Updated, null, null, null, null);
    }

    public async ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ResolveAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(generatedIdentity);
        ArgumentNullException.ThrowIfNull(identity);
        OperationResult<BasePolicyEvaluation> disclosure = await policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = session.Operation(BaseOperationKind.ModuleMutation, definition.Id),
            Collection = PolicyResource(definition),
            ResourceKind = PolicyResourceKind.ModuleMutation,
        }, cancellationToken).ConfigureAwait(false);
        if (!disclosure.IsSuccess() || disclosure.Value?.Authority is null)
            return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound);
        CollectionDefinition[] authorityCollections;
        try
        {
            authorityCollections = definition.SystemCollectionIds
                .Select(id => collections.Collections.Values.Single(value => string.Equals(value.Id, id, StringComparison.Ordinal)))
                .ToArray();
        }
        catch { return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound); }
        IAtomicRecordStore? store = ResolveOneStore(authorityCollections);
        if (store is null || !BaseModuleMutationCapabilityContract.Supports(definition.Limits, store.Capabilities.ModuleMutation))
            return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound);
        var resolver = new BaseModuleMutationReceiptResolver<TResult>(
            definition, generatedIdentity.ResultTypeInfo, generatedIdentity.ResultBindings,
            session.Principal, session.Operation(BaseOperationKind.ModuleMutation, definition.Id), policy);
        RecordMutationExecutionResult resolution;
        try
        {
            resolution = await store.ResolveAtomicReceiptAsync(
                resolver, identity, definition.Limits.Deadlines.ReceiptResolutionTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch { return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound); }
        if (resolution.Outcome != RecordMutationExecutionOutcome.Committed || resolver.Result is null)
            return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound);
        return new BaseSuccess<BaseModuleMutationExecutionResult<TResult>>(resolver.Result, OperationStatus.Ok, null, null, null, null);
    }

    private IAtomicRecordStore? ResolveOneStore(CollectionDefinition[] authorityCollections)
    {
        RecordStoreRegistration[] registrations = authorityCollections.Length == 0
            ? stores.GetRegistrations()
            : authorityCollections.Select(value => stores.GetRegistrationForCollection(value.Id)).Where(static value => value is not null).Cast<RecordStoreRegistration>().DistinctBy(static value => value.StoreId).ToArray();
        return registrations.Length == 1
            ? registrations[0].AtomicExecutionStore
                ?? registrations[0].Store as IAtomicRecordStore
            : null;
    }

    private async ValueTask<bool> AuthorizeDeclaredAuthorityAsync(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        OperationContext moduleOperation,
        CancellationToken cancellationToken)
    {
        foreach (BaseModuleSystemSourceGrant sourceGrant in definition.SystemSourceGrants)
        {
            string collectionId = sourceGrant.CollectionId;
            if (!collections.Collections.TryGetValue(collectionId, out CollectionDefinition? collection)) return false;
            OperationContext sourceOperation = moduleOperation with { CollectionId = collection.Id };
            OperationResult<BasePolicyEvaluation> source = await policy.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = session.Principal,
                Operation = sourceOperation,
                Collection = collection,
                ResourceKind = PolicyResourceKind.ModuleMutation,
            }, cancellationToken).ConfigureAwait(false);
            if (!BaseSystemCollectionGate.HasExactModuleSourceGrant(source, sourceGrant.GrantId,
                    definition.OwningModuleId, session.Principal, sourceOperation, collection.Id)) return false;
        }

        foreach (string contractId in definition.ImportedSubjectContractIds)
        {
            BaseGeneratedSubjectRegistration[] matches = subjects.All
                .Where(value => string.Equals(value.Definition.Id, contractId, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1) return false;
            BaseGeneratedSubjectRegistration registration = matches[0];
            OperationResult<BasePolicyEvaluation> imported = await policy.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = session.Principal,
                Operation = moduleOperation with
                {
                    Operation = BaseOperationKind.SubjectValidate,
                    CollectionId = registration.Definition.Id,
                    RecordId = null,
                    Mode = OperationMode.System,
                },
                Collection = new CollectionDefinition
                {
                    Id = registration.Definition.Id,
                    Name = "Exported logical subject contract",
                    Kind = BaseCollectionKinds.Custom,
                    SchemaMode = SchemaMode.Strict,
                    UnknownFields = UnknownFieldPolicy.Reject,
                    System = true,
                    SystemOwnerModuleId = registration.Definition.OwningModuleId,
                },
                ResourceKind = PolicyResourceKind.SubjectContract,
                SubjectContractId = registration.Definition.Id,
                SubjectContractVersion = registration.Definition.Version,
            }, cancellationToken).ConfigureAwait(false);
            if (!BaseSystemCollectionGate.HasExactGrant(imported, registration.Definition.ValidationGrantId)) return false;
        }
        return true;
    }

    private CollectionDefinition PolicyResource(BaseRegisteredModuleMutationDefinition definition) =>
        definition.SystemCollectionIds.Length > 0
        && collections.Collections.TryGetValue(definition.SystemCollectionIds[0], out CollectionDefinition? installed)
            ? installed
            : new CollectionDefinition
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, System = true,
                SystemOwnerModuleId = definition.OwningModuleId,
            };

    private static BaseModuleMutationCaptureExtension BuildCaptureExtension<TRequest, TResult>(
        BaseRegisteredModuleMutationDefinition definition,
        BaseModuleProgramEvaluator<TRequest, TResult> evaluator,
        BaseSession session,
        BaseModuleMutationRegistry registry,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        byte[] requestBytes)
    {
        var records = ImmutableArray.CreateBuilder<BaseModuleRecordCaptureRequest>();
        var generations = ImmutableArray.CreateBuilder<BaseModuleGenerationCaptureRequest>();
        var relations = ImmutableArray.CreateBuilder<BaseModuleRelationTargetCaptureRequest>();
        foreach (BaseModuleCapture capture in definition.Template.Captures.OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            if (capture is BaseModuleRecordCapture record)
            {
                BaseModuleProgramValue id = evaluator.Evaluate(record.RecordId);
                records.Add(new BaseModuleRecordCaptureRequest
                {
                    Ordinal = records.Count, CaptureId = record.Id, Collection = collections[record.CollectionId],
                    RecordId = new RecordId(id.Value.GetString() ?? throw new InvalidOperationException()), Presence = record.Presence,
                });
            }
            else if (capture is BaseModuleGenerationCapture generation)
            {
                BaseModuleGenerationCellDefinition cell = registry.FindCell(generation.CellId) ?? throw new InvalidOperationException();
                BaseModuleProgramValue key = generation.Key is null ? BaseModuleProgramValue.Missing : evaluator.Evaluate(generation.Key);
                OperationContext operation = session.Operation(BaseOperationKind.ModuleMutation, definition.Id);
                generations.Add(new BaseModuleGenerationCaptureRequest
                {
                    Ordinal = generations.Count, CaptureId = generation.Id, Cell = cell,
                    Scope = new BaseModuleGenerationScopeAuthority
                    {
                        Kind = cell.Scope,
                        Tenant = cell.Scope is BaseModuleGenerationScope.Tenant or BaseModuleGenerationScope.TenantAndKey ? operation.TenantId : null,
                        Project = cell.Scope is BaseModuleGenerationScope.Project or BaseModuleGenerationScope.ProjectAndKey ? operation.ProjectId : null,
                    },
                    KeyUtf8 = key.Present ? Encoding.UTF8.GetBytes(key.Value.GetString() ?? throw new InvalidOperationException()).ToImmutableArray() : [],
                    Absence = generation.Absence,
                });
            }
        }
        foreach (BaseModuleStatement statement in EnumerateStatements(definition.Template.Body))
        {
            string? collectionId = statement switch
            {
                BaseModuleCreateStatement value => value.CollectionId,
                BaseModulePatchStatement value => value.CollectionId,
                BaseModuleReplaceStatement value => value.CollectionId,
                BaseModuleUpsertStatement value => value.CollectionId,
                _ => null,
            };
            if (collectionId is null || !collections.TryGetValue(collectionId, out CollectionDefinition? collection)) continue;
            IEnumerable<BaseModuleObjectExpression> payloads = statement switch
            {
                BaseModuleCreateStatement value => [value.Payload],
                BaseModulePatchStatement value => [value.Patch],
                BaseModuleReplaceStatement value => [value.Payload],
                BaseModuleUpsertStatement value => [value.Create, value.Update],
                _ => [],
            };
            foreach (BaseModuleObjectExpression payload in payloads)
            foreach (BaseModuleObjectPropertyExpression property in payload.Properties)
            {
                FieldDefinition? field = collection.Fields?.SingleOrDefault(value => string.Equals(value.Id, property.StablePropertyId, StringComparison.Ordinal));
                if (field?.Relation is not { OwningSide: BaseRelationOwningSide.Source } relation) continue;
                BaseModuleProgramValue target = evaluator.Evaluate(property.Value);
                IEnumerable<string> ids = target.Value.ValueKind == JsonValueKind.Array
                    ? target.Value.EnumerateArray().Select(static value => value.GetString() ?? throw new InvalidOperationException()).ToArray()
                    : [target.Value.GetString() ?? throw new InvalidOperationException()];
                foreach (string id in ids)
                {
                    if (relations.Any(value => string.Equals(value.SourceStatementId, statement.Id, StringComparison.Ordinal)
                        && string.Equals(value.SourceFieldId, field.Id, StringComparison.Ordinal)
                        && string.Equals(value.TargetCollection.Id, relation.TargetCollectionId, StringComparison.Ordinal)
                        && value.TargetRecordId == new RecordId(id))) continue;
                    relations.Add(new BaseModuleRelationTargetCaptureRequest
                    {
                        Ordinal = relations.Count, SourceStatementId = statement.Id, SourceFieldId = field.Id,
                        TargetCollection = collections[relation.TargetCollectionId], TargetRecordId = new RecordId(id),
                    });
                }
            }
        }
        return new BaseModuleMutationCaptureExtension
        {
            OperationId = definition.Id, OperationVersion = definition.Version,
            OperationChecksum = Convert.ToHexString(definition.Checksum.ToArray()).ToLowerInvariant(),
            RequestDigest = Convert.ToHexString(SHA256.HashData(requestBytes)).ToLowerInvariant(),
            Records = records.ToImmutable(), RelationTargets = relations.ToImmutable(), Generations = generations.ToImmutable(),
        };
    }

    private static IEnumerable<BaseModuleStatement> EnumerateStatements(BaseModuleMutationBlock block)
    {
        foreach (BaseModuleStatement statement in block.Statements)
        {
            yield return statement;
            if (statement is not BaseModuleIfStatement branch) continue;
            foreach (BaseModuleStatement nested in EnumerateStatements(branch.WhenTrue)) yield return nested;
            foreach (BaseModuleStatement nested in EnumerateStatements(branch.WhenFalse)) yield return nested;
        }
    }

    internal static BaseAtomicMutationExecutionLimits ResolveExecutionLimits(BaseModuleMutationLimits value) => new()
    {
        MaximumItems = value.MaximumRecordMutations, MaximumQueryNodes = 0, MaximumQueryDepth = 0,
        MaximumLiteralValues = 0, MaximumSelectedRecords = 0, MaximumProducedMutations = value.MaximumRecordMutations,
        MaximumQueryExecutions = 0, MaximumPreviousStateRequirements = 0, MaximumRecordCaptures = value.MaximumRecordCaptures,
        MaximumRelationTargetCaptures = value.MaximumRelationTargetCaptures, MaximumGenerationReads = value.MaximumGenerationReads,
        MaximumGenerationComparisons = value.MaximumGenerationComparisons, MaximumGenerationIncrements = value.MaximumGenerationIncrements,
        MaximumGuardNodes = value.MaximumGuardNodes, MaximumGuardDepth = value.MaximumGuardDepth,
        MaximumStatements = value.MaximumStatements, MaximumBranches = value.MaximumBranches,
        MaximumExpressionNodes = value.MaximumExpressionNodes, MaximumSelectedBytes = value.MaximumSelectedBytes,
        MaximumEvidenceBytes = value.MaximumEvidenceBytes, MaximumTransientBytes = value.MaximumTransientBytes,
        MaximumReadIntervals = value.MaximumReadIntervals, MaximumSubjectValidations = value.MaximumSubjectValidations,
        MaximumAuthorityReads = value.MaximumAuthorityReads, MaximumRelationChecks = value.MaximumRelationChecks,
        MaximumUniqueConstraintChecks = value.MaximumUniqueConstraintChecks, MaximumRequestBytes = value.MaximumRequestBytes,
        MaximumRetirementProjections = value.MaximumRecordMutations, MaximumRetirementBarrierReads = value.MaximumRecordMutations,
        MaximumRetirementAcknowledgementReads = 1, MaximumRetirementPublications = value.MaximumRecordMutations,
        MaximumGenerationBytes = value.MaximumGenerationBytes, MaximumWrittenBytes = value.MaximumWrittenBytes,
        MaximumFactBytes = value.MaximumFactBytes, MaximumJournalBytes = value.MaximumJournalBytes,
        MaximumReceiptBytes = value.MaximumReceiptBytes, MaximumResultBytes = value.MaximumResultBytes,
        MaximumRetirementEvidenceBytes = value.MaximumEvidenceBytes, MaximumRetirementPublicationBytes = value.MaximumFactBytes,
        Deadlines = value.Deadlines with { },
    };

    private static bool AudienceAllowed(BaseSession session, BaseRegisteredModuleMutationDefinition definition) =>
        definition.Audience switch
        {
            BaseModuleMutationAudience.Service => session.Principal.AuthenticationState is PrincipalAuthenticationState.Service or PrincipalAuthenticationState.System,
            BaseModuleMutationAudience.System => session.Principal.AuthenticationState == PrincipalAuthenticationState.System,
            _ => false,
        };

    private static BaseFailure<BaseModuleMutationExecutionResult<TResult>> Failure<TResult>(OperationStatus status, string code, ErrorCategory category) =>
        Failure<TResult>(status, Error(code, category));
    private static BaseFailure<BaseModuleMutationExecutionResult<TResult>> Failure<TResult>(OperationStatus status, BaseError error) =>
        new(status, error, null, null);
    private static BaseError Error(string code, ErrorCategory category) => new() { Code = code, Message = "The registered module mutation could not be completed.", Category = category };
    private static string Digest(params string[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', values)))).ToLowerInvariant();
}

internal sealed class BaseModuleMutationReceiptResolver<TResult>(
    BaseRegisteredModuleMutationDefinition definition,
    System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultTypeInfo,
    IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> resultBindings,
    PrincipalContext principal,
    OperationContext operation,
    IBasePolicyOrchestrator policy) : IAtomicMutationProcessor
{
    internal BaseModuleMutationExecutionResult<TResult>? Result { get; private set; }

    public ValueTask<AtomicMutationProcessingResult> ProcessAsync(IAtomicRecordSession session, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Failed());

    public async ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseAtomicReceiptResult committedResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseModuleMutationReceiptResult? module = committedResult.ModuleMutation;
        if (committedResult.Kind != BaseAtomicReceiptResultKind.ModuleMutation || module is null
            || !string.Equals(module.OperationId, definition.Id, StringComparison.Ordinal)
            || module.OperationVersion != definition.Version)
            return Failed();
        if (!await BaseModuleReceiptDisclosure.AuthorizeAsync(
                committedResult, definition, resultBindings, principal, operation, policy, cancellationToken).ConfigureAwait(false))
            return Failed();
        try
        {
            TResult? typed = JsonSerializer.Deserialize(module.CanonicalResultBytes.AsSpan(), resultTypeInfo);
            if (typed is null) return Failed();
            Result = new BaseModuleMutationExecutionResult<TResult>
            {
                Disposition = BaseMutationRequestDisposition.Duplicate,
                Outcome = BaseModuleMutationOutcome.Duplicate,
                Result = typed,
            };
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, committedResult);
        }
        catch { return Failed(); }
    }

    private static AtomicMutationProcessingResult Failed() => new(
        AtomicMutationProcessingOutcome.Failed,
        [],
        new BaseError
        {
            Code = BaseModuleMutationErrorCodes.ReceiptUnavailable,
            Message = "The stored module mutation receipt cannot be resolved.",
            Category = ErrorCategory.Authorization,
        });
}

internal static class BaseModuleReceiptDisclosure
{
    internal static async ValueTask<bool> AuthorizeAsync(
        BaseAtomicReceiptResult committedResult,
        BaseRegisteredModuleMutationDefinition definition,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> resultBindings,
        PrincipalContext principal,
        OperationContext operation,
        IBasePolicyOrchestrator policy,
        CancellationToken cancellationToken)
    {
        foreach (BaseOwnedMutationFact owned in committedResult.Mutations)
        {
            BaseRecordMutationFact fact;
            try { fact = owned.MaterializeOwned(); }
            catch { return false; }
            RecordEnvelope? resource = fact.After ?? fact.Before;
            if (resource is null || !definition.SystemCollectionIds.Contains(fact.Collection.Id, StringComparer.Ordinal)) return false;
            OperationResult<BasePolicyEvaluation> disclosure = await policy.EvaluateReadAsync(new BasePolicyRequest
            {
                Principal = principal,
                Operation = operation with { CollectionId = fact.Collection.Id, RecordId = resource.Id.Value },
                Collection = fact.Collection,
                ResourceKind = PolicyResourceKind.ModuleMutation,
                ExistingRecord = resource,
                RecordId = resource.Id,
            }, cancellationToken).ConfigureAwait(false);
            BaseModuleSystemSourceGrant? sourceGrant = definition.SystemSourceGrants
                .SingleOrDefault(value => string.Equals(value.CollectionId, fact.Collection.Id, StringComparison.Ordinal));
            OperationContext sourceOperation = operation with { CollectionId = fact.Collection.Id, RecordId = resource.Id.Value };
            if (!disclosure.IsSuccess() || disclosure.Value is null || sourceGrant is null
                || !BaseSystemCollectionGate.HasExactModuleSourceGrant(disclosure, sourceGrant.GrantId,
                    definition.OwningModuleId, principal, sourceOperation, fact.Collection.Id)
                || !BaseRecordFilterMatcher.Matches(resource, disclosure.Value.EffectiveRecordFilter)) return false;
        }

        OperationResult<BasePolicyEvaluation> result = await policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                System = true, SystemOwnerModuleId = definition.OwningModuleId,
            },
            ResourceKind = PolicyResourceKind.ModuleMutation,
        }, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess() && result.Value is not null
            && BaseSystemCollectionGate.HasExactModuleGrant(result, definition.GrantId,
                definition.OwningModuleId, principal, operation)
            && ResultDisclosureAllows(result.Value.EffectiveReadMask, resultBindings.Values);
    }

    private static bool ResultDisclosureAllows(FieldMask? mask, IEnumerable<BaseModuleDtoPropertyBinding> bindings)
    {
        BaseModuleDtoPropertyBinding[] declared = bindings.ToArray();
        if (declared.Any(binding => binding.RecordDisclosure != BaseRecordDisclosure.Include))
            return false;
        string[] values = declared.Select(static binding => binding.StablePropertyId).ToArray();
        return mask?.Mode switch
        {
            null or FieldMaskMode.Unspecified or FieldMaskMode.AllowAll => true,
            FieldMaskMode.DenyAll => values.Length == 0,
            FieldMaskMode.IncludeOnly => values.All(value => (mask.Include ?? []).Contains(value, StringComparer.Ordinal)),
            FieldMaskMode.Exclude => values.All(value => !(mask.Exclude ?? []).Contains(value, StringComparer.Ordinal)),
            _ => false,
        };
    }
}
