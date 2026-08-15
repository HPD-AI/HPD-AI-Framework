using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed class BaseModuleMutationProcessor<TRequest, TResult>(
    BaseRegisteredModuleMutationDefinition definition,
    BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity,
    TRequest request,
    BaseAtomicMutationIntent intent,
    BaseModuleMutationCaptureExtension extension,
    BaseAtomicMutationExecutionLimits limits,
    IReadOnlyDictionary<string, CollectionDefinition> collections,
    PrincipalContext principal,
    OperationContext operation,
    IBaseSchemaValidator schemaValidator,
    IBasePolicyOrchestrator policy,
    IBaseResultNormalizer normalizer,
    BaseSubjectContractRegistry subjects) : IAtomicMutationProcessor
{
    internal BaseModuleMutationExecutionResult<TResult>? Result { get; private set; }

    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession provider,
        CancellationToken cancellationToken = default)
    {
        var captureRequest = new BaseAtomicMutationCaptureRequest
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
            Intent = intent,
            Module = extension,
            Limits = limits,
        };
        OperationResult<BaseCapturedAtomicMutationAuthority> captured = await provider
            .CaptureAtomicMutationAuthorityAsync(captureRequest, cancellationToken).ConfigureAwait(false);
        if (!captured.IsSuccess() || captured.Value is null)
            return Failed(captured.Error ?? Error(BaseModuleMutationErrorCodes.StoreError, ErrorCategory.Store));
        BaseCapturedAtomicMutationAuthority evidence = captured.Value;
        if (!CapturedMatches(evidence))
            return Failed(Error("base.moduleMutation.captureEvidenceInvalid", ErrorCategory.Store));

        var evaluator = new BaseModuleProgramEvaluator<TRequest, TResult>(definition, identity, request, evidence, collections);
        var increments = ImmutableArray.CreateBuilder<BaseModuleGenerationIncrement>();
        var selectedStatements = ImmutableArray.CreateBuilder<BaseModuleStatement>();
        var comparisons = ImmutableArray.CreateBuilder<BaseModuleGenerationComparison>();
        foreach (BaseModuleGenerationCaptureRequest generation in extension.Generations)
        {
            if (generation.Absence == BaseModuleGenerationAbsenceBehavior.RequireExisting)
                comparisons.Add(new BaseModuleGenerationComparison { CaptureOrdinal = generation.Ordinal, Kind = BaseModuleGenerationComparisonKind.MustExist });
            else if (generation.Absence == BaseModuleGenerationAbsenceBehavior.RequireMissing)
                comparisons.Add(new BaseModuleGenerationComparison { CaptureOrdinal = generation.Ordinal, Kind = BaseModuleGenerationComparisonKind.MustBeMissing });
        }
        try
        {
            if (!EvaluateBlock(definition.Template.Body, evaluator, increments, selectedStatements, out BaseError? programError))
                return Failed(programError!);
        }
        catch (OverflowException) { return Failed(Error(BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation)); }
        catch { return Failed(Error("base.moduleMutation.programInvalid", ErrorCategory.Validation)); }

        OperationResult<(BaseMutationCommand[] Commands, ImmutableArray<BaseModuleMutationItemCaptureBinding> Bindings)> commandResult =
            await BuildCommandsAsync(selectedStatements.ToImmutable(), evaluator, evidence, cancellationToken).ConfigureAwait(false);
        if (!commandResult.IsSuccess() || commandResult.Value == default)
            return Failed(commandResult.Error ?? Error(BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation));
        var recordPlanner = new DefaultBaseMutationProcessor(
            commandResult.Value.Commands, principal, policy, normalizer, collections.Values.ToArray(), limits, intent.Authority, subjects);
        IReadOnlyDictionary<int, BaseCapturedMutationItem> capturedItems = BuildCapturedItems(commandResult.Value.Commands, evidence);
        OperationResult<BaseFinalizedRecordMutationPlan> recordPlan = await recordPlanner
            .FinalizeCapturedCommandsAsync(capturedItems, cancellationToken).ConfigureAwait(false);
        if (!recordPlan.IsSuccess() || recordPlan.Value is null)
            return Failed(recordPlan.Error ?? Error(BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation));

        string recordPlanDigest = DefaultBaseMutationProcessor.ComputePlanDigest(
            intent.IntentDigest, evidence.CaptureDigest, recordPlan.Value.Items, recordPlan.Value.SubjectValidations);
        string planDigest = Digest(recordPlanDigest, extension.RequestDigest, evidence.CaptureDigest,
            string.Join(';', evaluator.Decisions.Select(static value => $"{value.EvaluationOrdinal}:{value.Kind}:{value.DecisionId}:{value.SelectedTrue}")),
            string.Join(';', increments.Select(static value => $"{value.CaptureOrdinal}:{value.CreateIfAbsent}")));
        var plan = new BaseAtomicMutationPlan
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
            IntentDigest = intent.IntentDigest,
            CaptureDigest = evidence.CaptureDigest,
            Authority = intent.Authority,
            Items = recordPlan.Value.Items,
            SubjectValidations = recordPlan.Value.SubjectValidations,
            Module = new BaseFinalizedModuleMutationExtension
            {
                OperationId = definition.Id, OperationVersion = definition.Version,
                OperationChecksum = extension.OperationChecksum, Decisions = evaluator.Decisions,
                ItemBindings = commandResult.Value.Bindings, Comparisons = comparisons.ToImmutable(), Increments = increments.ToImmutable(),
                ResultProjectionDigest = Digest(definition.Id, "result", Convert.ToHexString(definition.Checksum.ToArray())),
            },
            Limits = limits,
            PlanDigest = planDigest,
        };
        OperationResult<BasePreparedAtomicMutation> prepared = await provider
            .PrepareAtomicMutationAsync(evidence, plan, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess() || prepared.Value is null || !PreparedMatches(plan, evidence, prepared.Value))
            return Failed(prepared.Error ?? Error("base.moduleMutation.preparedEvidenceInvalid", ErrorCategory.Store));
        OperationResult<BaseProvisionalAppliedAtomicMutation> applied = await provider
            .ApplyPreparedAtomicMutationAsync(prepared.Value, cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess() || applied.Value is null || !AppliedMatches(plan, applied.Value))
            return Failed(applied.Error ?? Error("base.moduleMutation.appliedEvidenceInvalid", ErrorCategory.Store));

        IReadOnlyDictionary<string, BaseModuleCommittedGeneration> committedGenerations = applied.Value.Generations
            .ToDictionary(static value => value.CaptureId, StringComparer.Ordinal);
        IReadOnlyDictionary<string, BaseRecordMutationFact> committedStatements = applied.Value.Facts
            .Select((fact, index) => (commandResult.Value.Commands[index].ItemId, Fact: fact.MaterializeOwned()))
            .ToDictionary(static value => value.ItemId, static value => value.Fact, StringComparer.Ordinal);
        TResult typed;
        ImmutableArray<byte> resultBytes;
        try { typed = evaluator.ProjectResult(definition.Template.Result, committedStatements, committedGenerations, out resultBytes); }
        catch { return Failed(Error("base.moduleMutation.resultInvalid", ErrorCategory.Validation)); }
        var moduleReceipt = new BaseModuleMutationReceiptResult
        {
            OperationId = definition.Id, OperationVersion = definition.Version,
            Disposition = BaseMutationRequestDisposition.Committed, Outcome = BaseModuleMutationOutcome.Committed,
            Generations = applied.Value.Generations.Select(static value => value with { }).ToImmutableArray(),
            CanonicalResultBytes = resultBytes.ToArray().ToImmutableArray(),
        };
        var receipt = new BaseAtomicReceiptResult
        {
            Kind = BaseAtomicReceiptResultKind.ModuleMutation,
            Mutations = applied.Value.Facts.Select(static value => BaseOwnedMutationFact.FromCanonicalBytes(value.CopyCanonicalBytes(), value.CodecVersion)).ToImmutableArray(),
            ModuleMutation = moduleReceipt,
        };
        long receiptBytes = JsonSerializer.SerializeToUtf8Bytes(
            BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire).LongLength;
        BaseProvisionalAtomicMutationAccounting prior = applied.Value.Accounting;
        long transient = checked(prior.TransientBytes + receiptBytes + resultBytes.Length);
        if (receiptBytes > limits.MaximumReceiptBytes || resultBytes.Length > limits.MaximumResultBytes || transient > limits.MaximumTransientBytes)
            return Failed(Error(BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation));
        var finalization = new BaseAtomicMutationCommitFinalization
        {
            PlanDigest = plan.PlanDigest, Receipt = receipt, CanonicalResultBytes = resultBytes,
            Accounting = new BaseAtomicCommitAccounting
            {
                WrittenBytes = prior.WrittenBytes, GenerationBytes = prior.GenerationBytes, FactBytes = prior.FactBytes,
                JournalBytes = prior.JournalBytes, ReceiptBytes = receiptBytes, ResultBytes = resultBytes.Length,
                RelationChecks = prior.RelationChecks, UniqueConstraintChecks = prior.UniqueConstraintChecks,
                AuthorityReads = prior.AuthorityReads, ReadIntervals = prior.ReadIntervals,
                SelectedBytes = prior.SelectedBytes, EvidenceBytes = prior.EvidenceBytes, TransientBytes = transient,
            },
        };
        Result = new BaseModuleMutationExecutionResult<TResult>
        {
            Disposition = BaseMutationRequestDisposition.Committed,
            Outcome = BaseModuleMutationOutcome.Committed,
            Result = typed,
        };
        return new AtomicMutationProcessingResult(finalization);
    }

    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseAtomicReceiptResult committedResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseModuleMutationReceiptResult? module = committedResult.ModuleMutation;
        if (committedResult.Kind != BaseAtomicReceiptResultKind.ModuleMutation || module is null
            || !string.Equals(module.OperationId, definition.Id, StringComparison.Ordinal)
            || module.OperationVersion != definition.Version)
            return ValueTask.FromResult(Failed(Error(BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization)));
        try
        {
            TResult? typed = JsonSerializer.Deserialize(module.CanonicalResultBytes.AsSpan(), identity.ResultTypeInfo);
            if (typed is null) throw new JsonException();
            Result = new BaseModuleMutationExecutionResult<TResult>
            {
                Disposition = BaseMutationRequestDisposition.Duplicate,
                Outcome = BaseModuleMutationOutcome.Duplicate,
                Result = typed,
            };
            return ValueTask.FromResult(new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, committedResult));
        }
        catch { return ValueTask.FromResult(Failed(Error(BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization))); }
    }

    private bool EvaluateBlock(
        BaseModuleMutationBlock block,
        BaseModuleProgramEvaluator<TRequest, TResult> evaluator,
        ImmutableArray<BaseModuleGenerationIncrement>.Builder increments,
        ImmutableArray<BaseModuleStatement>.Builder selectedStatements,
        out BaseError? error)
    {
        foreach (BaseModuleStatement statement in block.Statements)
        {
            switch (statement)
            {
                case BaseModuleRequireStatement requirement when !evaluator.Guard(requirement.GuardId):
                    error = Error("base.moduleMutation.requirementFailed", ErrorCategory.Validation); return false;
                case BaseModuleRequireStatement: break;
                case BaseModuleIncrementGenerationStatement increment:
                    BaseModuleGenerationCapture capture = definition.Template.Captures.OfType<BaseModuleGenerationCapture>()
                        .Single(value => string.Equals(value.Id, increment.CaptureId, StringComparison.Ordinal));
                    int ordinal = extension.Generations.Single(value => string.Equals(value.CaptureId, capture.Id, StringComparison.Ordinal)).Ordinal;
                    increments.Add(new BaseModuleGenerationIncrement { CaptureOrdinal = ordinal, CreateIfAbsent = increment.CreateIfAbsent });
                    selectedStatements.Add(increment);
                    break;
                case BaseModuleIfStatement branch:
                    bool selected = evaluator.Guard(branch.GuardId);
                    evaluator.RecordIfDecision(branch.Id, selected);
                    if (!EvaluateBlock(selected ? branch.WhenTrue : branch.WhenFalse, evaluator, increments, selectedStatements, out error)) return false;
                    break;
                case BaseModuleCreateStatement or BaseModulePatchStatement or BaseModuleReplaceStatement
                    or BaseModuleDeleteStatement or BaseModuleUpsertStatement:
                    selectedStatements.Add(statement);
                    break;
                default:
                    error = Error(BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation); return false;
            }
        }
        error = null; return true;
    }

    private async ValueTask<OperationResult<(BaseMutationCommand[] Commands, ImmutableArray<BaseModuleMutationItemCaptureBinding> Bindings)>> BuildCommandsAsync(
        ImmutableArray<BaseModuleStatement> statements,
        BaseModuleProgramEvaluator<TRequest, TResult> evaluator,
        BaseCapturedAtomicMutationAuthority evidence,
        CancellationToken cancellationToken)
    {
        BaseModuleStatement[] writes = statements.Where(static statement => statement is not BaseModuleIncrementGenerationStatement).ToArray();
        var commands = new BaseMutationCommand[writes.Length];
        var bindings = ImmutableArray.CreateBuilder<BaseModuleMutationItemCaptureBinding>(writes.Length);
        for (int index = 0; index < writes.Length; index++)
        {
            BaseModuleStatement statement = writes[index];
            string collectionId = statement switch
            {
                BaseModuleCreateStatement value => value.CollectionId,
                BaseModulePatchStatement value => value.CollectionId,
                BaseModuleReplaceStatement value => value.CollectionId,
                BaseModuleDeleteStatement value => value.CollectionId,
                BaseModuleUpsertStatement value => value.CollectionId,
                _ => throw new InvalidOperationException(),
            };
            if (!collections.TryGetValue(collectionId, out CollectionDefinition? collection))
                return InvalidCommands();
            BaseModuleValueExpression idExpression = statement switch
            {
                BaseModuleCreateStatement value => value.RecordId,
                BaseModulePatchStatement value => value.RecordId,
                BaseModuleReplaceStatement value => value.RecordId,
                BaseModuleDeleteStatement value => value.RecordId,
                BaseModuleUpsertStatement value => value.RecordId,
                _ => throw new InvalidOperationException(),
            };
            string? idText = evaluator.Evaluate(idExpression).Value.GetString();
            if (string.IsNullOrEmpty(idText)) return InvalidCommands();
            var recordId = new RecordId(idText);
            BaseCapturedModuleRecord capture = evidence.ModuleRecords.SingleOrDefault(value =>
                string.Equals(value.CollectionId, collectionId, StringComparison.Ordinal) && value.RecordId == recordId)
                ?? throw new InvalidOperationException("Every selected record write must bind one declared capture.");
            bindings.Add(new BaseModuleMutationItemCaptureBinding { MutationOrdinal = index, RecordCaptureOrdinal = capture.Ordinal });

            RecordPayload? createPayload = null;
            RecordPayload? updatePayload = null;
            RevisionToken? expected = null;
            BaseRecordMutationKind kind;
            RecordUpsertUpdateMode upsertMode = RecordUpsertUpdateMode.Patch;
            switch (statement)
            {
                case BaseModuleCreateStatement create:
                    kind = BaseRecordMutationKind.Create;
                    createPayload = Payload(evaluator.Object(create.Payload, collection));
                    break;
                case BaseModulePatchStatement patch:
                    kind = BaseRecordMutationKind.Patch;
                    updatePayload = Payload(evaluator.Object(patch.Patch, collection));
                    expected = Revision(patch.ExpectedRevision, evaluator);
                    break;
                case BaseModuleReplaceStatement replace:
                    kind = BaseRecordMutationKind.Replace;
                    updatePayload = Payload(evaluator.Object(replace.Payload, collection));
                    expected = Revision(replace.ExpectedRevision, evaluator);
                    break;
                case BaseModuleDeleteStatement delete:
                    kind = BaseRecordMutationKind.Delete;
                    expected = Revision(delete.ExpectedRevision, evaluator);
                    break;
                case BaseModuleUpsertStatement upsert:
                    kind = BaseRecordMutationKind.Upsert;
                    createPayload = Payload(evaluator.Object(upsert.Create, collection));
                    updatePayload = Payload(evaluator.Object(upsert.Update, collection));
                    upsertMode = upsert.UpdateMode;
                    expected = Revision(upsert.ExpectedRevision, evaluator);
                    break;
                default: return InvalidCommands();
            }

            BaseValidatedPayload? validatedCreate = null;
            BaseValidatedPayload? validatedUpdate = null;
            if (createPayload is not null)
            {
                OperationResult<BaseValidatedPayload> validation = await schemaValidator.ValidateCreateAsync(new BasePayloadValidationRequest
                {
                    Collection = collection, Principal = principal, Operation = operation, Payload = createPayload,
                }, cancellationToken).ConfigureAwait(false);
                if (!validation.IsSuccess() || validation.Value is null) return CommandFailure(validation);
                validatedCreate = validation.Value;
            }
            if (updatePayload is not null)
            {
                OperationResult<BaseValidatedPayload> validation = kind == BaseRecordMutationKind.Patch
                    || kind == BaseRecordMutationKind.Upsert && upsertMode == RecordUpsertUpdateMode.Patch
                    ? await schemaValidator.ValidatePatchAsync(new BasePayloadValidationRequest
                    {
                        Collection = collection, Principal = principal, Operation = operation, Patch = updatePayload,
                    }, cancellationToken).ConfigureAwait(false)
                    : await schemaValidator.ValidateReplaceAsync(new BasePayloadValidationRequest
                    {
                        Collection = collection, Principal = principal, Operation = operation, Payload = updatePayload,
                    }, cancellationToken).ConfigureAwait(false);
                if (!validation.IsSuccess() || validation.Value is null) return CommandFailure(validation);
                validatedUpdate = validation.Value;
            }
            commands[index] = new BaseMutationCommand
            {
                Index = index, ItemId = statement.Id, CollectionId = collectionId, Kind = kind,
                Collection = collection, Context = operation, EventId = Guid.NewGuid().ToString("N"), Store = null!,
                Create = createPayload is null ? null : new RecordCreateRequest { Payload = createPayload, RequestedId = recordId },
                RecordId = recordId,
                Patch = kind == BaseRecordMutationKind.Patch ? new RecordPatchRequest { Patch = updatePayload!, ExpectedRevision = expected } : null,
                Replace = kind == BaseRecordMutationKind.Replace ? new RecordReplaceRequest { Payload = updatePayload!, ExpectedRevision = expected } : null,
                Delete = kind == BaseRecordMutationKind.Delete ? new RecordDeleteRequest { ExpectedRevision = expected, ReturnPrevious = false } : null,
                Upsert = kind == BaseRecordMutationKind.Upsert ? new RecordUpsertRequest
                {
                    Id = recordId, CreatePayload = createPayload!, UpdatePayload = updatePayload!, UpdateMode = upsertMode,
                    Condition = RecordUpsertExistenceCondition.Any, ExpectedRevision = expected,
                } : null,
                CreatePayload = validatedCreate, UpdatePayload = validatedUpdate,
            };
        }
        return OperationResults.Ok((commands, bindings.MoveToImmutable()));
    }

    private IReadOnlyDictionary<int, BaseCapturedMutationItem> BuildCapturedItems(
        BaseMutationCommand[] commands,
        BaseCapturedAtomicMutationAuthority evidence)
    {
        var result = new Dictionary<int, BaseCapturedMutationItem>();
        for (int ordinal = 0; ordinal < commands.Length; ordinal++)
        {
            BaseMutationCommand command = commands[ordinal];
            BaseCapturedModuleRecord capture = evidence.ModuleRecords.Single(value =>
                string.Equals(value.CollectionId, command.CollectionId, StringComparison.Ordinal)
                && value.RecordId == command.RecordId);
            result.Add(ordinal, new BaseCapturedMutationItem
            {
                Ordinal = ordinal, CollectionId = command.CollectionId, RecordId = command.RecordId!.Value,
                Current = capture.Current is null ? null : RecordCloneHelpers.CloneEnvelope(capture.Current),
                Disposition = command.Kind switch
                {
                    BaseRecordMutationKind.Create => BaseCapturedMutationDisposition.Create,
                    BaseRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
                    BaseRecordMutationKind.Upsert when capture.Current is null => BaseCapturedMutationDisposition.Create,
                    _ => BaseCapturedMutationDisposition.Update,
                },
                RelationTargets = evidence.ModuleRelationTargets.Where(value => string.Equals(value.SourceStatementId, command.ItemId, StringComparison.Ordinal))
                    .Select(static value => new BaseCapturedRelationTarget
                    {
                        SourceFieldId = value.SourceFieldId, TargetCollectionId = value.TargetCollectionId,
                        TargetRecordId = value.TargetRecordId, Current = value.Current,
                    }).ToImmutableArray(),
            });
        }
        return result;
    }

    private static RecordPayload Payload(BaseModuleProgramValue value)
    {
        if (!value.Present || value.Value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = value.Value.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value.Clone(), StringComparer.Ordinal),
        };
    }

    private static RevisionToken? Revision(BaseModuleValueExpression? expression, BaseModuleProgramEvaluator<TRequest, TResult> evaluator) =>
        expression is null ? null : new RevisionToken(evaluator.Evaluate(expression).Value.GetString() ?? throw new InvalidOperationException());

    private static OperationResult<(BaseMutationCommand[], ImmutableArray<BaseModuleMutationItemCaptureBinding>)> InvalidCommands() =>
        OperationResults.ValidationFailed<(BaseMutationCommand[], ImmutableArray<BaseModuleMutationItemCaptureBinding>)>(
            Error(BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation));

    private static OperationResult<(BaseMutationCommand[], ImmutableArray<BaseModuleMutationItemCaptureBinding>)> CommandFailure(
        OperationResult<BaseValidatedPayload> value) => new() { Status = value.Status, Error = value.Error };

    private bool CapturedMatches(BaseCapturedAtomicMutationAuthority value) =>
        value.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
        && string.Equals(value.IntentDigest, intent.IntentDigest, StringComparison.Ordinal)
        && value.ModuleRecords.Length == extension.Records.Length
        && value.Generations.Length == extension.Generations.Length
        && value.ReadIntervals.Length == value.Accounting.ReadIntervals;

    private static bool PreparedMatches(BaseAtomicMutationPlan plan, BaseCapturedAtomicMutationAuthority captured, BasePreparedAtomicMutation prepared) =>
        prepared.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
        && string.Equals(prepared.PlanDigest, plan.PlanDigest, StringComparison.Ordinal)
        && prepared.Generations.Length == captured.Generations.Length
        && prepared.Accounting.GenerationReads == captured.Generations.Length;

    private static bool AppliedMatches(BaseAtomicMutationPlan plan, BaseProvisionalAppliedAtomicMutation applied) =>
        applied.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
        && string.Equals(applied.PlanDigest, plan.PlanDigest, StringComparison.Ordinal)
        && applied.Facts.Length == plan.Items.Length;

    private static AtomicMutationProcessingResult Failed(BaseError error) => new(
        AtomicMutationProcessingOutcome.Failed, [], error);
    private static BaseError Error(string code, ErrorCategory category) => new()
    {
        Code = code, Message = "The registered module mutation could not be completed.", Category = category,
    };
    private static string Digest(params string[] values) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', values)))).ToLowerInvariant();
}
