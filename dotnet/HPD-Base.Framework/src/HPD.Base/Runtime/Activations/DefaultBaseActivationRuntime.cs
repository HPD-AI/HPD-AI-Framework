using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed class DefaultBaseActivationRuntime(
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    TimeProvider timeProvider) : IBaseActivationRuntime
{
    public async ValueTask<OperationResult<BaseActivationEnqueueResult>> EnqueueAsync<TInput, TResult>(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationRegistrationIdentity<TInput, TResult> identity,
        TInput input,
        BaseMutationRequestIdentity requestIdentity,
        BaseActivationEnqueueOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(requestIdentity);
        if (!BaseSystemCollectionGate.Allows(session.Principal))
            return Failure<BaseActivationEnqueueResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);

        OperationContext operation = session.Operation(BaseOperationKind.ActivationEnqueue, definition.Id);
        CollectionDefinition resource = PolicyResource(definition);
        OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = operation,
            Collection = resource,
            ResourceKind = PolicyResourceKind.ActivationDefinition,
        }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactActivationGrant(
            authorized, definition.Grants.Enqueue, definition.OwningModuleId, session.Principal, operation))
            return Failure<BaseActivationEnqueueResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);

        byte[] inputBytes;
        try { inputBytes = identity.CanonicalInput(input); }
        catch (BaseActivationDtoContractException exception) when (exception.Code == "base.activation.inputInvalid")
        { return Failure<BaseActivationEnqueueResult>(OperationStatus.ValidationFailed, "base.activation.inputInvalid", ErrorCategory.Validation); }
        catch (Exception exception) when (exception is JsonException or BaseModuleScalarContractException)
        { return Failure<BaseActivationEnqueueResult>(OperationStatus.ValidationFailed, "base.activation.inputInvalid", ErrorCategory.Validation); }
        if (inputBytes.LongLength > definition.Limits.MaximumInputBytes)
            return Failure<BaseActivationEnqueueResult>(OperationStatus.ValidationFailed, "base.activation.budgetExceeded", ErrorCategory.Validation);

        DateTimeOffset acceptedNow = timeProvider.GetUtcNow();
        DateTimeOffset requestedDue = options?.DueAt ?? acceptedNow;
        long requestedDueAt;
        try { requestedDueAt = requestedDue.ToUnixTimeMilliseconds(); }
        catch { return Failure<BaseActivationEnqueueResult>(OperationStatus.ValidationFailed, "base.activation.invalid", ErrorCategory.Validation); }
        if (requestedDueAt < 0)
            return Failure<BaseActivationEnqueueResult>(OperationStatus.ValidationFailed, "base.activation.invalid", ErrorCategory.Validation);

        RecordStoreRegistration[] candidates = stores.GetRegistrations()
            .Where(static item => item.Store is IAtomicRecordStore and IBaseActivationProvider)
            .DistinctBy(static item => item.Store)
            .ToArray();
        if (candidates.Length != 1 || candidates[0].Store is not IAtomicRecordStore capabilityStore ||
            candidates[0].Store is not IBaseActivationProvider activationProvider ||
            !BaseActivationCertificationReceiptContract.Validate(activationProvider.Descriptor) ||
            !activationProvider.Descriptor.Capability.AtomicCreationSupported)
            return Failure<BaseActivationEnqueueResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        IAtomicRecordStore store = candidates[0].AtomicExecutionStore ?? capabilityStore;

        BaseAtomicMutationExecutionLimits limits = definition.Limits.AtomicCreation;
        OperationResult<BaseAtomicMutationAuthorityRequirement> authority = await store
            .CaptureAtomicMutationAuthorityRequirementAsync(session.ApplicationId, [], limits, cancellationToken)
            .ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return CopyFailure<BaseActivationEnqueueResult, BaseAtomicMutationAuthorityRequirement>(authority);

        BaseOwnedSubjectScopeEvidence scope = session.ActivationScope;
        byte[] inputChecksum = SHA256.HashData(inputBytes);
        byte[] structuralDigest = StructuralDigest(
            session.ApplicationId,
            definition,
            inputChecksum,
            scope,
            options?.DueAt is null ? null : requestedDueAt,
            requestIdentity);
        var extension = new BaseActivationCreationExtension
        {
            StructuralDigest = structuralDigest.ToImmutableArray(),
            Items = [new BaseActivationCreateIntent
            {
                Ordinal = 0,
                Definition = new BaseActivationDefinitionKey
                {
                    Id = definition.Id, Version = definition.Version,
                    Checksum = definition.Checksum.ToArray().ToImmutableArray(),
                },
                CanonicalInput = inputBytes.ToImmutableArray(),
                InputChecksum = inputChecksum.ToImmutableArray(),
                Scope = scope,
                RequestedDueAt = requestedDueAt,
                EffectiveDueAt = requestedDueAt,
                Identity = requestIdentity,
            }],
        };
        string expectedActivationId = Convert.ToHexStringLower(SHA256.HashData(
            structuralDigest.Concat(new byte[4]).ToArray()));
        var processor = new BaseActivationEnqueueProcessor(authority.Value, extension, limits);
        var executionRequest = new RecordMutationExecutionRequest
        {
            AcquisitionTimeout = limits.Deadlines.AcquisitionTimeout,
            TransactionTimeout = limits.Deadlines.TransactionTimeout,
            CommitCompletionTimeout = limits.Deadlines.CommitObservationTimeout,
            AtomicRequest = new BaseAtomicMutationExecutionRequest
            {
                Identity = requestIdentity,
                StructuralDigest = structuralDigest,
                ExpiresAt = acceptedNow.AddDays(30),
                MaxReceiptBytes = checked((int)Math.Min(definition.Limits.Provider.MaximumEvidenceBytes, int.MaxValue)),
            },
        };
        RecordMutationExecutionResult execution;
        try { execution = await store.ExecuteAtomicAsync(processor, executionRequest, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return Failure<BaseActivationEnqueueResult>(OperationStatus.StoreError, "base.activation.cancelled", ErrorCategory.Store); }
        catch { return Failure<BaseActivationEnqueueResult>(OperationStatus.StoreError, "base.activation.storeError", ErrorCategory.Store); }
        if (execution.Outcome == RecordMutationExecutionOutcome.Indeterminate)
            return Failure<BaseActivationEnqueueResult>(OperationStatus.StoreError, "base.activation.commitIndeterminate", ErrorCategory.Store);
        if (execution.Outcome != RecordMutationExecutionOutcome.Committed)
        {
            string code = execution.Error?.Code ?? execution.Processing?.Error?.Code ?? "base.activation.storeError";
            if (code == "base.activation.providerContractInvalid")
            {
                (session.Services.GetService(typeof(BaseActivationProviderExecutionGate)) as BaseActivationProviderExecutionGate)
                    ?.QuarantineContractViolation();
                return BaseActivationFailureContract.ProviderContractInvalid<BaseActivationEnqueueResult>();
            }
            return Failure<BaseActivationEnqueueResult>(execution.Outcome == RecordMutationExecutionOutcome.ConflictRollbackConfirmed
                ? OperationStatus.Conflict : OperationStatus.StoreError,
                code,
                execution.Error?.Category ?? execution.Processing?.Error?.Category ?? ErrorCategory.Store);
        }
        string activationId = processor.ActivationId ?? expectedActivationId;
        return OperationResults.Ok(new BaseActivationEnqueueResult
        {
            ActivationId = activationId,
            State = BaseActivationState.Pending,
            Disposition = execution.RequestDisposition,
        });
    }

    private static CollectionDefinition PolicyResource(BaseActivationDefinition definition) => new()
    {
        Id = definition.Id,
        Name = definition.Id,
        Kind = BaseCollectionKinds.Custom,
        SchemaMode = SchemaMode.Strict,
        UnknownFields = UnknownFieldPolicy.Reject,
        System = true,
        SystemOwnerModuleId = definition.OwningModuleId,
    };

    private static byte[] StructuralDigest(
        string applicationId,
        BaseActivationDefinition definition,
        byte[] inputChecksum,
        BaseOwnedSubjectScopeEvidence scope,
        long? requestedDueAt,
        BaseMutationRequestIdentity identity)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "base.activation.identity.v2\0"); Add(hash, applicationId); Add(hash, definition.Id);
        Add(hash, definition.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        hash.AppendData(definition.Checksum.AsSpan()); hash.AppendData(inputChecksum);
        Add(hash, ((int)scope.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture)); Add(hash, scope.Value ?? string.Empty);
        Add(hash, requestedDueAt?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "immediate");
        Add(hash, identity.Scope); Add(hash, identity.Operation); Add(hash, identity.IdempotencyKey); hash.AppendData(identity.Fingerprint.ToArray());
        return hash.GetHashAndReset();
    }

    private static void Add(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length));
        hash.AppendData(length); hash.AppendData(bytes);
    }

    private static OperationResult<T> Failure<T>(OperationStatus status, string code, ErrorCategory category) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The activation operation could not be completed.", Category = category } };

    private static OperationResult<T> CopyFailure<T, TSource>(OperationResult<TSource> source) => new()
    { Status = source.Status, Error = source.Error, Warnings = source.Warnings, Diagnostics = source.Diagnostics };

    private sealed class BaseActivationEnqueueProcessor(
        BaseAtomicMutationAuthorityRequirement authority,
        BaseActivationCreationExtension extension,
        BaseAtomicMutationExecutionLimits limits) : IAtomicMutationProcessor
    {
        internal string? ActivationId { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            var request = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ActivationCreation,
                Intent = new BaseAtomicMutationIntent
                { IntentDigest = Convert.ToHexStringLower(extension.StructuralDigest.AsSpan()), Authority = authority, Items = [] },
                Activations = extension,
                Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured = await session.CaptureAtomicExecutionAsync(request, cancellationToken).ConfigureAwait(false);
            if (!captured.IsSuccess() || captured.Value?.Activations is null) return Failed(captured.Error);
            var plan = new BaseFinalizedAtomicExecutionPlan
            {
                Kind = request.Kind, PlanDigest = Convert.ToHexStringLower(extension.StructuralDigest.AsSpan()),
                IntentDigest = request.Intent.IntentDigest, CaptureDigest = captured.Value.CaptureDigest,
                PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]), Authority = authority,
                Items = [], SubjectValidations = [], Activations = extension, Limits = limits,
            };
            OperationResult<BasePreparedAtomicExecution> prepared = await session.PrepareAtomicExecutionAsync(captured.Value, plan, cancellationToken).ConfigureAwait(false);
            if (!prepared.IsSuccess() || prepared.Value?.Activations is null) return Failed(prepared.Error);
            OperationResult<BaseProvisionalAtomicExecution> applied = await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken).ConfigureAwait(false);
            if (!applied.IsSuccess() || applied.Value?.Activations is null) return Failed(applied.Error);
            ActivationId = applied.Value.Activations.Items[0].ActivationId;
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.ActivationCreation,
                Mutations = [],
                ActivationCreation = new BaseActivationCreationReceiptResult { ActivationIds = [ActivationId] },
            });
        }

        public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
            BaseAtomicReceiptResult committedResult,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (committedResult.Kind != BaseAtomicReceiptResultKind.ActivationCreation ||
                committedResult.ActivationCreation is null ||
                committedResult.ActivationCreation.ActivationIds.Length != 1 ||
                string.IsNullOrWhiteSpace(committedResult.ActivationCreation.ActivationIds[0]))
            {
                return ValueTask.FromResult(Failed(new BaseError
                {
                    Code = BaseMutationRequestErrorCodes.ReceiptUnavailable,
                    Message = "The stored activation receipt cannot be resolved.",
                    Category = ErrorCategory.Authorization,
                }));
            }

            ActivationId = committedResult.ActivationCreation.ActivationIds[0];
            return ValueTask.FromResult(new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.ReadyToCommit,
                committedResult));
        }

        private static AtomicMutationProcessingResult Failed(BaseError? error) => new(
            AtomicMutationProcessingOutcome.Failed, [], error ?? new BaseError
            { Code = "base.activation.providerContractInvalid", Message = "The provider cannot satisfy the activation contract.", Category = ErrorCategory.Capability });
    }
}
