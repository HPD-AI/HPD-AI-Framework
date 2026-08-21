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
    BaseActivationGuard? activationGuard,
    BaseAtomicMutationExecutionLimits limits,
    IReadOnlyDictionary<string, CollectionDefinition> collections,
    PrincipalContext principal,
    OperationContext operation,
    BasePolicyEvaluation operationPolicy,
    IBaseSchemaValidator schemaValidator,
    IBasePolicyOrchestrator policy,
    IBaseResultNormalizer normalizer,
    BaseSubjectContractRegistry subjects,
    BaseSubjectLifecycleRegistry lifecycleConsumers,
    BaseSubjectRetirementRegistry retirement,
    BaseTransactionalActivationCandidate? transactionalActivation = null) : IAtomicMutationProcessor
{
    internal BaseModuleMutationExecutionResult<TResult>? Result { get; private set; }

    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession provider,
        CancellationToken cancellationToken = default)
    {
        var captureRequest = new BaseAtomicExecutionRequest
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
            Intent = intent,
            Module = extension,
            ActivationGuard = activationGuard,
            SubjectRetirement = CreateRetirementCapture(extension),
            Limits = limits,
        };
        OperationResult<BaseCapturedAtomicExecution> captured = await provider
            .CaptureAtomicExecutionAsync(captureRequest, cancellationToken).ConfigureAwait(false);
        if (!captured.IsSuccess() || captured.Value is null)
            return Failed(captured.Error ?? Error(BaseModuleMutationErrorCodes.StoreError, ErrorCategory.Store));
        BaseCapturedAtomicExecution evidence = captured.Value;
        if (!CapturedMatches(intent, extension, limits, evidence))
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
            commandResult.Value.Commands, principal, policy, normalizer, collections.Values.ToArray(), limits, intent.Authority, subjects, lifecycleConsumers, retirement);
        ImmutableArray<BaseCapturedSubjectRetirementProjection> mappedRetirement = evidence.SubjectRetirement
            .Select(captured => captured with
            {
                SourceMutationOrdinal = commandResult.Value.Bindings.Single(binding =>
                    binding.RecordCaptureOrdinal == captured.SourceMutationOrdinal).MutationOrdinal,
            }).ToImmutableArray();
        recordPlanner.AdoptCapturedRetirement(mappedRetirement);
        IReadOnlyDictionary<int, BaseCapturedMutationItem> capturedItems = BuildCapturedItems(commandResult.Value.Commands, evidence);
        OperationResult<BaseFinalizedRecordMutationPlan> recordPlan = await recordPlanner
            .FinalizeCapturedCommandsAsync(capturedItems, cancellationToken).ConfigureAwait(false);
        if (!recordPlan.IsSuccess() || recordPlan.Value is null)
            return Failed(recordPlan.Error ?? Error(BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation));
        if (!BaseAtomicPolicyAuthority.IsAdmissible([operationPolicy, .. recordPlan.Value.PolicyEvaluations]))
            return Failed(Error(BaseModuleMutationErrorCodes.Unauthorized, ErrorCategory.Authorization));

        BaseAtomicPolicyAuthorityDigest policyDigest = BaseAtomicPolicyAuthority.Compute(
            intent.Authority.ApplicationId, $"{definition.Id}:{definition.Version}",
            [operationPolicy, .. recordPlan.Value.PolicyEvaluations, .. recordPlan.Value.RelationPolicies.Select(static value => value.Evaluation)]);
        ImmutableArray<BaseAuthorizedModuleRelationTarget> authorizedRelations = recordPlan.Value.RelationPolicies
            .Select(relation =>
            {
                BaseModuleRelationTargetCaptureRequest capture = extension.RelationTargets.Single(value =>
                    string.Equals(value.SourceStatementId, relation.SourceStatementId, StringComparison.Ordinal)
                    && string.Equals(value.SourceFieldId, relation.SourceFieldId, StringComparison.Ordinal)
                    && string.Equals(value.TargetCollection.Id, relation.TargetCollectionId, StringComparison.Ordinal)
                    && value.TargetRecordId == relation.TargetRecordId);
                return new BaseAuthorizedModuleRelationTarget
                {
                    CaptureOrdinal = capture.Ordinal,
                    SourceStatementId = relation.SourceStatementId,
                    SourceFieldId = relation.SourceFieldId,
                    TargetCollectionId = relation.TargetCollectionId,
                    TargetRecordId = relation.TargetRecordId,
                    PolicyAuthorityDigest = BaseAtomicPolicyAuthority.Compute(
                        intent.Authority.ApplicationId,
                        $"{definition.Id}:{definition.Version}:relation:{relation.SourceStatementId}:{relation.SourceFieldId}",
                        [relation.Evaluation]),
                };
            })
            .OrderBy(static value => value.CaptureOrdinal)
            .ToImmutableArray();
        ImmutableArray<BaseModuleGenerationIncrement> orderedIncrements = increments.ToImmutable()
            .OrderBy(static value => value.CaptureOrdinal).ToImmutableArray();
        ImmutableArray<BaseModuleGenerationComparison> orderedComparisons = comparisons.ToImmutable()
            .OrderBy(static value => value.CaptureOrdinal).ThenBy(static value => value.Kind).ToImmutableArray();
        string recordPlanDigest = BaseAtomicPolicyAuthority.BindPlanDigest(DefaultBaseMutationProcessor.ComputePlanDigest(
            intent.IntentDigest, evidence.CaptureDigest, recordPlan.Value.Items, recordPlan.Value.SubjectValidations), policyDigest);
        BaseSubjectRetirementProjectionPlan? retirementPlan = recordPlanner.BuildRetirementPlan(recordPlan.Value.Items, retirement);
        BaseFinalizedTextMutationExtension? textPlan = BaseTextAtomicMutationContract.Finalize(recordPlan.Value.Items);
        string planDigest = Digest(recordPlanDigest, extension.RequestDigest, evidence.CaptureDigest,
            string.Join(';', evaluator.Decisions.Select(static value => $"{value.EvaluationOrdinal}:{value.Kind}:{value.DecisionId}:{value.SelectedTrue}")),
            string.Join(';', orderedIncrements.Select(static value => $"{value.CaptureOrdinal}:{value.CreateIfAbsent}")),
            string.Join(';', authorizedRelations.Select(static value =>
                $"{value.CaptureOrdinal}:{value.SourceStatementId}:{value.SourceFieldId}:{value.TargetCollectionId}:{value.TargetRecordId.Value}:{Convert.ToHexString(value.PolicyAuthorityDigest.ToArray())}")),
            retirementPlan?.PlanChecksum ?? string.Empty,
            textPlan is null ? string.Empty : Convert.ToHexString(textPlan.ProjectionDigest.AsSpan()));
        var plan = new BaseFinalizedAtomicExecutionPlan
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
            IntentDigest = intent.IntentDigest,
            CaptureDigest = evidence.CaptureDigest,
            PolicyAuthorityDigest = policyDigest,
            Authority = intent.Authority,
            Items = recordPlan.Value.Items,
            SubjectValidations = recordPlan.Value.SubjectValidations,
            SubjectRetirement = retirementPlan,
            Text = textPlan,
            Module = new BaseFinalizedModuleMutationExtension
            {
                OperationId = definition.Id, OperationVersion = definition.Version,
                OperationChecksum = extension.OperationChecksum, Decisions = evaluator.Decisions,
                ItemBindings = commandResult.Value.Bindings, RelationTargets = authorizedRelations,
                Comparisons = orderedComparisons, Increments = orderedIncrements,
                ResultProjectionDigest = Digest(definition.Id, "result", Convert.ToHexString(definition.Checksum.ToArray())),
            },
            Limits = limits,
            PlanDigest = planDigest,
        };
        BaseFinalizedAtomicExecutionPlan retainedPlan = BaseAtomicMutationOwnership.FreezePlan(plan);
        BaseFinalizedAtomicExecutionPlan providerPlan = BaseAtomicMutationOwnership.FreezePlan(retainedPlan);
        OperationResult<BasePreparedAtomicExecution> prepared = await provider
            .PrepareAtomicExecutionAsync(evidence, providerPlan, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess() || prepared.Value is null || !PreparedMatches(retainedPlan, evidence, prepared.Value))
            return Failed(prepared.Error ?? Error("base.moduleMutation.preparedEvidenceInvalid", ErrorCategory.Store));
        OperationResult<BaseProvisionalAtomicExecution> applied = await provider
            .ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess() || applied.Value is null || !AppliedMatches(retainedPlan, evidence, prepared.Value, applied.Value))
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
        BaseTransactionalActivationCommitEvidence? activationCommit = null;
        if (transactionalActivation is not null)
        {
            OperationResult<BaseTransactionalActivationCommitEvidence> finalizedActivation = await provider
                .FinalizeActivationAsync(new BaseTransactionalActivationFinalization
                {
                    Candidate = transactionalActivation,
                    CanonicalResult = resultBytes,
                    ResultChecksum = SHA256.HashData(resultBytes.AsSpan()).ToImmutableArray(),
                }, cancellationToken).ConfigureAwait(false);
            if (!finalizedActivation.IsSuccess() || finalizedActivation.Value is null)
                return Failed(finalizedActivation.Error ?? Error("base.activation.providerContractInvalid", ErrorCategory.Store));
            activationCommit = finalizedActivation.Value;
        }
        var moduleReceipt = new BaseModuleMutationReceiptResult
        {
            OperationId = definition.Id, OperationVersion = definition.Version,
            Disposition = BaseMutationRequestDisposition.Committed, Outcome = BaseModuleMutationOutcome.Committed,
            Generations = applied.Value.Generations.Select(static value => value with { }).ToImmutableArray(),
            CanonicalResultBytes = resultBytes.ToArray().ToImmutableArray(),
        };
        var receipt = activationCommit is null
            ? new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.ModuleMutation,
                Mutations = applied.Value.Facts.Select(static value => BaseOwnedMutationFact.FromCanonicalBytes(value.CopyCanonicalBytes(), value.CodecVersion)).ToImmutableArray(),
                ModuleMutation = moduleReceipt,
            }
            : new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.ActivationTransactionalOperation,
                Mutations = applied.Value.Facts.Select(static value => BaseOwnedMutationFact.FromCanonicalBytes(value.CopyCanonicalBytes(), value.CodecVersion)).ToImmutableArray(),
                ActivationTransactionalOperation = new BaseActivationTransactionalReceiptResult
                {
                    ActivationId = activationCommit.ActivationId,
                    ActivationGeneration = activationCommit.ActivationGeneration,
                    TargetKind = "moduleMutation",
                    TargetId = definition.Id,
                    TargetVersion = definition.Version,
                    TargetChecksum = Convert.ToHexStringLower(definition.Checksum.ToArray()),
                    Generations = moduleReceipt.Generations,
                    CanonicalResultBytes = resultBytes,
                    ActivationControlChecksum = activationCommit.ControlChecksum,
                },
            };
        long receiptBytes = JsonSerializer.SerializeToUtf8Bytes(
            BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire).LongLength;
        BaseProvisionalAtomicMutationAccounting prior = applied.Value.Accounting;
        long activationEvidenceBytes = activationCommit?.Accounting.EvidenceBytes ?? 0;
        long activationTransientBytes = activationCommit?.Accounting.TransientBytes ?? 0;
        long transient = checked(prior.TransientBytes + receiptBytes + resultBytes.Length + activationTransientBytes);
        if (receiptBytes > limits.MaximumReceiptBytes || resultBytes.Length > limits.MaximumResultBytes || transient > limits.MaximumTransientBytes)
            return Failed(Error(BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation));
        var finalization = new BaseAtomicMutationCommitFinalization
        {
            PlanDigest = retainedPlan.PlanDigest, Receipt = receipt, CanonicalResultBytes = resultBytes,
            Accounting = new BaseAtomicCommitAccounting
            {
                WrittenBytes = prior.WrittenBytes, GenerationBytes = prior.GenerationBytes, FactBytes = prior.FactBytes,
                JournalBytes = prior.JournalBytes, ReceiptBytes = receiptBytes, ResultBytes = resultBytes.Length,
                RelationChecks = prior.RelationChecks, UniqueConstraintChecks = prior.UniqueConstraintChecks,
                AuthorityReads = prior.AuthorityReads, ReadIntervals = prior.ReadIntervals,
                SelectedBytes = prior.SelectedBytes, EvidenceBytes = checked(prior.EvidenceBytes + activationEvidenceBytes), TransientBytes = transient,
                RetirementBarrierReads=prior.RetirementBarrierReads,RetirementAcknowledgementReads=prior.RetirementAcknowledgementReads,RetirementProjections=prior.RetirementProjections,RetirementPublications=prior.RetirementPublications,RetirementEvidenceBytes=prior.RetirementEvidenceBytes,RetirementPublicationBytes=prior.RetirementPublicationBytes,
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

    private BaseSubjectRetirementCaptureExtension? CreateRetirementCapture(BaseModuleMutationCaptureExtension module)
    {
        ImmutableArray<BaseSubjectRetirementProjectionCaptureRequest> projections = [.. module.Records
            .OrderBy(static value => value.Ordinal)
            .Select(capture =>
            {
                BaseGeneratedSubjectRegistration? contract = subjects.All.SingleOrDefault(value =>
                    value.Definition.ValidationPlan.PrivateCollectionId == capture.Collection.Id);
                BaseInstalledSubjectRetirementPolicy? policy = contract is null
                    ? null : retirement.FindPolicy(contract.Definition.Id, contract.Definition.Version);
                return contract is null || policy is null ? null : new BaseSubjectRetirementProjectionCaptureRequest
                {
                    SourceMutationOrdinal = capture.Ordinal,
                    ContractId = contract.Definition.Id,
                    ContractVersion = contract.Definition.Version,
                    ContractChecksum = contract.Checksum,
                    RetirementPolicyChecksum = policy.Definition.PolicyChecksum,
                    AcceptedConsumerSetChecksum = BaseSubjectRetirementRegistry.AcceptedSetChecksum(policy.Definition.AcceptedConsumers),
                };
            })
            .Where(static value => value is not null)
            .Select(static value => value!)];
        return projections.IsEmpty ? null : new BaseSubjectRetirementCaptureExtension { Projections = projections };
    }

    public async ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseAtomicReceiptResult committedResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseModuleMutationReceiptResult? module = committedResult.ModuleMutation;
        if (committedResult.Kind != BaseAtomicReceiptResultKind.ModuleMutation || module is null
            || !string.Equals(module.OperationId, definition.Id, StringComparison.Ordinal)
            || module.OperationVersion != definition.Version)
            return Failed(Error(BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization));
        if (!await BaseModuleReceiptDisclosure.AuthorizeAsync(
                committedResult, definition, identity.ResultBindings, principal, operation, policy, cancellationToken).ConfigureAwait(false))
            return Failed(Error(BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization));
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
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, committedResult);
        }
        catch { return Failed(Error(BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization)); }
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
        BaseCapturedAtomicExecution evidence,
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
        BaseCapturedAtomicExecution evidence)
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

    internal static bool CapturedMatches(
        BaseAtomicMutationIntent intent,
        BaseModuleMutationCaptureExtension extension,
        BaseAtomicMutationExecutionLimits limits,
        BaseCapturedAtomicExecution value)
    {
        if (value.Kind != BaseAtomicMutationExecutionKind.ModuleMutation
            || !string.Equals(value.IntentDigest, intent.IntentDigest, StringComparison.Ordinal)
            || value.CaptureDigest is not { Length: 64 }
            || value.Items.Length != 0
            || value.ModuleRecords.Length != extension.Records.Length
            || value.ModuleRelationTargets.Length != extension.RelationTargets.Length
            || value.Generations.Length != extension.Generations.Length
            || value.Authority.ApplicationId != intent.Authority.ApplicationId
            || value.Authority.StoreInstanceId != intent.Authority.StoreInstanceId
            || value.Authority.RestoreEpoch != intent.Authority.RestoreEpoch
            || value.Authority.SchemaGeneration != intent.Authority.SchemaGeneration
            || !value.Authority.Collections.SequenceEqual(intent.Authority.Collections)
            || !Enum.IsDefined(value.Authority.Isolation)
            || value.Authority.TransactionEvidenceToken.IsDefaultOrEmpty)
            return false;

        long selectedBytes = 0;
        long relationBytes = 0;
        long generationBytes = 0;
        int intervalOrdinal = 0;
        for (int index = 0; index < extension.Records.Length; index++)
        {
            BaseModuleRecordCaptureRequest expected = extension.Records[index];
            BaseCapturedModuleRecord actual = value.ModuleRecords[index];
            if (expected.Ordinal != index || actual.Ordinal != index
                || actual.CaptureId != expected.CaptureId
                || actual.CollectionId != expected.Collection.Id
                || actual.RecordId != expected.RecordId
                || actual.Exists != (actual.Current is not null)
                || actual.Current is not null && (actual.Current.CollectionId != expected.Collection.Id || actual.Current.Id != expected.RecordId)
                || expected.Presence == BaseModuleCapturePresence.RequirePresent && !actual.Exists
                || expected.Presence == BaseModuleCapturePresence.RequireMissing && actual.Exists
                || !ExactInterval(value.ReadIntervals, intervalOrdinal++, $"collection:{expected.Collection.Id}:record", Encoding.UTF8.GetBytes(expected.RecordId.Value)))
                return false;
            if (actual.Current is not null)
                selectedBytes = checked(selectedBytes + JsonSerializer.SerializeToUtf8Bytes(actual.Current, HPDBaseJsonSerializerContext.Default.RecordEnvelope).LongLength);
        }
        for (int index = 0; index < extension.RelationTargets.Length; index++)
        {
            BaseModuleRelationTargetCaptureRequest expected = extension.RelationTargets[index];
            BaseCapturedModuleRelationTarget actual = value.ModuleRelationTargets[index];
            if (expected.Ordinal != index || actual.Ordinal != index
                || actual.SourceStatementId != expected.SourceStatementId
                || actual.SourceFieldId != expected.SourceFieldId
                || actual.TargetCollectionId != expected.TargetCollection.Id
                || actual.TargetRecordId != expected.TargetRecordId
                || actual.Current is not null && (actual.Current.CollectionId != expected.TargetCollection.Id || actual.Current.Id != expected.TargetRecordId)
                || !ExactInterval(value.ReadIntervals, intervalOrdinal++, $"collection:{expected.TargetCollection.Id}:record", Encoding.UTF8.GetBytes(expected.TargetRecordId.Value)))
                return false;
            if (actual.Current is not null)
                relationBytes = checked(relationBytes + JsonSerializer.SerializeToUtf8Bytes(actual.Current, HPDBaseJsonSerializerContext.Default.RecordEnvelope).LongLength);
        }
        for (int index = 0; index < extension.Generations.Length; index++)
        {
            BaseModuleGenerationCaptureRequest expected = extension.Generations[index];
            BaseCapturedModuleGeneration actual = value.Generations[index];
            if (expected.Ordinal != index || actual.Ordinal != index
                || actual.CaptureId != expected.CaptureId
                || actual.CellId != expected.Cell.Id || actual.CellVersion != expected.Cell.Version
                || actual.CanonicalKeyDigest is not { Length: 64 }
                || actual.Exists != (actual.Generation is not null)
                || expected.Absence == BaseModuleGenerationAbsenceBehavior.RequireExisting && !actual.Exists
                || expected.Absence == BaseModuleGenerationAbsenceBehavior.RequireMissing && actual.Exists
                || intervalOrdinal >= value.ReadIntervals.Length)
                return false;
            BaseAtomicReadIntervalEvidence interval = value.ReadIntervals[intervalOrdinal++];
            if (interval.LogicalAccessPathId != "module-generation" || !interval.LowerInclusive || !interval.UpperInclusive
                || !interval.CanonicalLowerBound.AsSpan().SequenceEqual(interval.CanonicalUpperBound.AsSpan())
                || !string.Equals(actual.CanonicalKeyDigest,
                    Convert.ToHexStringLower(SHA256.HashData(interval.CanonicalLowerBound.AsSpan())), StringComparison.Ordinal))
                return false;
            generationBytes = checked(generationBytes + interval.CanonicalLowerBound.Length + 1 + (actual.Exists ? 8 : 0));
        }
        if (intervalOrdinal != value.ReadIntervals.Length) return false;
        long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(value.ReadIntervals);
        long transient = checked(selectedBytes + relationBytes + generationBytes + evidenceBytes);
        return value.Accounting.Records == extension.Records.Length
            && value.Accounting.RelationTargetReads == extension.RelationTargets.Length
            && value.Accounting.GenerationReads == extension.Generations.Length
            && value.Accounting.ReadIntervals == value.ReadIntervals.Length
            && value.Accounting.SelectedBytes == selectedBytes
            && value.Accounting.RelationTargetBytes == relationBytes
            && value.Accounting.GenerationBytes == generationBytes
            && value.Accounting.EvidenceBytes == evidenceBytes
            && value.Accounting.TransientBytes == transient
            && selectedBytes <= limits.MaximumSelectedBytes
            && generationBytes <= limits.MaximumGenerationBytes
            && evidenceBytes <= limits.MaximumEvidenceBytes
            && transient <= limits.MaximumTransientBytes
            && value.ReadIntervals.Length <= limits.MaximumReadIntervals;

        static bool ExactInterval(
            ImmutableArray<BaseAtomicReadIntervalEvidence> intervals,
            int ordinal,
            string path,
            byte[] key) => ordinal < intervals.Length
            && intervals[ordinal].LogicalAccessPathId == path
            && intervals[ordinal].LowerInclusive && intervals[ordinal].UpperInclusive
            && intervals[ordinal].CanonicalLowerBound.AsSpan().SequenceEqual(key)
            && intervals[ordinal].CanonicalUpperBound.AsSpan().SequenceEqual(key);
    }

    private static bool PreparedMatches(BaseFinalizedAtomicExecutionPlan plan, BaseCapturedAtomicExecution captured, BasePreparedAtomicExecution prepared)
    {
        BaseFinalizedModuleMutationExtension? module = plan.Module;
        if (module is null || prepared.Kind != BaseAtomicMutationExecutionKind.ModuleMutation
            || !string.Equals(prepared.PlanDigest, plan.PlanDigest, StringComparison.Ordinal)
            || !AuthorityMatches(prepared.Authority, captured.Authority)
            || prepared.Dispositions.Length != plan.Items.Length
            || prepared.Generations.Length != captured.Generations.Length
            || prepared.Accounting.GenerationReads != captured.Generations.Length
            || prepared.Accounting.GenerationComparisons != module.Comparisons.Length
            || prepared.Accounting.GenerationIncrements != module.Increments.Length
            || prepared.Accounting.ReadIntervals != prepared.ReadIntervals.Length
            || !DefaultBaseMutationProcessor.RetirementEvidenceMatches(plan.SubjectRetirement, prepared.SubjectRetirement)
            || !BaseTextAtomicMutationContract.PreparedMatches(plan.Text, prepared.Text))
            return false;
        HashSet<int> incremented = module.Increments.Select(static value => value.CaptureOrdinal).ToHashSet();
        for (int index = 0; index < captured.Generations.Length; index++)
        {
            BaseCapturedModuleGeneration prior = captured.Generations[index];
            BasePreparedModuleGenerationEvidence actual = prepared.Generations[index];
            if (actual.CaptureOrdinal != index || actual.CanonicalKeyDigest != prior.CanonicalKeyDigest
                || !Equals(actual.Previous, prior.Generation)) return false;
            if (!incremented.Contains(index))
            {
                if (prior.Exists && (actual.Disposition != BaseModuleGenerationPreparationDisposition.Preserved
                    || !Equals(actual.Resulting, prior.Generation))) return false;
                if (!prior.Exists && (actual.Disposition != BaseModuleGenerationPreparationDisposition.RemainedAbsent
                    || actual.Resulting is not null)) return false;
                continue;
            }
            if (prior.Exists)
            {
                BaseModuleGeneration expected;
                try { expected = prior.Generation!.Increment(); } catch { return false; }
                if (actual.Disposition != BaseModuleGenerationPreparationDisposition.Incremented
                    || !Equals(actual.Resulting, expected)) return false;
            }
            else if (actual.Disposition != BaseModuleGenerationPreparationDisposition.Created
                || actual.Resulting?.ToCanonicalString() != "1") return false;
        }
        return true;
    }

    private static bool AuthorityMatches(BaseAtomicMutationAuthorityEvidence left, BaseAtomicMutationAuthorityEvidence right) =>
        left.ApplicationId == right.ApplicationId
        && left.StoreInstanceId == right.StoreInstanceId
        && left.RestoreEpoch == right.RestoreEpoch
        && left.SchemaGeneration == right.SchemaGeneration
        && left.Isolation == right.Isolation
        && left.Collections.Length == right.Collections.Length
        && left.Collections.Zip(right.Collections).All(static pair =>
            pair.First.CollectionId == pair.Second.CollectionId
            && pair.First.CollectionGeneration == pair.Second.CollectionGeneration)
        && left.TransactionEvidenceToken.AsSpan().SequenceEqual(right.TransactionEvidenceToken.AsSpan());

    private static bool AppliedMatches(
        BaseFinalizedAtomicExecutionPlan plan,
        BaseCapturedAtomicExecution captured,
        BasePreparedAtomicExecution prepared,
        BaseProvisionalAtomicExecution applied)
    {
        BaseFinalizedModuleMutationExtension? module = plan.Module;
        if (module is null || applied.Kind != BaseAtomicMutationExecutionKind.ModuleMutation
            || !string.Equals(applied.PlanDigest, plan.PlanDigest, StringComparison.Ordinal)
            || applied.Facts.Length != plan.Items.Length
            || applied.Generations.Length != module.Increments.Length
            || !DefaultBaseMutationProcessor.RetirementEvidenceMatches(plan.SubjectRetirement, applied.SubjectRetirement)
            || !BaseTextAtomicMutationContract.AppliedMatches(plan.Text, prepared.Text, applied.Text, applied.Facts))
            return false;
        for (int index = 0; index < applied.Generations.Length; index++)
        {
            BaseModuleGenerationIncrement increment = module.Increments[index];
            BaseModuleCommittedGeneration actual = applied.Generations[index];
            if (increment.CaptureOrdinal < 0 || increment.CaptureOrdinal >= captured.Generations.Length)
                return false;
            BaseCapturedModuleGeneration prior = captured.Generations[increment.CaptureOrdinal];
            if (actual.CaptureId != prior.CaptureId || actual.CellId != prior.CellId
                || actual.CellVersion != prior.CellVersion || actual.Resulting is null
                || !Equals(actual.Previous, prior.Generation)
                || actual.Previous is not null && (actual.Previous.Value == long.MaxValue || actual.Resulting.Value != actual.Previous.Value + 1)
                || actual.Previous is null && actual.Resulting.ToCanonicalString() != "1")
                return false;
        }
        return true;
    }

    private static AtomicMutationProcessingResult Failed(BaseError error) => new(
        AtomicMutationProcessingOutcome.Failed, [], error);
    private static BaseError Error(string code, ErrorCategory category) => new()
    {
        Code = code, Message = "The registered module mutation could not be completed.", Category = category,
    };
    private static string Digest(params string[] values) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', values)))).ToLowerInvariant();
}
