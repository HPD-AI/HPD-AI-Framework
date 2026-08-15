using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed class DefaultBaseModuleMutationRuntime(
    IRecordStoreRegistry stores,
    BaseCollectionRegistry collections,
    BaseModuleMutationRegistry registry,
    TimeProvider timeProvider) : IBaseModuleMutationRuntime
{
    public async ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options,
        CancellationToken cancellationToken)
    {
        if (!AudienceAllowed(session, definition)
            || options?.MaximumWait is { } wait && (wait <= TimeSpan.Zero || wait > definition.Limits.Deadlines.CommitObservationTimeout))
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
        if (atomicStore is null)
            return Failure<TResult>(OperationStatus.Unsupported, BaseModuleMutationErrorCodes.CapabilityMissing, ErrorCategory.Unsupported);
        BaseAtomicMutationExecutionLimits limits = Limits(definition.Limits);
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
            definition, generatedIdentity, request, intent, extension, limits, installed);
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

    public ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ResolveAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<BaseResult<BaseModuleMutationExecutionResult<TResult>>>(
            Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound));

    private IAtomicRecordStore? ResolveOneStore(CollectionDefinition[] authorityCollections)
    {
        RecordStoreRegistration[] registrations = authorityCollections.Length == 0
            ? stores.GetRegistrations()
            : authorityCollections.Select(value => stores.GetRegistrationForCollection(value.Id)).Where(static value => value is not null).Cast<RecordStoreRegistration>().DistinctBy(static value => value.StoreId).ToArray();
        return registrations.Length == 1 ? registrations[0].Store as IAtomicRecordStore : null;
    }

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
        return new BaseModuleMutationCaptureExtension
        {
            OperationId = definition.Id, OperationVersion = definition.Version,
            OperationChecksum = Convert.ToHexString(definition.Checksum.ToArray()).ToLowerInvariant(),
            RequestDigest = Convert.ToHexString(SHA256.HashData(requestBytes)).ToLowerInvariant(),
            Records = records.ToImmutable(), RelationTargets = [], Generations = generations.ToImmutable(),
        };
    }

    private static BaseAtomicMutationExecutionLimits Limits(BaseModuleMutationLimits value) => new()
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
        MaximumGenerationBytes = value.MaximumGenerationBytes, MaximumWrittenBytes = value.MaximumWrittenBytes,
        MaximumFactBytes = value.MaximumFactBytes, MaximumJournalBytes = value.MaximumJournalBytes,
        MaximumReceiptBytes = value.MaximumReceiptBytes, MaximumResultBytes = value.MaximumResultBytes,
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
