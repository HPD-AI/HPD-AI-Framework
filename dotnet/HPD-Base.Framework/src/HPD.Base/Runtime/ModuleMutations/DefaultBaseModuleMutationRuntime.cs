using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
    BaseSubjectRetirementRegistry? retirementRegistry = null,
    BaseSemanticActivationRegistry? semanticRegistry = null,
    BaseActivationRegistry? activationRegistry = null,
    BaseActivationAcceptedTimeAuthority? acceptedTimeAuthority = null) : IBaseModuleMutationRuntime
{
    private readonly BaseSubjectLifecycleRegistry lifecycleConsumers = lifecycleRegistry ?? new([], subjects);
    private readonly BaseSubjectRetirementRegistry retirement = retirementRegistry ?? new([], [], lifecycleRegistry ?? new([], subjects));
    private readonly BaseActivationAcceptedTimeAuthority acceptedTimes = acceptedTimeAuthority ?? new(timeProvider);
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
        BaseSemanticActivationKeyDefinition? semanticDefinition;
        try { semanticDefinition = ResolveSemanticDefinition(definition, options, semanticRegistry); }
        catch { return Failure<TResult>(OperationStatus.ValidationFailed, "base.semanticActivation.contractInvalid", ErrorCategory.Validation); }
        if (semanticDefinition is not null && !await AuthorizeSemanticAsync(
                session, semanticDefinition, options!.SemanticActivation!, cancellationToken).ConfigureAwait(false))
            return Failure<TResult>(OperationStatus.PolicyDenied, BaseModuleMutationErrorCodes.Unauthorized, ErrorCategory.Authorization);
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

        RecordStoreRegistration? storeRegistration = ResolveOneRegistration(authorityCollections);
        IAtomicRecordStore? atomicStore = storeRegistration?.AtomicExecutionStore
            ?? storeRegistration?.Store as IAtomicRecordStore;
        if (atomicStore is null || !BaseModuleMutationCapabilityContract.Supports(definition.Limits, atomicStore.Capabilities.ModuleMutation))
            return Failure<TResult>(OperationStatus.Unsupported, BaseModuleMutationErrorCodes.CapabilityMissing, ErrorCategory.Unsupported);
        BaseAtomicMutationExecutionLimits limits = ResolveExecutionLimits(definition.Limits);
        OperationResult<BaseAtomicMutationAuthorityRequirement> authority = await atomicStore
            .CaptureAtomicMutationAuthorityRequirementAsync(session.ApplicationId, [.. authorityCollections], limits, cancellationToken)
            .ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return Failure<TResult>(authority.Status, authority.Error ?? Error(BaseModuleMutationErrorCodes.AuthorityChanged, ErrorCategory.Conflict));
        BaseAtomicSemanticActivationExtension? semantic;
        try { semantic = CreateSemanticExtension(definition, options, semanticRegistry, acceptedTimes, authority.Value, storeRegistration!.StoreId, request, generatedIdentity); }
        catch { return Failure<TResult>(OperationStatus.ValidationFailed, "base.semanticActivation.contractInvalid", ErrorCategory.Validation); }

        string intentDigest = Digest("base.moduleMutation.intent.v1", extension.RequestDigest, authority.Value.ApplicationId);
        var intent = new BaseAtomicMutationIntent
        {
            IntentDigest = intentDigest,
            Authority = authority.Value,
            Items = [],
        };
        var processor = new BaseModuleMutationProcessor<TRequest, TResult>(
            definition, generatedIdentity, request, intent, extension, options?.ActivationGuard,
            options?.ActivationCreation, semantic, limits, installed,
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

    private BaseAtomicSemanticActivationExtension? CreateSemanticExtension<TRequest, TResult>(
        BaseRegisteredModuleMutationDefinition operation,
        BaseModuleMutationExecutionOptions? options,
        BaseSemanticActivationRegistry? registry,
        BaseActivationAcceptedTimeAuthority acceptedTime,
        BaseAtomicMutationAuthorityRequirement authority,
        string logicalStoreId,
        TRequest request,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> requestIdentity)
    {
        BaseSemanticActivationGuardedRequest? requested = options?.SemanticActivation;
        if (requested is null) return null;
        if (registry is null || options!.ActivationGuard is null || options.ActivationCreation is not null)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseSemanticActivationKeyDefinition definition = registry.Find(requested.Key.DefinitionId, requested.Key.DefinitionVersion)
            ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        byte[] suppliedChecksum = requested.Key.CopyDefinitionChecksum();
        if (!CryptographicOperations.FixedTimeEquals(suppliedChecksum, definition.Checksum.AsSpan())
            || !string.Equals(requested.Key.ApplicationId, definition.OwningApplicationId, StringComparison.Ordinal)
            || !string.Equals(requested.Key.ModuleId, definition.OwningModuleId, StringComparison.Ordinal)
            || requested.Key.OwnerGeneration != registry.OwnerGeneration
            || requested.Scope.Kind != definition.ScopeKind)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseSemanticActivationModuleOperationIdentity expected = requested is BaseSemanticActivationGuardedEnsureRequest
            ? definition.EnsureOperation : definition.RetirementOperation;
        if (!string.Equals(operation.Id, expected.OperationId, StringComparison.Ordinal)
            || operation.Version != expected.OperationVersion
            || !string.Equals(Convert.ToHexStringLower(operation.Checksum.ToArray()), expected.OperationChecksum, StringComparison.Ordinal))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        byte[] canonicalKey = requested.Key.CopyCanonicalKey();
        if (canonicalKey.Length is < 1 || canonicalKey.Length > definition.Limits.MaximumCanonicalKeyBytes)
            throw new InvalidOperationException("base.semanticActivation.keyInvalid");
        byte[] proposedScopeBinding = RandomNumberGenerator.GetBytes(32);
        BaseSemanticActivationSubjectLifetimeBinding? subjectLifetime = ExtractSubjectLifetime(
            definition, request, requestIdentity, proposedScopeBinding);
        BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(Hash(
            "base.semanticActivation.key.v1\0", Encoding.UTF8.GetBytes(definition.Id), proposedScopeBinding, canonicalKey));
        var identity = new BaseSemanticActivationDefinitionIdentity
        {
            Id = new string(definition.Id.AsSpan()), Version = definition.Version,
            Checksum = definition.Checksum.ToArray().ToImmutableArray(), OwnerGeneration = registry.OwnerGeneration,
            OwningModuleId = new string(definition.OwningModuleId.AsSpan()),
            RetirementOperation = definition.RetirementOperation with { },
        };
        BaseSemanticActivationOperation semanticOperation = requested switch
        {
            BaseSemanticActivationGuardedEnsureRequest ensure => CreateEnsure(ensure, identity, key, canonicalKey, proposedScopeBinding, subjectLifetime,
                definition.OwningApplicationId, logicalStoreId, definition.OwningModuleId,
                activationRegistry ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid")),
            BaseSemanticActivationGuardedRetireRequest => new BaseSemanticActivationRetireIntent
            {
                Definition = identity, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(),
                Scope = requested.Scope with { Value = requested.Scope.Value is null ? null : new string(requested.Scope.Value.AsSpan()) },
                SubjectLifetime = subjectLifetime,
                CompletionOperation = definition.RetirementOperation with { },
            },
            _ => throw new InvalidOperationException("base.semanticActivation.contractInvalid"),
        };
        byte[] structural = Hash("base.semanticActivation.extension.v1\0", definition.Checksum.ToArray(), canonicalKey,
            proposedScopeBinding, [(byte)(requested is BaseSemanticActivationGuardedEnsureRequest ? 1 : 2)]);
        return new BaseAtomicSemanticActivationExtension
        {
            Capture = new BaseSemanticActivationCaptureRequest
            {
                Definition = identity,
                CanonicalKey = canonicalKey.ToImmutableArray(),
                KeyPreimageChecksum = requested.Key.CopyPreimageChecksum().ToImmutableArray(),
                Scope = requested.Scope with { Value = requested.Scope.Value is null ? null : new string(requested.Scope.Value.AsSpan()) },
                ProposedScopeBindingId = proposedScopeBinding.ToImmutableArray(),
                Operation = requested is BaseSemanticActivationGuardedEnsureRequest
                    ? BaseSemanticActivationOperationKind.Ensure : BaseSemanticActivationOperationKind.Retire,
                StoreAuthority = ResolveSemanticStoreAuthority(authority, registry, logicalStoreId),
                Limits = definition.Limits.Execution with { },
                AcceptedTime = acceptedTime.Capture(definition.OwningApplicationId),
            },
            Operation = semanticOperation,
            StructuralDigest = structural.ToImmutableArray(),
        };
    }

    private static BaseSemanticActivationStoreAuthorityRequirement ResolveSemanticStoreAuthority(
        BaseAtomicMutationAuthorityRequirement authority, BaseSemanticActivationRegistry registry, string logicalStoreId)
    {
        BaseSemanticActivationStoreAuthorityRequirement value = authority.SemanticActivation
            ?? throw new InvalidOperationException("base.semanticActivation.capabilityMissing");
        if (!string.Equals(value.ApplicationId, authority.ApplicationId, StringComparison.Ordinal)
            || !string.Equals(value.LogicalStoreId, logicalStoreId, StringComparison.Ordinal)
            || !string.Equals(value.StoreInstanceId, authority.StoreInstanceId, StringComparison.Ordinal)
            || value.RestoreEpoch != authority.RestoreEpoch || value.SchemaGeneration != authority.SchemaGeneration
            || value.SemanticAuthorityGeneration <= 0 || value.DefinitionSetChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(value.DefinitionSetChecksum.AsSpan(), registry.DefinitionSetChecksum.AsSpan()))
            throw new InvalidOperationException("base.semanticActivation.authorityChanged");
        return value with
        {
            ApplicationId = new string(value.ApplicationId.AsSpan()), LogicalStoreId = new string(value.LogicalStoreId.AsSpan()),
            StoreInstanceId = new string(value.StoreInstanceId.AsSpan()),
            DefinitionSetChecksum = value.DefinitionSetChecksum.ToArray().ToImmutableArray(),
        };
    }

    private static BaseSemanticActivationKeyDefinition? ResolveSemanticDefinition(
        BaseRegisteredModuleMutationDefinition operation,
        BaseModuleMutationExecutionOptions? options,
        BaseSemanticActivationRegistry? registry)
    {
        BaseSemanticActivationGuardedRequest? requested = options?.SemanticActivation;
        if (requested is null) return null;
        if (registry is null || options!.ActivationGuard is null || options.ActivationCreation is not null)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseSemanticActivationKeyDefinition definition = registry.Find(requested.Key.DefinitionId, requested.Key.DefinitionVersion)
            ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseSemanticActivationModuleOperationIdentity expected = requested is BaseSemanticActivationGuardedEnsureRequest
            ? definition.EnsureOperation : definition.RetirementOperation;
        if (!string.Equals(operation.Id, expected.OperationId, StringComparison.Ordinal)
            || operation.Version != expected.OperationVersion
            || !string.Equals(Convert.ToHexStringLower(operation.Checksum.ToArray()), expected.OperationChecksum, StringComparison.Ordinal))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        return definition;
    }

    private async ValueTask<bool> AuthorizeSemanticAsync(
        BaseSession session,
        BaseSemanticActivationKeyDefinition definition,
        BaseSemanticActivationGuardedRequest request,
        CancellationToken cancellationToken)
    {
        string grant = request is BaseSemanticActivationGuardedEnsureRequest
            ? definition.EnsureGrantId : definition.RetirementGrantId;
        OperationContext operation = session.Operation(BaseOperationKind.ModuleMutation, definition.Id) with
        {
            CollectionId = definition.Id,
            Mode = OperationMode.System,
        };
        OperationResult<BasePolicyEvaluation> evaluation = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = definition.Id, Name = "Semantic activation authority", Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                System = true, SystemOwnerModuleId = definition.OwningModuleId,
            },
            ResourceKind = PolicyResourceKind.ModuleMutation,
        }, cancellationToken).ConfigureAwait(false);
        return BaseSystemCollectionGate.HasExactModuleGrant(
            evaluation, grant, definition.OwningModuleId, session.Principal, operation);
    }

    private static BaseSemanticActivationEnsureIntent CreateEnsure(
        BaseSemanticActivationGuardedEnsureRequest request,
        BaseSemanticActivationDefinitionIdentity definition,
        BaseSemanticActivationKeyDigest key,
        byte[] canonicalKey,
        byte[] scopeBinding,
        BaseSemanticActivationSubjectLifetimeBinding? subjectLifetime,
        string applicationId,
        string logicalStoreId,
        string owningModuleId,
        BaseActivationRegistry installedActivations)
    {
        long due = request.DueAt?.ToUnixTimeMilliseconds() ?? 0;
        var dueAuthority = new BaseSemanticActivationDueAuthority
        {
            Mode = request.DueAt is null ? BaseSemanticActivationDueMode.AcceptedCurrentTime : BaseSemanticActivationDueMode.ExplicitUtcInstant,
            CanonicalUnixMilliseconds = due,
        };
        Span<byte> digest = stackalloc byte[32]; key.CopyTo(digest);
        byte[] activationIdBytes = SemanticActivationId(applicationId, logicalStoreId, owningModuleId,
            definition.Id, scopeBinding, canonicalKey);
        byte[] creationChecksum = Hash("base.semanticActivation.creation.v1\0", definition.Checksum.ToArray(), digest.ToArray(), scopeBinding, activationIdBytes);
        BaseActivationDefinition installed = installedActivations.Find(request.Activation.Id, request.Activation.Version)
            ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        if (!CryptographicOperations.FixedTimeEquals(installed.Checksum.AsSpan(), request.Activation.Checksum.AsSpan()))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        return new BaseSemanticActivationEnsureIntent
        {
            Definition = definition, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(),
            Scope = request.Scope with { Value = request.Scope.Value is null ? null : new string(request.Scope.Value.AsSpan()) },
            SubjectLifetime = subjectLifetime,
            Due = dueAuthority,
            Activation = new BaseSemanticActivationCreateIntent
            {
                Definition = request.Activation with { Checksum = request.Activation.Checksum.ToArray().ToImmutableArray() },
                CanonicalInput = request.CanonicalInput.ToArray().ToImmutableArray(), InputChecksum = request.InputChecksum.ToArray().ToImmutableArray(),
                Scope = request.Scope with { Value = request.Scope.Value is null ? null : new string(request.Scope.Value.AsSpan()) }, Due = dueAuthority,
                Priority = 0, InitiallyEligible = true,
                Identity = new BaseSemanticActivationCreationIdentity
                {
                    SemanticDefinition = definition, Key = key, ScopeBindingId = scopeBinding.ToImmutableArray(),
                    DerivedActivationIdBytes = activationIdBytes.ToImmutableArray(), Checksum = creationChecksum.ToImmutableArray(),
                },
                Limits = installed.Limits with
                {
                    Provider = installed.Limits.Provider with { },
                    AtomicCreation = installed.Limits.AtomicCreation with { Deadlines = installed.Limits.AtomicCreation.Deadlines with { } },
                },
            },
        };
    }

    private static byte[] SemanticActivationId(string applicationId, string logicalStoreId, string owningModuleId,
        string definitionId, byte[] scopeBinding, byte[] canonicalKey) => Hash(
        "base.semanticActivation.activation.v1\0", Encoding.UTF8.GetBytes(applicationId), Encoding.UTF8.GetBytes(logicalStoreId),
        Encoding.UTF8.GetBytes(owningModuleId), Encoding.UTF8.GetBytes(definitionId), scopeBinding, canonicalKey);

    private BaseSemanticActivationSubjectLifetimeBinding? ExtractSubjectLifetime<TRequest, TResult>(
        BaseSemanticActivationKeyDefinition definition,
        TRequest request,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity,
        byte[] proposedScopeBinding)
    {
        if (definition.Compaction is BaseSemanticActivationNoCompaction) return null;
        if (definition.Compaction is not BaseSemanticActivationSubjectRetirementCompaction compaction)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseGeneratedSubjectRegistration subject = subjects.Find(
            compaction.SubjectContract.ContractId, compaction.SubjectContract.ContractVersion)
            ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseModuleDtoPropertyBinding[] matches = identity.RequestBindings.Values
            .Where(value => value.StablePropertyId == compaction.SubjectReferenceRequestPropertyId).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        JsonElement current = JsonSerializer.SerializeToElement(request, identity.RequestTypeInfo);
        for (int index = 0; index < matches[0].WirePropertyPath.Count; index++)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(matches[0].WirePropertyPath[index], out current))
                throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        }
        var decoded = BaseSubjectReferenceEncoding.DecodeElement(current, subject.Definition.SubjectIdKind,
            subject.Definition.MaximumSubjectIdUtf8Bytes);
        var value = new BaseSemanticActivationSubjectLifetimeBinding
        {
            ContractId = subject.Definition.Id, ContractVersion = subject.Definition.Version,
            ContractChecksum = Convert.FromHexString(subject.Checksum).ToImmutableArray(),
            SubjectId = decoded.SubjectId,
            AuthorityEpoch = decoded.AuthorityEpoch,
            Incarnation = decoded.Incarnation,
            ScopeBindingId = proposedScopeBinding.ToImmutableArray(), Checksum = [],
        };
        return value with { Checksum = SemanticLifetimeChecksum(value).ToImmutableArray() };
    }

    private static byte[] SemanticLifetimeChecksum(BaseSemanticActivationSubjectLifetimeBinding value) => Hash(
        "base.semanticActivation.subjectLifetime.v1\0", Encoding.UTF8.GetBytes(value.ContractId),
        BitConverter.GetBytes(value.ContractVersion).Reverse().ToArray(), value.ContractChecksum.ToArray(),
        value.SubjectId.ToUtf8Bytes(), Encoding.UTF8.GetBytes(value.AuthorityEpoch.ToBase64Url()),
        Encoding.UTF8.GetBytes(value.Incarnation.ToBase64Url()), value.ScopeBindingId.ToArray());

    private static byte[] Hash(string purpose, params byte[][] fields)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(purpose));
        byte[] length = new byte[4];
        foreach (byte[] field in fields)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, field.Length);
            hash.AppendData(length); hash.AppendData(field);
        }
        return hash.GetHashAndReset();
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
        RecordStoreRegistration? registration = ResolveOneRegistration(authorityCollections);
        return registration?.AtomicExecutionStore ?? registration?.Store as IAtomicRecordStore;
    }

    private RecordStoreRegistration? ResolveOneRegistration(CollectionDefinition[] authorityCollections)
    {
        RecordStoreRegistration[] registrations = authorityCollections.Length == 0
            ? stores.GetRegistrations()
            : authorityCollections.Select(value => stores.GetRegistrationForCollection(value.Id)).Where(static value => value is not null).Cast<RecordStoreRegistration>().DistinctBy(static value => value.StoreId).ToArray();
        return registrations.Length == 1 ? registrations[0] : null;
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
