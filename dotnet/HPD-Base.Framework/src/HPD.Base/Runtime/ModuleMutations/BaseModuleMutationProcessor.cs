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
    BaseActivationCreationExtension? activationCreation,
    BaseAtomicSemanticActivationExtension? semanticActivation,
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
    internal BaseSemanticActivationReceiptEvidence? SemanticReceipt { get; private set; }
    internal ImmutableArray<byte> OuterReceiptChecksum { get; private set; }

    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession provider,
        CancellationToken cancellationToken = default)
    {
        var captureRequest = new BaseAtomicExecutionRequest
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
            Intent = intent,
            Module = extension,
            Activations = activationCreation,
            SemanticActivation = semanticActivation,
            ActivationGuard = activationGuard,
            SubjectRetirement = CreateRetirementCapture(extension),
            Limits = limits,
        };
        OperationResult<BaseCapturedAtomicExecution> captured = await provider
            .CaptureAtomicExecutionAsync(captureRequest, cancellationToken).ConfigureAwait(false);
        if (!captured.IsSuccess() || captured.Value is null)
            return Failed(captured.Error ?? Error(BaseModuleMutationErrorCodes.StoreError, ErrorCategory.Store));
        BaseCapturedAtomicExecution evidence = captured.Value;
        if (!CapturedMatches(intent, extension, activationCreation, limits, evidence)
            || !ActivationCapturedMatches(activationCreation, evidence.Activations)
            || (semanticActivation is null) != (evidence.SemanticActivation is null))
            return Failed(Error("base.moduleMutation.captureEvidenceInvalid", ErrorCategory.Store));
        BaseAtomicSemanticActivationExtension? finalizedSemantic;
        try { finalizedSemantic = FinalizeSemantic(semanticActivation, evidence.SemanticActivation); }
        catch { return Failed(Error("base.semanticActivation.captureEvidenceInvalid", ErrorCategory.Store)); }

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
            textPlan is null ? string.Empty : Convert.ToHexString(textPlan.ProjectionDigest.AsSpan()),
            activationCreation is null ? string.Empty : Convert.ToHexString(activationCreation.StructuralDigest.AsSpan()),
            finalizedSemantic is null ? string.Empty : Convert.ToHexString(finalizedSemantic.StructuralDigest.AsSpan()));
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
            Activations = activationCreation,
            SemanticActivation = finalizedSemantic,
            ActivationGuard = activationGuard,
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
        BaseSemanticActivationReceiptEvidence? semanticReceipt = CreateSemanticReceipt(finalizedSemantic, evidence.SemanticActivation, applied.Value.SemanticActivation);
        SemanticReceipt = semanticReceipt;
        try { typed = evaluator.ProjectResult(definition.Template.Result, committedStatements, committedGenerations, semanticReceipt, out resultBytes); }
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
            CreatedActivationIds = finalizedSemantic is null
                ? (applied.Value.Activations?.Items
                    .Select(static value => new string(value.ActivationId.AsSpan())).ToImmutableArray() ?? [])
                : [],
            SemanticActivation = semanticReceipt,
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
        byte[] outerReceiptBytes = JsonSerializer.SerializeToUtf8Bytes(
            BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        OuterReceiptChecksum = SHA256.HashData(outerReceiptBytes).ToImmutableArray();
        long receiptBytes = outerReceiptBytes.LongLength;
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
            SemanticReceipt = module.SemanticActivation;
            OuterReceiptChecksum = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                BaseAtomicReceiptWire.From(committedResult), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)).ToImmutableArray();
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
        BaseActivationCreationExtension? activationCreation,
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
        if (value.Activations is { } activationEvidence)
        {
            if (intervalOrdinal + activationEvidence.ReadIntervals.Length > value.ReadIntervals.Length)
                return false;
            for (int index = 0; index < activationEvidence.ReadIntervals.Length; index++)
            {
                if (!Equals(value.ReadIntervals[intervalOrdinal + index], activationEvidence.ReadIntervals[index]))
                    return false;
            }
            intervalOrdinal += activationEvidence.ReadIntervals.Length;
        }
        if (activationCreation is not null)
            selectedBytes = checked(selectedBytes + activationCreation.Items.Sum(static item => item.CanonicalInput.Length + 32L));
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

    private static bool ActivationCapturedMatches(
        BaseActivationCreationExtension? expected,
        BaseCapturedActivationExtension? actual)
    {
        if (expected is null) return actual is null;
        if (actual is null || expected.StructuralDigest.Length != 32
            || actual.Checksum.Length != 32 || actual.Items.Length != expected.Items.Length
            || actual.ReadIntervals.Length != expected.Items.Length)
            return false;
        for (int ordinal = 0; ordinal < expected.Items.Length; ordinal++)
        {
            BaseCapturedActivationItem item = actual.Items[ordinal];
            BaseAtomicReadIntervalEvidence interval = actual.ReadIntervals[ordinal];
            if (item.Ordinal != ordinal || string.IsNullOrWhiteSpace(item.ActivationId)
                || interval.LogicalAccessPathId != "base.activation.byId"
                || !interval.LowerInclusive || !interval.UpperInclusive
                || !interval.CanonicalLowerBound.AsSpan().SequenceEqual(interval.CanonicalUpperBound.AsSpan())
                || !interval.CanonicalLowerBound.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(item.ActivationId))
                || item.Exists && item.ExistingFingerprint.Length != 32
                || !item.Exists && !item.ExistingFingerprint.IsDefaultOrEmpty)
                return false;
        }
        return true;
    }

    private static BaseAtomicSemanticActivationExtension? FinalizeSemantic(
        BaseAtomicSemanticActivationExtension? requested,
        BaseCapturedSemanticActivationEvidence? captured)
    {
        if (requested is null) return captured is null ? null : throw new InvalidOperationException();
        if (captured is null || !CapturedSemanticMatches(requested, captured))
            throw new InvalidOperationException();
        BaseSemanticActivationScopeBinding binding = captured.ScopeDirectory.ResultingBinding;
        BaseSemanticActivationOperation operation = requested.Operation;
        BaseSemanticActivationDefinitionIdentity definition;
        ImmutableArray<byte> canonicalKey;
        if (operation is BaseSemanticActivationEnsureIntent ensure) { definition = ensure.Definition; canonicalKey = ensure.CanonicalKey; }
        else if (operation is BaseSemanticActivationRetireIntent retire) { definition = retire.Definition; canonicalKey = retire.CanonicalKey; }
        else throw new InvalidOperationException();
        BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(SemanticHash(
            "base.semanticActivation.key.v1\0", Encoding.UTF8.GetBytes(definition.Id), binding.BindingId.ToArray(), canonicalKey.ToArray()));
        BaseSemanticActivationOperation finalized = operation switch
        {
            BaseSemanticActivationEnsureIntent ensureIntent => FinalizeEnsure(ensureIntent, definition, key,
                binding.BindingId.ToArray(), requested.Capture.StoreAuthority, captured.AcceptedTime),
            BaseSemanticActivationRetireIntent retire => retire with
            {
                Key = key,
                SubjectLifetime = FinalizeLifetime(retire.SubjectLifetime, binding.BindingId),
            },
            _ => throw new InvalidOperationException(),
        };
        Span<byte> digestBytes = stackalloc byte[32]; key.CopyTo(digestBytes);
        byte[] structural = requested.Capture.RecoveryPending is { } pending
            ? SemanticHash("base.semanticActivation.extension.v1\0", definition.Checksum.ToArray(), canonicalKey.ToArray(), binding.BindingId.ToArray(),
                [(byte)(operation is BaseSemanticActivationEnsureIntent ? 1 : 2)], pending.Checksum.ToArray())
            : SemanticHash("base.semanticActivation.extension.v1\0", definition.Checksum.ToArray(), canonicalKey.ToArray(), binding.BindingId.ToArray(),
                [(byte)(operation is BaseSemanticActivationEnsureIntent ? 1 : 2)]);
        return new BaseAtomicSemanticActivationExtension
        {
            Capture = requested.Capture with
            {
                Definition = requested.Capture.Definition with { Checksum = requested.Capture.Definition.Checksum.ToArray().ToImmutableArray() },
                CanonicalKey = requested.Capture.CanonicalKey.ToArray().ToImmutableArray(),
                KeyPreimageChecksum = requested.Capture.KeyPreimageChecksum.ToArray().ToImmutableArray(),
                Scope = requested.Capture.Scope with { Value = requested.Capture.Scope.Value is null ? null : new string(requested.Capture.Scope.Value.AsSpan()) },
                ProposedScopeBindingId = requested.Capture.ProposedScopeBindingId.ToArray().ToImmutableArray(),
                StoreAuthority = requested.Capture.StoreAuthority with { DefinitionSetChecksum = requested.Capture.StoreAuthority.DefinitionSetChecksum.ToArray().ToImmutableArray() },
                Limits = requested.Capture.Limits with { },
                AcceptedTime = requested.Capture.AcceptedTime,
            },
            Operation = finalized,
            StructuralDigest = structural.ToImmutableArray(),
        };
    }

    private static bool CapturedSemanticMatches(
        BaseAtomicSemanticActivationExtension requested,
        BaseCapturedSemanticActivationEvidence captured)
    {
        BaseSemanticActivationCaptureRequest capture = requested.Capture;
        BaseSemanticActivationScopeBinding binding = captured.ScopeDirectory.ResultingBinding;
        if (binding.BindingId.Length != 32 || binding.Checksum.Length != 32 || binding.SeekDigest.Length != 32
            || binding.ProtectionKeyVersion < 1 || string.IsNullOrWhiteSpace(binding.ProtectionKeyId)
            || captured.ScopeDirectory.Checksum.Length != 32 || captured.Checksum.Length != 32
            || !BaseActivationAcceptedTimeAuthority.Verify(captured.AcceptedTime,
                captured.AcceptedTime.CapturedUtc)
            || !CryptographicOperations.FixedTimeEquals(
                captured.AcceptedTime.Checksum.Span, capture.AcceptedTime.Checksum.Span)
            || captured.ReadIntervals.Length != 2 || captured.Accounting.ReadIntervals != 2
            || !SemanticAccountingWithin(captured.Accounting, capture.Limits)
            || captured.ScopeDirectory.State == BaseSemanticActivationScopeDirectoryState.Missing
                && !binding.BindingId.AsSpan().SequenceEqual(capture.ProposedScopeBindingId.AsSpan())
            || binding.Kind != capture.Scope.Kind
            || !CryptographicOperations.FixedTimeEquals(
                BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(binding).AsSpan(), binding.Checksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(
                BaseSemanticActivationEvidenceContract.ScopeDirectoryChecksum(binding).AsSpan(), captured.ScopeDirectory.Checksum.AsSpan()))
            return false;
        BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(SemanticHash(
            "base.semanticActivation.key.v1\0", Encoding.UTF8.GetBytes(capture.Definition.Id),
            binding.BindingId.ToArray(), capture.CanonicalKey.ToArray()));
        byte[] scopeBound = Encoding.UTF8.GetBytes($"{(int)capture.Scope.Kind}\n{Convert.ToHexString(binding.SeekDigest.AsSpan())}");
        byte[] slotBound = Encoding.UTF8.GetBytes($"{capture.Definition.Id}\n{Convert.ToHexString(binding.BindingId.AsSpan())}\n{Convert.ToHexString(key.ToArray())}");
        if (!ExactInterval(captured.ReadIntervals[0], "base.semanticActivation.scope", scopeBound)
            || !ExactInterval(captured.ReadIntervals[1], "base.semanticActivation.slot", slotBound)
            || captured.ScopeDirectory.ReadIntervals.Length != 1
            || !IntervalEqual(captured.ScopeDirectory.ReadIntervals[0], captured.ReadIntervals[0]))
            return false;
        Span<byte> keyBytes = stackalloc byte[32]; key.CopyTo(keyBytes);
        int payloads = (captured.Missing is null ? 0 : 1) + (captured.Live is null ? 0 : 1)
            + (captured.Retired is null ? 0 : 1) + (captured.Absent is null ? 0 : 1);
        if (payloads != 1 || captured.State switch
            {
                BaseSemanticActivationCapturedState.Missing => !MissingMatches(captured.Missing, key, capture.StoreAuthority, captured.ReadIntervals[1]),
                BaseSemanticActivationCapturedState.Live => !LiveMatches(captured.Live, key, requested, binding, captured),
                BaseSemanticActivationCapturedState.Retired => !RetiredMatches(captured.Retired, key, requested, binding),
                BaseSemanticActivationCapturedState.CompactedAbsent => !AbsentMatches(captured.Absent, key, requested, binding),
                _ => true,
            }) return false;
        if (capture.RecoveryPreflight is { } preflight
            && !BaseSemanticActivationEvidenceContract.RecoveryPreflightMatchesCapture(preflight, captured))
            return false;
        if ((capture.RecoveryPreflight is null) != (capture.RecoveryPending is null)
            || capture.RecoveryPending is { } pending && !RecoveryPendingMatches(pending, capture, binding, key))
            return false;
        if (captured.State == BaseSemanticActivationCapturedState.Live)
        {
            if (captured.ActivationGeneration is not > 0 || captured.ActivationChecksum.Length != 32
                || captured.ActivationState is null || !Enum.IsDefined(captured.ActivationState.Value)) return false;
            bool terminal = captured.ActivationState.Value is BaseActivationState.Succeeded or BaseActivationState.Exhausted
                or BaseActivationState.Cancelled or BaseActivationState.Migrated or BaseActivationState.Disposed;
            if (terminal ? captured.ActivationTerminalReceiptChecksum.Length != 32
                : !captured.ActivationTerminalReceiptChecksum.IsDefaultOrEmpty) return false;
        }
        else if (captured.ActivationGeneration is not null || captured.ActivationState is not null
            || !captured.ActivationChecksum.IsDefaultOrEmpty || !captured.ActivationTerminalReceiptChecksum.IsDefaultOrEmpty) return false;
        return CryptographicOperations.FixedTimeEquals(
            BaseSemanticActivationEvidenceContract.CapturedChecksum(requested, captured).AsSpan(), captured.Checksum.AsSpan());
    }

    private static bool MissingMatches(BaseSemanticActivationMissingAuthority? value, BaseSemanticActivationKeyDigest key,
        BaseSemanticActivationStoreAuthorityRequirement store, BaseAtomicReadIntervalEvidence slot)
    {
        return value is not null && KeyEqual(value.Key, key) && StoreMatches(value.StoreAuthority, store)
            && value.AccessPathChecksum.Length == 32
            && CryptographicOperations.FixedTimeEquals(value.AccessPathChecksum.AsSpan(),
                BaseSemanticActivationEvidenceContract.MissingAccessPathChecksum(slot.CanonicalLowerBound.AsSpan()).AsSpan());
    }

    private static bool LiveMatches(BaseSemanticActivationLiveAuthority? value, BaseSemanticActivationKeyDigest key,
        BaseAtomicSemanticActivationExtension requested, BaseSemanticActivationScopeBinding binding,
        BaseCapturedSemanticActivationEvidence captured)
    {
        BaseSemanticActivationCaptureRequest capture = requested.Capture;
        if (value is null || !KeyEqual(value.KeyDigest, key) || value.SlotGeneration < 1
            || value.Definition.Id != capture.Definition.Id || value.Definition.Version != capture.Definition.Version
            || value.Definition.OwnerGeneration != capture.Definition.OwnerGeneration
            || value.Definition.OwningModuleId != capture.Definition.OwningModuleId
            || !value.Definition.Checksum.AsSpan().SequenceEqual(capture.Definition.Checksum.AsSpan())
            || value.Scope.Kind != capture.Scope.Kind || value.Scope.Value != capture.Scope.Value
            || !ScopeBindingEqual(value.ScopeBinding, binding)
            || !StoreMatches(value.StoreAuthority, capture.StoreAuthority)
            || string.IsNullOrWhiteSpace(value.ActivationId) || value.ActivationDefinition.Checksum.Length != 32
            || value.InputChecksum.Length != 32 || !Enum.IsDefined(value.Due.Mode)
            || value.Due.CanonicalUnixMilliseconds < 0 || captured.ActivationGeneration is not > 0
            || captured.ActivationChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), BaseSemanticActivationEvidenceContract.LiveChecksum(value).AsSpan()))
            return false;
        byte[] expectedControl = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.control.v2\0{value.ActivationId}\n{captured.ActivationGeneration.Value}\n{(int)captured.ActivationState!.Value}"));
        if (!CryptographicOperations.FixedTimeEquals(expectedControl, captured.ActivationChecksum.AsSpan())) return false;
        byte[] expectedId = SemanticHash("base.semanticActivation.activation.v1\0",
            Encoding.UTF8.GetBytes(capture.StoreAuthority.ApplicationId), Encoding.UTF8.GetBytes(capture.StoreAuthority.LogicalStoreId),
            Encoding.UTF8.GetBytes(capture.Definition.OwningModuleId), Encoding.UTF8.GetBytes(capture.Definition.Id),
            binding.BindingId.ToArray(), capture.CanonicalKey.ToArray());
        if (!string.Equals(value.ActivationId, Convert.ToHexStringLower(expectedId), StringComparison.Ordinal)) return false;
        BaseSemanticActivationSubjectLifetimeBinding? requestedLifetime = requested.Operation switch
        {
            BaseSemanticActivationEnsureIntent ensureValue => FinalizeLifetime(ensureValue.SubjectLifetime, binding.BindingId),
            BaseSemanticActivationRetireIntent retireValue => FinalizeLifetime(retireValue.SubjectLifetime, binding.BindingId),
            _ => null,
        };
        if (!LifetimeEqual(value.SubjectLifetime, requestedLifetime)) return false;
        if (requested.Operation is BaseSemanticActivationEnsureIntent ensure)
            return value.ActivationDefinition.Id == ensure.Activation.Definition.Id
                && value.ActivationDefinition.Version == ensure.Activation.Definition.Version
                && value.ActivationDefinition.Checksum.AsSpan().SequenceEqual(ensure.Activation.Definition.Checksum.AsSpan())
                && value.InputChecksum.AsSpan().SequenceEqual(ensure.Activation.InputChecksum.AsSpan())
                && value.Due.Mode == ensure.Due.Mode
                && (ensure.Due.Mode == BaseSemanticActivationDueMode.AcceptedCurrentTime
                    || value.Due.CanonicalUnixMilliseconds == ensure.Due.CanonicalUnixMilliseconds);
        return true;
    }

    private static bool RetiredMatches(BaseSemanticActivationRetirementAuthority? value, BaseSemanticActivationKeyDigest key,
        BaseSemanticActivationCaptureRequest capture) => value is not null && KeyEqual(value.KeyDigest, key)
        && value.Definition.Id == capture.Definition.Id && value.Definition.Version == capture.Definition.Version
        && value.Definition.Checksum.AsSpan().SequenceEqual(capture.Definition.Checksum.AsSpan())
        && value.SlotGeneration > 0 && value.TerminalActivationGeneration > 0 && value.RetirementPosition > 0
        && value.TerminalActivationChecksum.Length == 32 && value.CompletionOperationChecksum.Length == 32
        && value.CompletionReceiptChecksum.Length == 32 && StoreMatches(value.StoreAuthority, capture.StoreAuthority)
        && CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), BaseSemanticActivationEvidenceContract.RetirementChecksum(value).AsSpan());

    private static bool RetiredMatches(BaseSemanticActivationRetirementAuthority? value, BaseSemanticActivationKeyDigest key,
        BaseAtomicSemanticActivationExtension requested, BaseSemanticActivationScopeBinding binding)
    {
        if (!RetiredMatches(value, key, requested.Capture) || value is null) return false;
        if (!SemanticTerminalStateAllowed(value.TerminalState))
            return false;
        byte[] expectedTerminalChecksum = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.control.v2\0{value.ActivationId}\n{value.TerminalActivationGeneration}\n{(int)value.TerminalState}"));
        byte[] installedCompletionChecksum;
        try { installedCompletionChecksum = Convert.FromHexString(requested.Capture.Definition.RetirementOperation.OperationChecksum); }
        catch { return false; }
        if (installedCompletionChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(expectedTerminalChecksum, value.TerminalActivationChecksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(installedCompletionChecksum, value.CompletionOperationChecksum.AsSpan()))
            return false;
        BaseSemanticActivationSubjectLifetimeBinding? requestedLifetime = requested.Operation switch
        {
            BaseSemanticActivationEnsureIntent ensure => FinalizeLifetime(ensure.SubjectLifetime, binding.BindingId),
            BaseSemanticActivationRetireIntent retire => FinalizeLifetime(retire.SubjectLifetime, binding.BindingId),
            _ => null,
        };
        if (!LifetimeEqual(value.SubjectLifetime, requestedLifetime)) return false;
        return requested.Operation is not BaseSemanticActivationRetireIntent retirement
            || retirement.CompletionOperation == requested.Capture.Definition.RetirementOperation;
    }

    private static bool ExactInterval(BaseAtomicReadIntervalEvidence value, string path, ReadOnlySpan<byte> bound) =>
        string.Equals(value.LogicalAccessPathId, path, StringComparison.Ordinal)
        && value.LowerInclusive && value.UpperInclusive
        && value.CanonicalLowerBound.AsSpan().SequenceEqual(bound)
        && value.CanonicalUpperBound.AsSpan().SequenceEqual(bound);

    private static bool IntervalEqual(BaseAtomicReadIntervalEvidence left, BaseAtomicReadIntervalEvidence right) =>
        string.Equals(left.LogicalAccessPathId, right.LogicalAccessPathId, StringComparison.Ordinal)
        && left.LowerInclusive == right.LowerInclusive && left.UpperInclusive == right.UpperInclusive
        && left.CanonicalLowerBound.AsSpan().SequenceEqual(right.CanonicalLowerBound.AsSpan())
        && left.CanonicalUpperBound.AsSpan().SequenceEqual(right.CanonicalUpperBound.AsSpan());

    internal static bool SemanticTerminalStateAllowed(BaseActivationState value) =>
        value is BaseActivationState.Succeeded or BaseActivationState.Exhausted
            or BaseActivationState.Cancelled or BaseActivationState.Migrated or BaseActivationState.Disposed;

    private static bool ScopeBindingEqual(BaseSemanticActivationScopeBinding left, BaseSemanticActivationScopeBinding right) =>
        left.Kind == right.Kind && left.ProtectionKeyVersion == right.ProtectionKeyVersion
        && string.Equals(left.ProtectionKeyId, right.ProtectionKeyId, StringComparison.Ordinal)
        && left.BindingId.AsSpan().SequenceEqual(right.BindingId.AsSpan())
        && left.ProtectedCanonicalScope.AsSpan().SequenceEqual(right.ProtectedCanonicalScope.AsSpan())
        && left.SeekDigest.AsSpan().SequenceEqual(right.SeekDigest.AsSpan())
        && left.Checksum.AsSpan().SequenceEqual(right.Checksum.AsSpan());

    private static bool LifetimeEqual(BaseSemanticActivationSubjectLifetimeBinding? left, BaseSemanticActivationSubjectLifetimeBinding? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return string.Equals(left.ContractId, right.ContractId, StringComparison.Ordinal)
            && left.ContractVersion == right.ContractVersion
            && left.ContractChecksum.AsSpan().SequenceEqual(right.ContractChecksum.AsSpan())
            && left.SubjectId.Equals(right.SubjectId)
            && left.AuthorityEpoch.Equals(right.AuthorityEpoch)
            && left.Incarnation.Equals(right.Incarnation)
            && left.ScopeBindingId.AsSpan().SequenceEqual(right.ScopeBindingId.AsSpan())
            && left.Checksum.AsSpan().SequenceEqual(right.Checksum.AsSpan());
    }

    private static bool AbsentMatches(BaseSemanticActivationAbsenceAuthority? value, BaseSemanticActivationKeyDigest key,
        BaseAtomicSemanticActivationExtension requested, BaseSemanticActivationScopeBinding binding)
    {
        BaseSemanticActivationCaptureRequest capture = requested.Capture;
        BaseSemanticActivationSubjectLifetimeBinding? requestedLifetime = requested.Operation switch
        {
            BaseSemanticActivationEnsureIntent ensure => FinalizeLifetime(ensure.SubjectLifetime, binding.BindingId),
            BaseSemanticActivationRetireIntent retire => FinalizeLifetime(retire.SubjectLifetime, binding.BindingId),
            _ => null,
        };
        return value is not null && KeyEqual(value.Key, key)
        && value.Definition.Id == capture.Definition.Id && value.Definition.Version == capture.Definition.Version
        && value.Definition.OwnerGeneration == capture.Definition.OwnerGeneration
        && value.Definition.OwningModuleId == capture.Definition.OwningModuleId
        && value.Definition.Checksum.AsSpan().SequenceEqual(capture.Definition.Checksum.AsSpan())
        && value.ScopeBindingId.AsSpan().SequenceEqual(binding.BindingId.AsSpan())
        && LifetimeEqual(value.SubjectLifetime, requestedLifetime)
        && value.FinalSlotGeneration > 0 && value.AbsenceFloorGeneration > 0
        && value.RetirementPosition > 0 && StoreMatches(value.StoreAuthority, capture.StoreAuthority)
        && CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), BaseSemanticActivationEvidenceContract.AbsenceChecksum(value).AsSpan());
    }

    private static bool KeyEqual(BaseSemanticActivationKeyDigest left, BaseSemanticActivationKeyDigest right) =>
        CryptographicOperations.FixedTimeEquals(left.ToArray(), right.ToArray());

    private static bool RecoveryPendingMatches(BaseSemanticRecoveryPendingCommitAuthority value,
        BaseSemanticActivationCaptureRequest capture, BaseSemanticActivationScopeBinding binding,
        BaseSemanticActivationKeyDigest key)
    {
        if (capture.Operation != BaseSemanticActivationOperationKind.Retire || value.AuthorityVersion <= 0
            || string.IsNullOrWhiteSpace(value.AuthorityId) || value.AuthorityChecksum.Length != 32
            || value.Intent.Boundary.DefinitionId != capture.Definition.Id
            || !value.Intent.Boundary.ScopeBindingId.AsSpan().SequenceEqual(binding.BindingId.AsSpan())
            || !KeyEqual(value.Intent.Boundary.Key, key) || value.Intent.RetirementOperationFingerprint.Length != 32
            || value.Intent.Checksum.Length != 32 || value.Pending.IntentChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticRecoveryAuthorityContract.PendingIntentChecksum(value.Intent).AsSpan(), value.Intent.Checksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(value.Intent.Checksum.AsSpan(), value.Pending.IntentChecksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticRecoveryAuthorityContract.PendingChecksum(value.Pending).AsSpan(), value.Pending.Checksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticRecoveryAuthorityContract.PendingCommitChecksum(value).AsSpan(), value.Checksum.AsSpan()))
            return false;
        return LifetimeEqual(value.Intent.SubjectLifetime, capture.RecoveryPreflight?.Live.SubjectLifetime);
    }

    private static bool StoreMatches(BaseSemanticActivationStoreAuthority actual, BaseSemanticActivationStoreAuthorityRequirement expected)
    {
        BaseSemanticActivationStoreAuthorityRequirement value = actual.Requirement;
        if (actual.Checksum.Length != 32 || value.ApplicationId != expected.ApplicationId
            || value.LogicalStoreId != expected.LogicalStoreId || value.StoreInstanceId != expected.StoreInstanceId
            || value.RestoreEpoch != expected.RestoreEpoch || value.SchemaGeneration != expected.SchemaGeneration
            || value.SemanticAuthorityGeneration != expected.SemanticAuthorityGeneration
            || !value.DefinitionSetChecksum.AsSpan().SequenceEqual(expected.DefinitionSetChecksum.AsSpan())) return false;
        return CryptographicOperations.FixedTimeEquals(
            BaseSemanticActivationEvidenceContract.StoreAuthorityChecksum(value).AsSpan(), actual.Checksum.AsSpan());
    }

    private static BaseSemanticActivationEnsureIntent FinalizeEnsure(
        BaseSemanticActivationEnsureIntent ensure,
        BaseSemanticActivationDefinitionIdentity definition,
        BaseSemanticActivationKeyDigest key,
        byte[] binding,
        BaseSemanticActivationStoreAuthorityRequirement store,
        BaseAcceptedTimeReceipt acceptedTime)
    {
        Span<byte> keyBytes = stackalloc byte[32]; key.CopyTo(keyBytes);
        byte[] activationId = SemanticHash("base.semanticActivation.activation.v1\0",
            Encoding.UTF8.GetBytes(store.ApplicationId), Encoding.UTF8.GetBytes(store.LogicalStoreId),
            Encoding.UTF8.GetBytes(definition.OwningModuleId), Encoding.UTF8.GetBytes(definition.Id), binding,
            ensure.CanonicalKey.ToArray());
        BaseSemanticActivationDueAuthority due = ensure.Due.Mode == BaseSemanticActivationDueMode.AcceptedCurrentTime
            ? ensure.Due with { CanonicalUnixMilliseconds = acceptedTime.CapturedUtc }
            : ensure.Due;
        byte[] checksum = SemanticHash("base.semanticActivation.creation.v1\0", definition.Checksum.ToArray(), keyBytes.ToArray(), binding, activationId);
        return ensure with
        {
            Key = key,
            SubjectLifetime = FinalizeLifetime(ensure.SubjectLifetime, binding.ToImmutableArray()),
            Due = due,
            Activation = ensure.Activation with
            {
                Due = due,
                Identity = ensure.Activation.Identity with
                {
                    Key = key, ScopeBindingId = binding.ToImmutableArray(), DerivedActivationIdBytes = activationId.ToImmutableArray(),
                    Checksum = checksum.ToImmutableArray(),
                },
            },
        };
    }

    private static BaseSemanticActivationSubjectLifetimeBinding? FinalizeLifetime(
        BaseSemanticActivationSubjectLifetimeBinding? value,
        ImmutableArray<byte> binding)
    {
        if (value is null) return null;
        var finalized = value with { ScopeBindingId = binding.ToArray().ToImmutableArray(), Checksum = [] };
        byte[] checksum = SemanticHash("base.semanticActivation.subjectLifetime.v1\0",
            Encoding.UTF8.GetBytes(finalized.ContractId), BitConverter.GetBytes(finalized.ContractVersion).Reverse().ToArray(),
            finalized.ContractChecksum.ToArray(), finalized.SubjectId.ToUtf8Bytes(),
            Encoding.UTF8.GetBytes(finalized.AuthorityEpoch.ToBase64Url()),
            Encoding.UTF8.GetBytes(finalized.Incarnation.ToBase64Url()), finalized.ScopeBindingId.ToArray());
        return finalized with { Checksum = checksum.ToImmutableArray() };
    }

    private static byte[] SemanticHash(string purpose, params byte[][] fields)
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
            || !PreparedActivationMatches(plan.Activations, captured.Activations, prepared.Activations)
            || !PreparedSemanticMatches(plan.SemanticActivation, captured.SemanticActivation, prepared.SemanticActivation)
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

    private static bool PreparedSemanticMatches(
        BaseAtomicSemanticActivationExtension? plan,
        BaseCapturedSemanticActivationEvidence? captured,
        BasePreparedSemanticActivation? prepared)
    {
        if (plan is null) return captured is null && prepared is null;
        if (captured is null || prepared is null || prepared.Checksum.Length != 32
            || prepared.ResultingSlotGeneration < 1 || prepared.PriorState != captured.State
            || !SemanticReadIntervalsEqual(prepared.ReadIntervals, captured.ReadIntervals)
            || prepared.WriteIntervals.IsDefaultOrEmpty
            || prepared.WriteIntervals.Any(static interval => interval.Checksum.Length != 32
                || string.IsNullOrWhiteSpace(interval.AccessPathId)
                || !interval.LowerInclusive || !interval.UpperInclusive
                || !CryptographicOperations.FixedTimeEquals(interval.Checksum.AsSpan(), BaseSemanticActivationEvidenceContract.WriteIntervalChecksum(interval).AsSpan()))
            || prepared.Accounting != ExpectedSemanticAccounting(plan, captured)
            || !SemanticAccountingWithin(prepared.Accounting, plan.Capture.Limits))
            return false;
        BaseSemanticActivationOperationKind expected = plan.Operation is BaseSemanticActivationEnsureIntent
            ? BaseSemanticActivationOperationKind.Ensure : BaseSemanticActivationOperationKind.Retire;
        BaseAtomicReadIntervalEvidence? slotRead = captured.ReadIntervals.SingleOrDefault(static value =>
            value.LogicalAccessPathId == "base.semanticActivation.slot");
        BaseSemanticActivationWriteIntervalEvidence? slotWrite = prepared.WriteIntervals.Length == 1 ? prepared.WriteIntervals[0] : null;
        if (prepared.Operation != expected
            || slotRead is null || slotWrite is null
            || slotWrite.AccessPathId != slotRead.LogicalAccessPathId
            || !slotWrite.Lower.AsSpan().SequenceEqual(slotRead.CanonicalLowerBound.AsSpan())
            || !slotWrite.Upper.AsSpan().SequenceEqual(slotRead.CanonicalUpperBound.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(prepared.Checksum.AsSpan(), BaseSemanticActivationEvidenceContract.PreparedChecksum(plan, prepared).AsSpan())) return false;
        return (expected, captured.State) switch
        {
            (BaseSemanticActivationOperationKind.Ensure, BaseSemanticActivationCapturedState.Missing) =>
                prepared.ResultingState == BaseSemanticActivationSlotState.Live
                && prepared.ResultingSlotGeneration == 1 && !string.IsNullOrWhiteSpace(prepared.ResultingActivationId)
                && prepared.Accounting.ActivationCreation.Candidates == 1
                && prepared.Accounting.ActivationCreation.Comparisons == 1
                && prepared.Accounting.ActivationCreation.IndexOperations >= 1,
            (BaseSemanticActivationOperationKind.Ensure, BaseSemanticActivationCapturedState.Live) =>
                prepared.ResultingState == BaseSemanticActivationSlotState.Live
                && prepared.ResultingSlotGeneration == captured.Live!.SlotGeneration
                && prepared.ResultingActivationId == captured.Live.ActivationId,
            (BaseSemanticActivationOperationKind.Ensure, BaseSemanticActivationCapturedState.Retired) =>
                prepared.ResultingState == BaseSemanticActivationSlotState.Retired
                && prepared.ResultingSlotGeneration == captured.Retired!.SlotGeneration
                && prepared.ResultingActivationId is null,
            (BaseSemanticActivationOperationKind.Ensure, BaseSemanticActivationCapturedState.CompactedAbsent) =>
                prepared.ResultingState == BaseSemanticActivationSlotState.CompactedAbsent
                && prepared.ResultingSlotGeneration == captured.Absent!.FinalSlotGeneration
                && prepared.ResultingActivationId is null,
            (BaseSemanticActivationOperationKind.Retire, BaseSemanticActivationCapturedState.Live) =>
                prepared.ResultingState == BaseSemanticActivationSlotState.Retired
                && captured.Live!.SlotGeneration < long.MaxValue
                && prepared.ResultingSlotGeneration == captured.Live.SlotGeneration + 1
                && prepared.ResultingActivationId == captured.Live.ActivationId,
            (BaseSemanticActivationOperationKind.Retire, BaseSemanticActivationCapturedState.Retired) =>
                prepared.ResultingState == BaseSemanticActivationSlotState.Retired
                && prepared.ResultingSlotGeneration == captured.Retired!.SlotGeneration
                && prepared.ResultingActivationId == captured.Retired.ActivationId,
            (BaseSemanticActivationOperationKind.Retire, BaseSemanticActivationCapturedState.CompactedAbsent) =>
                prepared.ResultingState == BaseSemanticActivationSlotState.CompactedAbsent
                && prepared.ResultingSlotGeneration == captured.Absent!.FinalSlotGeneration
                && prepared.ResultingActivationId is null,
            _ => false,
        };
    }

    private static BaseSemanticActivationAccounting ExpectedSemanticAccounting(
        BaseAtomicSemanticActivationExtension plan,
        BaseCapturedSemanticActivationEvidence captured)
    {
        bool created = plan.Operation is BaseSemanticActivationEnsureIntent
            && captured.State == BaseSemanticActivationCapturedState.Missing;
        long activationBytes = 0;
        BaseActivationAccounting activation = new()
        {
            Candidates = 0, Comparisons = 0, IndexOperations = 0, ReadIntervals = 0, EvidenceBytes = 0, TransientBytes = 0,
        };
        if (created)
        {
            BaseSemanticActivationEnsureIntent ensure = (BaseSemanticActivationEnsureIntent)plan.Operation;
            activationBytes = checked(ensure.Activation.CanonicalInput.Length + ensure.Activation.InputChecksum.Length
                + ensure.Activation.Definition.Checksum.Length + 64);
            long evidence = checked(activationBytes + 32 + sizeof(long) * 2);
            activation = new BaseActivationAccounting
            {
                Candidates = 1, Comparisons = 1, IndexOperations = 2, ReadIntervals = 1,
                EvidenceBytes = evidence, TransientBytes = evidence,
            };
        }
        return captured.Accounting with
        {
            IndexOperations = captured.ScopeDirectory.State == BaseSemanticActivationScopeDirectoryState.Missing ? 2 : 1,
            ActivationReads = Math.Max(captured.Accounting.ActivationReads,
                plan.Operation is BaseSemanticActivationRetireIntent ? 1 : 0),
            ActivationBytes = activationBytes,
            EvidenceBytes = checked(captured.Accounting.EvidenceBytes + activation.EvidenceBytes),
            TransientBytes = checked(captured.Accounting.TransientBytes + activation.TransientBytes),
            ActivationCreation = activation,
        };
    }

    private static bool PreparedActivationMatches(
        BaseActivationCreationExtension? plan,
        BaseCapturedActivationExtension? captured,
        BasePreparedActivationExtension? prepared)
    {
        if (plan is null) return captured is null && prepared is null;
        if (captured is null || prepared is null || prepared.Checksum.Length != 32
            || captured.Items.Length != plan.Items.Length || prepared.Items.Length != plan.Items.Length)
            return false;
        for (int ordinal = 0; ordinal < plan.Items.Length; ordinal++)
        {
            BasePreparedActivationItem item = prepared.Items[ordinal];
            if (item.Ordinal != ordinal || item.ActivationId != captured.Items[ordinal].ActivationId
                || item.ResultingGeneration != 1 || item.PayloadChecksum.Length != 32
                || item.ControlChecksum.Length != 32
                || !CryptographicOperations.FixedTimeEquals(
                    item.PayloadChecksum.AsSpan(), SHA256.HashData(plan.Items[ordinal].CanonicalInput.AsSpan())))
                return false;
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
            || !AppliedActivationMatches(plan.Activations, prepared.Activations, applied.Activations)
            || !AppliedSemanticMatches(plan.SemanticActivation, captured.SemanticActivation, prepared.SemanticActivation, applied.SemanticActivation)
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

    private static bool AppliedSemanticMatches(
        BaseAtomicSemanticActivationExtension? plan,
        BaseCapturedSemanticActivationEvidence? captured,
        BasePreparedSemanticActivation? prepared,
        BaseProvisionalSemanticActivation? applied)
    {
        if (plan is null) return captured is null && prepared is null && applied is null;
        if (captured is null || prepared is null || applied is null || applied.Checksum.Length != 32
            || applied.ResultingSlotChecksum.Length != 32
            || applied.Operation != prepared.Operation || applied.PriorState != prepared.PriorState
            || applied.ResultingState != prepared.ResultingState
            || applied.ResultingSlotGeneration != prepared.ResultingSlotGeneration
            || !string.Equals(applied.ActivationId, prepared.ResultingActivationId, StringComparison.Ordinal)
            || applied.Accounting != prepared.Accounting
            || (applied.ActivationId is null
                ? applied.ActivationGeneration is not null || !applied.ActivationChecksum.IsDefaultOrEmpty
                : applied.ActivationGeneration is not > 0 || applied.ActivationChecksum.Length != 32)
            || applied.CommitJournalPosition <= 0) return false;
        if (!CryptographicOperations.FixedTimeEquals(applied.Checksum.AsSpan(), BaseSemanticActivationEvidenceContract.ProvisionalChecksum(prepared, applied).AsSpan())
            || !ResultingSlotChecksumMatches(plan, captured, applied)) return false;
        if (prepared.PriorState == BaseSemanticActivationCapturedState.Missing && prepared.Operation == BaseSemanticActivationOperationKind.Ensure)
        {
            if (plan.Operation is not BaseSemanticActivationEnsureIntent ensure || applied.ActivationGeneration != 1
                || applied.ActivationId != Convert.ToHexStringLower(ensure.Activation.Identity.DerivedActivationIdBytes.AsSpan())) return false;
            byte[] expectedControl = SHA256.HashData(Encoding.UTF8.GetBytes($"base.activation.control.v2\0{applied.ActivationId}\n1\n{(int)BaseActivationState.Pending}"));
            return CryptographicOperations.FixedTimeEquals(expectedControl, applied.ActivationChecksum.AsSpan());
        }
        return true;
    }

    internal static bool ResultingSlotChecksumMatches(
        BaseAtomicSemanticActivationExtension plan,
        BaseCapturedSemanticActivationEvidence captured,
        BaseProvisionalSemanticActivation applied)
    {
        ImmutableArray<byte> expected;
        if (applied.PriorState == BaseSemanticActivationCapturedState.Live
            && applied.Operation == BaseSemanticActivationOperationKind.Ensure)
            expected = captured.Live?.Checksum ?? [];
        else if (applied.PriorState == BaseSemanticActivationCapturedState.Retired)
            expected = captured.Retired?.Checksum ?? [];
        else if (applied.PriorState == BaseSemanticActivationCapturedState.CompactedAbsent)
            expected = captured.Absent?.Checksum ?? [];
        else if (applied.PriorState == BaseSemanticActivationCapturedState.Missing
            && plan.Operation is BaseSemanticActivationEnsureIntent ensure
            && captured.Missing is not null)
        {
            var live = new BaseSemanticActivationLiveAuthority
            {
                Definition = ensure.Definition, KeyDigest = ensure.Key, Scope = ensure.Scope,
                ScopeBinding = captured.ScopeDirectory.ResultingBinding, SubjectLifetime = ensure.SubjectLifetime,
                ActivationId = applied.ActivationId!, ActivationDefinition = ensure.Activation.Definition,
                InputChecksum = ensure.Activation.InputChecksum, Due = ensure.Due,
                SlotGeneration = applied.ResultingSlotGeneration, StoreAuthority = captured.Missing.StoreAuthority, Checksum = [],
            };
            expected = BaseSemanticActivationEvidenceContract.LiveChecksum(live);
        }
        else if (applied.PriorState == BaseSemanticActivationCapturedState.Live
            && plan.Operation is BaseSemanticActivationRetireIntent retire
            && captured.Live is not null && captured.ActivationState is { } terminalState
            && captured.ActivationGeneration is { } terminalGeneration
            && captured.ActivationChecksum.Length == 32 && captured.ActivationTerminalReceiptChecksum.Length == 32)
        {
            var retired = new BaseSemanticActivationRetirementAuthority
            {
                Definition = new BaseSemanticActivationDefinitionKey
                {
                    Id = retire.Definition.Id, Version = retire.Definition.Version, Checksum = retire.Definition.Checksum,
                },
                KeyDigest = retire.Key, SubjectLifetime = retire.SubjectLifetime, ActivationId = applied.ActivationId!,
                TerminalState = terminalState, TerminalActivationGeneration = terminalGeneration,
                TerminalActivationChecksum = captured.ActivationChecksum,
                CompletionOperationChecksum = Convert.FromHexString(retire.CompletionOperation.OperationChecksum).ToImmutableArray(),
                CompletionReceiptChecksum = captured.ActivationTerminalReceiptChecksum,
                RetirementPosition = applied.CommitJournalPosition, SlotGeneration = applied.ResultingSlotGeneration,
                StoreAuthority = captured.Live.StoreAuthority, Checksum = [],
            };
            expected = BaseSemanticActivationEvidenceContract.RetirementChecksum(retired);
        }
        else return false;
        return expected.Length == 32
            && CryptographicOperations.FixedTimeEquals(expected.AsSpan(), applied.ResultingSlotChecksum.AsSpan());
    }

    private static bool SemanticReadIntervalsEqual(
        ImmutableArray<BaseAtomicReadIntervalEvidence> left,
        ImmutableArray<BaseAtomicReadIntervalEvidence> right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
        {
            BaseAtomicReadIntervalEvidence a = left[index];
            BaseAtomicReadIntervalEvidence b = right[index];
            if (a.LogicalAccessPathId != b.LogicalAccessPathId || a.LowerInclusive != b.LowerInclusive
                || a.UpperInclusive != b.UpperInclusive
                || !a.CanonicalLowerBound.AsSpan().SequenceEqual(b.CanonicalLowerBound.AsSpan())
                || !a.CanonicalUpperBound.AsSpan().SequenceEqual(b.CanonicalUpperBound.AsSpan())) return false;
        }
        return true;
    }

    private static bool SemanticAccountingWithin(BaseSemanticActivationAccounting value, BaseSemanticActivationExecutionLimits limits) =>
        value.Operations <= limits.MaximumOperations
        && value.ScopeDirectoryReads <= limits.MaximumScopeDirectoryReads
        && value.SlotReads <= limits.MaximumSlotReads
        && value.ActivationReads <= limits.MaximumActivationReads
        && value.ReadIntervals <= limits.MaximumReadIntervals
        && value.IndexOperations <= limits.MaximumIndexOperations
        && value.ActivationBytes <= limits.MaximumActivationBytes
        && value.ScopeDirectoryBytes <= limits.MaximumScopeDirectoryBytes
        && value.EvidenceBytes <= limits.MaximumEvidenceBytes
        && value.ReceiptBytes <= limits.MaximumReceiptBytes
        && value.TransientBytes <= limits.MaximumTransientBytes;

    private static BaseSemanticActivationReceiptEvidence? CreateSemanticReceipt(
        BaseAtomicSemanticActivationExtension? extension,
        BaseCapturedSemanticActivationEvidence? captured,
        BaseProvisionalSemanticActivation? applied)
    {
        if (extension is null) return applied is null ? null : throw new InvalidOperationException();
        if (applied is null) throw new InvalidOperationException();
        BaseSemanticActivationDefinitionIdentity definition;
        BaseSemanticActivationKeyDigest key;
        if (extension.Operation is BaseSemanticActivationEnsureIntent ensure) { definition = ensure.Definition; key = ensure.Key; }
        else if (extension.Operation is BaseSemanticActivationRetireIntent retire) { definition = retire.Definition; key = retire.Key; }
        else throw new InvalidOperationException();
        BaseSemanticActivationEnsureDisposition? ensureDisposition = applied.Operation == BaseSemanticActivationOperationKind.Ensure
            ? applied.PriorState switch
            {
                BaseSemanticActivationCapturedState.Missing => BaseSemanticActivationEnsureDisposition.Created,
                BaseSemanticActivationCapturedState.Live => BaseSemanticActivationEnsureDisposition.Existing,
                _ => BaseSemanticActivationEnsureDisposition.Retired,
            } : null;
        BaseSemanticActivationRetirementDisposition? retirementDisposition = applied.Operation == BaseSemanticActivationOperationKind.Retire
            ? applied.PriorState switch
            {
                BaseSemanticActivationCapturedState.Live => BaseSemanticActivationRetirementDisposition.RetiredNow,
                BaseSemanticActivationCapturedState.Retired => BaseSemanticActivationRetirementDisposition.AlreadyRetired,
                _ => BaseSemanticActivationRetirementDisposition.AlreadyCompacted,
            } : null;
        byte[] slotChecksum = applied.ResultingSlotChecksum.ToArray();
        byte[] commitChecksum = SemanticHash("base.semanticActivation.commit.v1\0", slotChecksum,
            BitConverter.GetBytes(applied.CommitJournalPosition).Reverse().ToArray());
        BaseSemanticRecoveryLocalReceiptAuthority? recovery = CreateRecoveryReceipt(extension, captured, applied);
        var receipt = new BaseSemanticActivationReceiptEvidence
        {
            Operation = applied.Operation, DefinitionId = definition.Id, DefinitionVersion = definition.Version,
            DefinitionChecksum = definition.Checksum.ToArray().ToImmutableArray(), Key = key,
            State = applied.ResultingState, SlotGeneration = applied.ResultingSlotGeneration,
            EnsureDisposition = ensureDisposition, RetirementDisposition = retirementDisposition,
            ActivationId = applied.ResultingState == BaseSemanticActivationSlotState.Live ? applied.ActivationId : null,
            SlotChecksum = slotChecksum.ToImmutableArray(), JournalPosition = applied.CommitJournalPosition,
            CommitEvidenceChecksum = commitChecksum.ToImmutableArray(), RecoveryPublication = recovery,
            Checksum = [],
        };
        return receipt with { Checksum = BaseSemanticActivationEvidenceContract.ReceiptChecksum(receipt) };
    }

    private static BaseSemanticRecoveryLocalReceiptAuthority? CreateRecoveryReceipt(
        BaseAtomicSemanticActivationExtension extension,
        BaseCapturedSemanticActivationEvidence? captured,
        BaseProvisionalSemanticActivation applied)
    {
        BaseSemanticRecoveryPendingCommitAuthority? pending = extension.Capture.RecoveryPending;
        if (pending is null) return null;
        if (extension.Operation is not BaseSemanticActivationRetireIntent retire || captured?.Live is not { } live
            || captured.ActivationState is not { } terminal || captured.ActivationGeneration is not { } generation
            || applied.PriorState != BaseSemanticActivationCapturedState.Live
            || applied.ResultingState != BaseSemanticActivationSlotState.Retired) throw new InvalidOperationException();
        var retired = new BaseSemanticActivationRetirementAuthority
        {
            Definition = new BaseSemanticActivationDefinitionKey { Id = retire.Definition.Id, Version = retire.Definition.Version, Checksum = retire.Definition.Checksum },
            KeyDigest = retire.Key, SubjectLifetime = retire.SubjectLifetime, ActivationId = live.ActivationId,
            TerminalState = terminal, TerminalActivationGeneration = generation,
            TerminalActivationChecksum = captured.ActivationChecksum,
            CompletionOperationChecksum = Convert.FromHexString(retire.CompletionOperation.OperationChecksum).ToImmutableArray(),
            CompletionReceiptChecksum = captured.ActivationTerminalReceiptChecksum,
            RetirementPosition = applied.CommitJournalPosition, SlotGeneration = applied.ResultingSlotGeneration,
            StoreAuthority = live.StoreAuthority, Checksum = [],
        };
        retired = retired with { Checksum = BaseSemanticActivationEvidenceContract.RetirementChecksum(retired) };
        if (!CryptographicOperations.FixedTimeEquals(retired.Checksum.AsSpan(), applied.ResultingSlotChecksum.AsSpan()))
            throw new InvalidOperationException();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(retired, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority);
        var entry = new BaseSemanticActivationRecoveryEntry
        {
            Boundary = pending.Intent.Boundary,
            Definition = retired.Definition,
            State = BaseSemanticActivationSlotState.Retired,
            SlotGeneration = retired.SlotGeneration,
            AuthorityBytes = bytes.ToImmutableArray(), Checksum = [],
        };
        entry = entry with { Checksum = BaseSemanticRecoveryAuthorityContract.RecoveryEntryChecksum(entry) };
        var value = new BaseSemanticRecoveryLocalReceiptAuthority
        {
            PendingAuthority = pending, FinalEntry = entry, Checksum = [],
        };
        return value with { Checksum = BaseSemanticRecoveryAuthorityContract.LocalReceiptAuthorityChecksum(value) };
    }

    private static bool AppliedActivationMatches(
        BaseActivationCreationExtension? plan,
        BasePreparedActivationExtension? prepared,
        BaseProvisionalActivationExtension? applied)
    {
        if (plan is null) return prepared is null && applied is null;
        if (prepared is null || applied is null || applied.Checksum.Length != 32
            || applied.Items.Length != plan.Items.Length || prepared.Items.Length != plan.Items.Length)
            return false;
        for (int ordinal = 0; ordinal < plan.Items.Length; ordinal++)
        {
            BaseProvisionalActivationItem item = applied.Items[ordinal];
            if (item.Ordinal != ordinal || item.ActivationId != prepared.Items[ordinal].ActivationId
                || item.Generation != prepared.Items[ordinal].ResultingGeneration || item.Checksum.Length != 32)
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
