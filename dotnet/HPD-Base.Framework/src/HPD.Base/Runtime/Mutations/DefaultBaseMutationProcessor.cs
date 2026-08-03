using System.Diagnostics;
using System.Text.Json;

namespace HPD.Base;

internal sealed class DefaultBaseMutationProcessor(
    BaseMutationCommand[] commands,
    PrincipalContext principal,
    IBasePolicyOrchestrator policy,
    IBaseResultNormalizer normalizer,
    CollectionDefinition[] collections,
    TimeSpan transactionTimeout) : IAtomicMutationProcessor
{
    private readonly List<BaseMutationAttempt> _attempts = [];
    private readonly IReadOnlyDictionary<string, CollectionDefinition> _collections = collections.ToDictionary(static value => value.Id, StringComparer.Ordinal);
    private long _deadline;

    /// <summary>Gets the attempts.</summary>
    public IReadOnlyList<BaseMutationAttempt> Attempts => _attempts;

    public async ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseRecordMutationFact[] committedMutations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(committedMutations);
        if (_attempts.Count != 0 || committedMutations.Length != commands.Length)
            return Failed(Error(BaseMutationRequestErrorCodes.ReceiptUnavailable, "The stored receipt is unavailable.", ErrorCategory.Authorization));

        for (var index = 0; index < committedMutations.Length; index++)
        {
            BaseMutationCommand command = commands[index];
            BaseRecordMutationFact mutation = committedMutations[index];
            if (!string.Equals(mutation.Collection.Id, command.Collection.Id, StringComparison.Ordinal)
                || mutation.RequestedOperation != command.Kind)
                return Failed(Error(BaseMutationRequestErrorCodes.ReceiptUnavailable, "The stored receipt is unavailable.", ErrorCategory.Authorization));

            RecordEnvelope? resource = mutation.After ?? mutation.Before;
            OperationResult<BasePolicyEvaluation> policyResult = await policy.EvaluateReadAsync(new BasePolicyRequest
            {
                Principal = principal,
                Operation = command.Context,
                Collection = command.Collection,
                ResourceKind = PolicyResourceKind.Record,
                RecordId = resource?.Id,
                ExistingRecord = resource,
            }, cancellationToken).ConfigureAwait(false);
            if (!policyResult.IsSuccess())
                return Failed(policyResult.Error ?? Error("base.runtime.policy.denied", "Policy denied the operation.", ErrorCategory.Authorization));
            if (policyResult.Value?.Decision.Effect != PolicyEffect.Allow
                || resource is not null && !BaseRecordFilterMatcher.Matches(resource, policyResult.Value.EffectiveRecordFilter))
                return Failed(Error("base.runtime.policy.denied", "Policy denied the operation.", ErrorCategory.Authorization));

            _attempts.Add(new BaseMutationAttempt
            {
                Command = command,
                Mutation = mutation with { ItemId = command.ItemId },
                Policy = policyResult.Value,
                Status = mutation.CommittedOperation switch
                {
                    BaseCommittedRecordMutationKind.Create => OperationStatus.Created,
                    BaseCommittedRecordMutationKind.Delete => OperationStatus.Deleted,
                    _ => OperationStatus.Updated,
                },
                Revision = mutation.After?.Metadata.Revision is { } revision
                    ? new RevisionInfo
                    {
                        Revision = revision.Value,
                        ETag = mutation.After.Metadata.ETag,
                        LastModified = mutation.After.Metadata.UpdatedAt,
                        Guarantee = RevisionGuarantee.Store,
                    }
                    : null,
            });
        }

        return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, committedMutations);
    }

    /// <summary>Executes the process async operation.</summary>
    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_attempts.Count != 0)
            return Failed(Error("base.runtime.batch.invalid", "A mutation processor can only be invoked once.", ErrorCategory.Unexpected));
        _deadline = Stopwatch.GetTimestamp() + (long)(transactionTimeout.TotalSeconds * Stopwatch.Frequency);

        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = await ProcessCommandAsync(session, command, cancellationToken).ConfigureAwait(false);
            _attempts.Add(attempt);
            if (!attempt.Status.IsSuccess())
                return Failed(attempt.Error ?? Error("base.runtime.batch.itemInvalid", "A batch item failed.", ErrorCategory.Unexpected));
        }

        return new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            _attempts.Select(static attempt => attempt.Mutation!).ToArray());
    }

    private async ValueTask<BaseMutationAttempt> ProcessCommandAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken) =>
        command.Kind switch
        {
            BaseRecordMutationKind.Create => await CreateAsync(session, command, cancellationToken).ConfigureAwait(false),
            BaseRecordMutationKind.Patch => await PatchAsync(session, command, cancellationToken).ConfigureAwait(false),
            BaseRecordMutationKind.Replace => await ReplaceAsync(session, command, cancellationToken).ConfigureAwait(false),
            BaseRecordMutationKind.Delete => await DeleteAsync(session, command, cancellationToken).ConfigureAwait(false),
            BaseRecordMutationKind.Upsert => await UpsertAsync(session, command, cancellationToken).ConfigureAwait(false),
            _ => Failure(command, OperationStatus.ValidationFailed, Error(
                "base.runtime.batch.itemInvalid", "The mutation kind is invalid.", ErrorCategory.Validation))
        };

    private async ValueTask<BaseMutationAttempt> CreateAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken,
        BaseRecordMutationKind requested = BaseRecordMutationKind.Create,
        RecordUpsertOutcome? upsertOutcome = null)
    {
        var validated = command.CreatePayload!;
        var policyResult = await EvaluateAsync(command, PolicyResourceKind.CreatePayload, validated.Payload, null, null, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess() || policyResult.Value is null)
            return FromFailure(command, policyResult);
        if (EnforceWritePolicy<RecordEnvelope>(validated.Payload, validated.Payload, policyResult.Value) is { } gate)
            return FromFailure(command, gate);
        if (await EnforceRelationsAsync(session, command, validated.Payload, cancellationToken).ConfigureAwait(false) is { } relationError)
            return Failure(command, RelationStatus(relationError), relationError);

        return await WriteCreateAsync(
            session,
            command,
            policyResult.Value,
            requested,
            upsertOutcome,
            cancellationToken).ConfigureAwait(false);
    }

    private BaseMutationAttempt NormalizeCreateFailure(
        BaseMutationCommand command,
        OperationResult<BasePolicyEvaluation> policyResult) =>
        FromFailure(command, policyResult);

    private async ValueTask<BaseMutationAttempt> WriteCreateAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        BasePolicyEvaluation policyResult,
        BaseRecordMutationKind requested,
        RecordUpsertOutcome? upsertOutcome,
        CancellationToken cancellationToken)
    {
        var validated = command.CreatePayload!;
        var request = command.Create! with { Payload = validated.Payload };
        var result = await session.CreateAsync(
            command.Collection,
            request,
            SessionContext(command, requested, validated.ChangedFields),
            cancellationToken).ConfigureAwait(false);
        return NormalizeSession(command, result, policyResult, upsertOutcome);
    }

    private async ValueTask<BaseMutationAttempt> PatchAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken,
        BaseRecordMutationKind requested = BaseRecordMutationKind.Patch,
        RecordUpsertOutcome? upsertOutcome = null,
        RecordEnvelope? knownExisting = null)
    {
        var existingResult = knownExisting is null
            ? normalizer.NormalizeStoreResult(
                await session.GetAsync(command.Collection, command.RecordId!.Value, command.Context, cancellationToken).ConfigureAwait(false),
                command.Context)
            : OperationResults.Ok(knownExisting);
        if (!existingResult.IsSuccess() || existingResult.Value is null)
            return FromFailure(command, existingResult, providerError: true);

        var validated = command.UpdatePayload!;
        var proposedPayload = BasePolicyRuntimeSimulation.MergePatchPayload(existingResult.Value.Payload, validated.Payload);
        var proposed = existingResult.Value with { Payload = proposedPayload };
        var policyResult = await EvaluateAsync(
            command, PolicyResourceKind.UpdatePayload, proposedPayload,
            existingResult.Value, proposed, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess() || policyResult.Value is null)
            return FromFailure(command, policyResult);
        if (EnforceWritePolicy<RecordEnvelope>(proposedPayload, validated.Payload, policyResult.Value) is { } gate)
            return FromFailure(command, gate);
        if (await EnforceRelationsAsync(session, command, proposedPayload, cancellationToken).ConfigureAwait(false) is { } relationError)
            return Failure(command, RelationStatus(relationError), relationError);

        return await WritePatchAsync(
            session,
            command,
            existingResult.Value,
            policyResult.Value,
            requested,
            upsertOutcome,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<BaseMutationAttempt> WritePatchAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        RecordEnvelope existing,
        BasePolicyEvaluation policyResult,
        BaseRecordMutationKind requested,
        RecordUpsertOutcome? upsertOutcome,
        CancellationToken cancellationToken)
    {
        var validated = command.UpdatePayload!;
        var request = command.Patch! with { Patch = validated.Payload };
        var result = await session.PatchAsync(
            command.Collection,
            command.RecordId.GetValueOrDefault(),
            request,
            SessionContext(command, requested, validated.ChangedFields),
            cancellationToken).ConfigureAwait(false);
        return NormalizeSession(command, result, policyResult, upsertOutcome);
    }

    private async ValueTask<BaseMutationAttempt> ReplaceAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken,
        BaseRecordMutationKind requested = BaseRecordMutationKind.Replace,
        RecordUpsertOutcome? upsertOutcome = null,
        RecordEnvelope? knownExisting = null)
    {
        var existingResult = knownExisting is null
            ? normalizer.NormalizeStoreResult(
                await session.GetAsync(command.Collection, command.RecordId!.Value, command.Context, cancellationToken).ConfigureAwait(false),
                command.Context)
            : OperationResults.Ok(knownExisting);
        if (!existingResult.IsSuccess() || existingResult.Value is null)
            return FromFailure(command, existingResult, providerError: true);

        var validated = command.UpdatePayload!;
        var proposed = existingResult.Value with { Payload = validated.Payload };
        var policyResult = await EvaluateAsync(
            command, PolicyResourceKind.UpdatePayload, validated.Payload,
            existingResult.Value, proposed, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess() || policyResult.Value is null)
            return FromFailure(command, policyResult);
        if (EnforceWritePolicy<RecordEnvelope>(validated.Payload, validated.Payload, policyResult.Value) is { } gate)
            return FromFailure(command, gate);
        if (await EnforceRelationsAsync(session, command, validated.Payload, cancellationToken).ConfigureAwait(false) is { } relationError)
            return Failure(command, RelationStatus(relationError), relationError);

        return await WriteReplaceAsync(
            session,
            command,
            existingResult.Value,
            policyResult.Value,
            requested,
            upsertOutcome,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<BaseMutationAttempt> WriteReplaceAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        RecordEnvelope existing,
        BasePolicyEvaluation policyResult,
        BaseRecordMutationKind requested,
        RecordUpsertOutcome? upsertOutcome,
        CancellationToken cancellationToken)
    {
        var validated = command.UpdatePayload!;
        var request = command.Replace! with { Payload = validated.Payload };
        var result = await session.ReplaceAsync(
            command.Collection,
            command.RecordId.GetValueOrDefault(),
            request,
            SessionContext(command, requested, validated.ChangedFields),
            cancellationToken).ConfigureAwait(false);
        return NormalizeSession(command, result, policyResult, upsertOutcome);
    }

    private async ValueTask<BaseMutationAttempt> DeleteAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken)
    {
        var existing = normalizer.NormalizeStoreResult(
            await session.GetAsync(command.Collection, command.RecordId!.Value, command.Context, cancellationToken).ConfigureAwait(false),
            command.Context);
        if (!existing.IsSuccess() || existing.Value is null)
            return FromFailure(command, existing, providerError: true);

        var policyResult = await EvaluateAsync(
            command, PolicyResourceKind.DeleteCandidate, null,
            existing.Value, null, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
            return FromFailure(command, policyResult);
        if (EnforceWritePolicy<DeleteResult>(existing.Value.Payload, null, policyResult.Value) is { } gate)
            return FromFailure(command, gate);

        var result = await session.DeleteAsync(
            command.Collection,
            command.RecordId.Value,
            command.Delete!,
            SessionContext(command, BaseRecordMutationKind.Delete),
            cancellationToken).ConfigureAwait(false);
        return NormalizeSession(command, result, policyResult.Value, null);
    }

    private async ValueTask<BaseMutationAttempt> UpsertAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Upsert!;
        var existing = normalizer.NormalizeStoreResult(
            await session.GetAsync(command.Collection, request.Id, command.Context, cancellationToken).ConfigureAwait(false),
            command.Context);
        if (existing.Status == OperationStatus.NotFound)
        {
            var validated = command.CreatePayload!;
            var policyResult = await EvaluateAsync(
                command,
                PolicyResourceKind.CreatePayload,
                validated.Payload,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            if (!policyResult.IsSuccess() || policyResult.Value is null)
                return NormalizeCreateFailure(command, policyResult);
            if (EnforceWritePolicy<RecordEnvelope>(
                    validated.Payload,
                    validated.Payload,
                    policyResult.Value) is { } gate)
            {
                return FromFailure(command, gate);
            }
            if (await EnforceRelationsAsync(session, command, validated.Payload, cancellationToken).ConfigureAwait(false) is { } createRelationError)
                return Failure(command, RelationStatus(createRelationError), createRelationError);

            if (request.Condition == RecordUpsertExistenceCondition.UpdateOnly || request.ExpectedRevision is not null)
                return Failure(command,
                    request.ExpectedRevision is null
                        ? OperationStatus.NotFound
                        : OperationStatus.Conflict,
                    Error(
                    request.ExpectedRevision is null ? "base.runtime.upsert.preconditionFailed" : "base.runtime.revision.conflict",
                    "The upsert precondition was not satisfied.",
                    request.ExpectedRevision is null ? ErrorCategory.NotFound : ErrorCategory.Conflict));

            return await WriteCreateAsync(
                session,
                command,
                policyResult.Value,
                BaseRecordMutationKind.Upsert,
                RecordUpsertOutcome.Created,
                cancellationToken).ConfigureAwait(false);
        }

        if (!existing.IsSuccess() || existing.Value is null)
            return FromFailure(command, existing, providerError: true);

        var validatedUpdate = command.UpdatePayload!;
        var proposedPayload = request.UpdateMode == RecordUpsertUpdateMode.Patch
            ? BasePolicyRuntimeSimulation.MergePatchPayload(
                existing.Value.Payload,
                validatedUpdate.Payload)
            : validatedUpdate.Payload;
        var proposed = existing.Value with { Payload = proposedPayload };
        var updatePolicy = await EvaluateAsync(
            command,
            PolicyResourceKind.UpdatePayload,
            proposedPayload,
            existing.Value,
            proposed,
            cancellationToken).ConfigureAwait(false);
        if (!updatePolicy.IsSuccess() || updatePolicy.Value is null)
            return FromFailure(command, updatePolicy);
        if (EnforceWritePolicy<RecordEnvelope>(
                proposedPayload,
                validatedUpdate.Payload,
                updatePolicy.Value) is { } updateGate)
        {
            return FromFailure(command, updateGate);
        }
        if (await EnforceRelationsAsync(session, command, proposedPayload, cancellationToken).ConfigureAwait(false) is { } relationError)
            return Failure(command, RelationStatus(relationError), relationError);

        if (request.Condition == RecordUpsertExistenceCondition.CreateOnly)
            return Failure(command, OperationStatus.Conflict, Error(
                "base.runtime.upsert.preconditionFailed",
                "The upsert precondition was not satisfied.",
                ErrorCategory.Conflict));

        return request.UpdateMode == RecordUpsertUpdateMode.Patch
            ? await WritePatchAsync(
                session,
                command,
                existing.Value,
                updatePolicy.Value,
                BaseRecordMutationKind.Upsert,
                RecordUpsertOutcome.Updated,
                cancellationToken).ConfigureAwait(false)
            : await WriteReplaceAsync(
                session,
                command,
                existing.Value,
                updatePolicy.Value,
                BaseRecordMutationKind.Upsert,
                RecordUpsertOutcome.Updated,
                cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateAsync(
        BaseMutationCommand command,
        PolicyResourceKind resourceKind,
        RecordPayload? proposedPayload,
        RecordEnvelope? existing,
        RecordEnvelope? proposed,
        CancellationToken cancellationToken)
    {
        using var activity = HPDBaseRuntimeTelemetry.StartPolicyEvaluation(
            command.Context,
            resourceKind.ToString());
        var startedAt = Stopwatch.GetTimestamp();
        var result = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = command.Context,
            Collection = command.Collection,
            ResourceKind = resourceKind,
            RecordId = command.RecordId ?? command.Upsert?.Id,
            ExistingRecord = existing,
            ProposedPayload = proposedPayload,
            ProposedRecord = proposed
        }, cancellationToken).ConfigureAwait(false);
        return HPDBaseRuntimeTelemetry.FinishPolicyEvaluation(
            activity,
            result,
            command.Context,
            startedAt);
    }

    private async ValueTask<BaseError?> EnforceRelationsAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        RecordPayload payload,
        CancellationToken cancellationToken)
    {
        foreach (FieldDefinition field in command.Collection.Fields ?? [])
        {
            if (field.Relation is not { OwningSide: BaseRelationOwningSide.Source } relation)
                continue;
            if (relation.DeleteBehavior is not BaseRelationDeleteBehavior.Restrict || relation.ExistenceEnforcement is not EnforcementOwner.Runtime)
                return RelationError("base.relation.enforcementUnsupported", "The declared relation enforcement mode is unavailable.", ErrorCategory.Unsupported);
            if (!_collections.TryGetValue(relation.TargetCollectionId, out CollectionDefinition? targetCollection))
                return RelationError("base.relation.invalid", "The declared relation is invalid.", ErrorCategory.Validation);
            if (!TryRelationIds(payload, field.Name, relation, out RecordId[] ids, out string? code))
                return RelationError(code!, "The relation value has an invalid shape or cardinality.", ErrorCategory.Validation);

            foreach (RecordId id in ids)
            {
                OperationResult<RecordEnvelope> target = normalizer.NormalizeStoreResult(
                    await session.GetAsync(targetCollection, id, command.Context, cancellationToken).ConfigureAwait(false),
                    command.Context);
                if (!target.IsSuccess() || target.Value is null)
                    return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization);

                OperationResult<BasePolicyEvaluation> targetPolicy;
                try
                {
                    Task<OperationResult<BasePolicyEvaluation>> policyTask = policy.EvaluateReadAsync(new BasePolicyRequest
                    {
                        Principal = principal,
                        Operation = command.Context,
                        Collection = targetCollection,
                        ResourceKind = PolicyResourceKind.RelationTarget,
                        RecordId = id,
                        ExistingRecord = target.Value
                    }, cancellationToken).AsTask();
                    TimeSpan remaining = TimeSpan.FromSeconds(Math.Max(0, _deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
                    if (remaining <= TimeSpan.Zero) return RelationError("base.relation.policyTimeout", "Relation policy evaluation exceeded its bounded lifetime.", ErrorCategory.Store);
                    TimeSpan publicationMargin = TimeSpan.FromMilliseconds(Math.Min(10, Math.Max(1, remaining.TotalMilliseconds / 10)));
                    remaining -= publicationMargin;
                    if (remaining <= TimeSpan.Zero) return RelationError("base.relation.policyTimeout", "Relation policy evaluation exceeded its bounded lifetime.", ErrorCategory.Store);
                    try { targetPolicy = await policyTask.WaitAsync(remaining, cancellationToken).ConfigureAwait(false); }
                    catch { Observe(policyTask); throw; }
                }
                catch (TimeoutException)
                {
                    return RelationError("base.relation.policyTimeout", "Relation policy evaluation exceeded its bounded lifetime.", ErrorCategory.Store);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization);
                }
                if (!targetPolicy.IsSuccess() || targetPolicy.Value?.Decision.Effect != PolicyEffect.Allow ||
                    !BaseRecordFilterMatcher.Matches(target.Value, targetPolicy.Value.EffectiveRecordFilter))
                    return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization);
            }
        }
        return null;
    }

    private static bool TryRelationIds(RecordPayload payload, string name, RelationDefinition relation, out RecordId[] ids, out string? code)
    {
        ids = []; code = null;
        if (payload.Fields is null || !payload.Fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            if (relation.Required || relation.LocalMultiplicity == BaseRelationMultiplicity.ExactlyOne) { code = "base.relation.cardinalityInvalid"; return false; }
            return true;
        }
        if (relation.LocalMultiplicity == BaseRelationMultiplicity.Many)
        {
            if (value.ValueKind != JsonValueKind.Array) { code = "base.relation.invalid"; return false; }
            var values = new List<RecordId>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())) { code = "base.relation.invalid"; return false; }
                string id = item.GetString()!;
                if (!unique.Add(id)) { code = "base.relation.cardinalityInvalid"; return false; }
                values.Add(new RecordId(id));
            }
            if (relation.MinimumCount is int minimum && values.Count < minimum ||
                relation.MaximumCount is int maximum && values.Count > maximum)
            {
                code = "base.relation.cardinalityInvalid";
                return false;
            }
            ids = values.ToArray(); return true;
        }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) { code = "base.relation.invalid"; return false; }
        ids = [new RecordId(value.GetString()!)]; return true;
    }

    private static OperationStatus RelationStatus(BaseError error) => error.Category switch
    {
        ErrorCategory.Validation => OperationStatus.ValidationFailed,
        ErrorCategory.Unsupported => OperationStatus.Unsupported,
        ErrorCategory.Store => OperationStatus.StoreError,
        _ => OperationStatus.PolicyDenied
    };

    private static BaseError RelationError(string code, string message, ErrorCategory category) => new() { Code = code, Message = message, Category = category };

    private static void Observe(Task task) => _ = task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private BaseMutationAttempt NormalizeSession(
        BaseMutationCommand command,
        OperationResult<RecordMutationSessionResult> result,
        BasePolicyEvaluation? policyResult,
        RecordUpsertOutcome? upsertOutcome)
    {
        var normalized = normalizer.NormalizeStoreResult(result, command.Context);
        if (!normalized.IsSuccess() || normalized.Value is null)
            return FromFailure(command, normalized, providerError: true);

        var mutation = normalized.Value.Mutation;
        if (!ValidMutationFact(command, normalized.Value))
        {
            return Failure(command, OperationStatus.StoreError, Error(
                "base.runtime.store.malformedMutationFact",
                "The store returned an inconsistent mutation fact.",
                ErrorCategory.Store));
        }

        if (mutation.UpsertOutcome != upsertOutcome)
        {
            return Failure(command, OperationStatus.StoreError, Error(
                "base.runtime.store.malformedMutationFact",
                "The store returned an inconsistent mutation fact.",
                ErrorCategory.Store));
        }

        return new BaseMutationAttempt
        {
            Command = command,
            Status = normalized.Status,
            Mutation = mutation,
            Policy = policyResult,
            Revision = normalized.Revision
        };
    }

    private static bool ValidMutationFact(
        BaseMutationCommand command,
        RecordMutationSessionResult sessionResult)
    {
        var mutation = sessionResult.Mutation;
        if (mutation.Event is null
            || !string.Equals(mutation.Event.EventId, command.EventId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(mutation.Event.Type)
            || !Enum.IsDefined(mutation.Event.Guarantee)
            || !string.Equals(mutation.ItemId, command.ItemId, StringComparison.Ordinal)
            || !string.Equals(mutation.Collection.Id, command.Collection.Id, StringComparison.Ordinal)
            || mutation.RequestedOperation != command.Kind)
        {
            return false;
        }

        var expectedCommitted = command.Kind switch
        {
            BaseRecordMutationKind.Create => BaseCommittedRecordMutationKind.Create,
            BaseRecordMutationKind.Patch => BaseCommittedRecordMutationKind.Patch,
            BaseRecordMutationKind.Replace => BaseCommittedRecordMutationKind.Replace,
            BaseRecordMutationKind.Delete => BaseCommittedRecordMutationKind.Delete,
            BaseRecordMutationKind.Upsert when mutation.UpsertOutcome == RecordUpsertOutcome.Created
                => BaseCommittedRecordMutationKind.Create,
            BaseRecordMutationKind.Upsert when command.Upsert?.UpdateMode == RecordUpsertUpdateMode.Patch
                && mutation.UpsertOutcome == RecordUpsertOutcome.Updated
                => BaseCommittedRecordMutationKind.Patch,
            BaseRecordMutationKind.Upsert when mutation.UpsertOutcome == RecordUpsertOutcome.Updated
                => BaseCommittedRecordMutationKind.Replace,
            _ => (BaseCommittedRecordMutationKind)(-1)
        };
        if (mutation.CommittedOperation != expectedCommitted
            || !string.Equals(mutation.Event.Type, EventType(expectedCommitted), StringComparison.Ordinal))
        {
            return false;
        }

        if (command.Kind != BaseRecordMutationKind.Upsert && mutation.UpsertOutcome is not null)
            return false;

        var targetId = command.RecordId ?? command.Upsert?.Id ?? command.Create?.RequestedId;
        if (!ValidRecord(mutation.Before, command.CollectionId, targetId)
            || !ValidRecord(mutation.After, command.CollectionId, targetId))
        {
            return false;
        }

        return expectedCommitted switch
        {
            BaseCommittedRecordMutationKind.Create =>
                mutation.Before is null
                && mutation.After is not null
                && mutation.Delete is null
                && sessionResult.Record == mutation.After
                && sessionResult.Delete is null,
            BaseCommittedRecordMutationKind.Patch or BaseCommittedRecordMutationKind.Replace =>
                mutation.Before is not null
                && mutation.After is not null
                && mutation.Delete is null
                && sessionResult.Record == mutation.After
                && sessionResult.Delete is null,
            BaseCommittedRecordMutationKind.Delete =>
                mutation.Before is not null
                && mutation.After is null
                && mutation.Delete is { Deleted: true }
                && sessionResult.Record is null
                && sessionResult.Delete == mutation.Delete
                && mutation.Delete.Id == mutation.Before.Id,
            _ => false
        };
    }

    private static bool ValidRecord(
        RecordEnvelope? record,
        string collectionId,
        RecordId? targetId) =>
        record is null
        || string.Equals(record.CollectionId, collectionId, StringComparison.Ordinal)
        && (targetId is null || record.Id == targetId.Value);

    private static string EventType(BaseCommittedRecordMutationKind operation) =>
        operation switch
        {
            BaseCommittedRecordMutationKind.Create => BaseEventTypes.RecordCreated,
            BaseCommittedRecordMutationKind.Patch => BaseEventTypes.RecordPatched,
            BaseCommittedRecordMutationKind.Replace => BaseEventTypes.RecordUpdated,
            BaseCommittedRecordMutationKind.Delete => BaseEventTypes.RecordDeleted,
            _ => string.Empty
        };

    private static RecordMutationSessionContext SessionContext(
        BaseMutationCommand command,
        BaseRecordMutationKind requested,
        string[]? changedFields = null) => new()
    {
        ItemId = command.ItemId,
        RequestedOperation = requested,
        EventId = command.EventId,
        Operation = command.Context,
        ChangedFields = changedFields
    };

    private AtomicMutationProcessingResult Failed(BaseError error) =>
        new(AtomicMutationProcessingOutcome.Failed, _attempts
            .Where(static attempt => attempt.Mutation is not null)
            .Select(static attempt => attempt.Mutation!)
            .ToArray(), error);

    private static BaseMutationAttempt FromFailure<T>(
        BaseMutationCommand command,
        OperationResult<T> result,
        bool providerError = false)
    {
        if (command.Kind == BaseRecordMutationKind.Upsert
            && (result.Status is OperationStatus.PolicyDenied or OperationStatus.Unauthorized
                || result.Error?.Category is ErrorCategory.Authorization or ErrorCategory.Authentication))
        {
            return Failure(
                command,
                OperationStatus.PolicyDenied,
                Error(
                    "base.runtime.policy.denied",
                    "Policy denied the operation.",
                    ErrorCategory.Authorization),
                providerError: false);
        }

        return Failure(
            command,
            result.Status,
            result.Error ?? Error(
                "base.runtime.batch.itemInvalid",
                "A batch item failed.",
                ErrorCategory.Unexpected),
            providerError);
    }

    private static BaseMutationAttempt Failure(
        BaseMutationCommand command,
        OperationStatus status,
        BaseError error,
        bool providerError = false) => new()
    {
        Command = command,
        Status = status,
        Error = error,
        ProviderError = providerError
    };

    private static OperationResult<T>? EnforceWritePolicy<T>(
        RecordPayload? predicatePayload,
        RecordPayload? changedPayload,
        BasePolicyEvaluation? evaluation)
    {
        if (evaluation?.Decision.Constraints?.WriteCheck is { } writeCheck)
        {
            var outcome = BasePolicyWriteConstraintEvaluator.Evaluate(predicatePayload, writeCheck);
            if (outcome == BasePolicyWriteCheckEvaluation.Unsupported)
                return OperationResults.Unsupported<T>(Error(
                    "base.runtime.policy.writeCheck.unsupported",
                    "Policy write check is not safely evaluable by this runtime.",
                    ErrorCategory.Unsupported));
            if (outcome == BasePolicyWriteCheckEvaluation.Denied)
                return OperationResults.PolicyDenied<T>(Error(
                    "base.runtime.policy.writeCheck.denied",
                    "Policy write check denied the operation.",
                    ErrorCategory.Authorization));
        }

        if (evaluation?.EffectiveWriteMask is not { } mask)
            return null;
        var fields = changedPayload is null ? [] : BasePolicyRuntimeSimulation.PayloadFields(changedPayload);
        var denied = mask.Mode switch
        {
            FieldMaskMode.Unspecified or FieldMaskMode.AllowAll => null,
            FieldMaskMode.DenyAll => fields.FirstOrDefault(),
            FieldMaskMode.IncludeOnly => fields.FirstOrDefault(field => !(mask.Include ?? []).Contains(field, StringComparer.Ordinal)),
            FieldMaskMode.Exclude => fields.FirstOrDefault(field => (mask.Exclude ?? []).Contains(field, StringComparer.Ordinal)),
            _ => fields.FirstOrDefault()
        };
        return denied is null ? null : OperationResults.PolicyDenied<T>(Error(
            "base.runtime.policy.writeMask.denied",
            "Policy write mask does not allow this field to be written.",
            ErrorCategory.Authorization));
    }

    private static BaseError Error(string code, string message, ErrorCategory category) => new()
    {
        Code = code,
        Message = message,
        Category = category
    };
}
