using HPD.Base.Tests.InMemory.TestDoubles;
using System.Collections.Immutable;

namespace HPD.Base.Tests.InMemory.Mutations;

public sealed class AtomicExecutionTests
{
    private static readonly RecordMutationExecutionRequest ExecutionRequest = new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5)
    };

    [Fact]
    public async Task ActivationCreationCommitsAndExactReplayObservesExistingAuthority()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], limits)).Value!;
        var first = new ActivationCreationProbe(authority, limits);
        var second = new ActivationCreationProbe(authority, limits);
        var conflict = new ActivationCreationProbe(authority, limits, "changed-input");

        RecordMutationExecutionResult committed = await store.ExecuteAtomicAsync(first, ExecutionRequest);
        RecordMutationExecutionResult duplicate = await store.ExecuteAtomicAsync(second, ExecutionRequest);
        RecordMutationExecutionResult rejected = await store.ExecuteAtomicAsync(conflict, ExecutionRequest);

        committed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        duplicate.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        first.CapturedExisting.Should().BeFalse();
        second.CapturedExisting.Should().BeTrue();
        first.ProvisionalCount.Should().Be(1);
        second.ProvisionalCount.Should().Be(1);
        rejected.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        conflict.RejectedCode.Should().Be("base.activation.fingerprintConflict");
    }

    [Fact]
    public async Task Activation_due_claim_renew_complete_is_fenced_and_terminal()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits mutationLimits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], mutationLimits)).Value!;
        var creation = new ActivationCreationProbe(authority, mutationLimits);
        (await store.ExecuteAtomicAsync(creation, ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);

        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseAcceptedTimeReceipt now = AcceptedTime(10);
        var scope = new BaseOwnedScopeSeekAuthority
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n"))
                .ToImmutableArray(),
        };
        BaseActivationDefinitionKey definition = new()
        {
            Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray(),
        };
        OperationResult<BaseActivationDueObservation> observationResult = await store.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = "activation-test",
            WorkerModuleId = "test",
            Definitions = [definition],
            Scope = scope,
            AcceptedTime = now,
            MaximumCandidates = 8,
            Limits = limits,
        });
        observationResult.IsSuccess().Should().BeTrue(observationResult.Error?.Code);
        BaseActivationDueObservation observed = observationResult.Value!;
        observed.Earliest!.ActivationId.Should().NotBeNullOrWhiteSpace();

        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker-1",
            Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
        };
        var claimRequest = new BaseActivationClaimRequest
        {
            Observation = observed.Token,
            Worker = worker,
            AcceptedTime = now,
            LeaseMilliseconds = 1_000,
            Identity = RequestIdentity("claim"),
            Limits = limits,
        };
        var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(claimRequest)).Value!;
        BaseActivationRenewResult renewed = (await store.RenewAsync(new BaseActivationRenewRequest
        {
            Claim = claimed.Claim,
            ExpectedLeaseRevision = claimed.Lease.LeaseRevision,
            AcceptedTime = AcceptedTime(20),
            ExtensionMilliseconds = 2_000,
            Identity = RequestIdentity("renew"),
            Limits = limits,
        })).Value!;
        renewed.Claim.FencingToken.Should().Equal(claimed.Claim.FencingToken);
        renewed.Lease.LeaseRevision.Should().Be(2);

        byte[] result = "done"u8.ToArray();
        BaseActivationTransitionResult completed = (await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            Claim = claimed.Claim,
            CanonicalResult = result.ToImmutableArray(),
            ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(),
            AcceptedTime = AcceptedTime(30),
            Identity = RequestIdentity("complete"),
            Limits = limits,
        })).Value!;
        completed.State.Should().Be(BaseActivationState.Succeeded);

        OperationResult<BaseActivationTransitionResult> late = await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            Claim = claimed.Claim,
            CanonicalResult = result.ToImmutableArray(),
            ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(),
            AcceptedTime = AcceptedTime(40),
            Identity = RequestIdentity("late"),
            Limits = limits,
        });
        late.Status.Should().Be(OperationStatus.Conflict);
        late.Error!.Code.Should().Be("base.activation.claimLost");

        var disposalRequest = new BaseActivationDisposeRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            ExpectedGeneration = completed.Generation,
            AcceptedTime = AcceptedTime(45),
            Identity = RequestIdentity("dispose"),
            Limits = limits,
        };
        BaseActivationTransitionResult disposed = (await store.TransitionAsync(disposalRequest)).Value!;
        BaseActivationTransitionResult disposedReplay = (await store.TransitionAsync(disposalRequest with
        {
            AcceptedTime = AcceptedTime(46),
        })).Value!;
        disposed.State.Should().Be(BaseActivationState.Disposed);
        disposedReplay.Generation.Should().Be(disposed.Generation);
        disposedReplay.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);

        BaseActivationDueObservation terminalObservation = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
            AcceptedTime = AcceptedTime(50), MaximumCandidates = 8, Limits = limits,
        })).Value!;
        terminalObservation.Earliest.Should().BeNull();
    }

    [Fact]
    public async Task At_most_once_effect_requires_live_executor_and_recovers_only_to_outcome_unknown()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits mutationLimits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], mutationLimits)).Value!;
        (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits), ExecutionRequest))
            .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        BaseActivationExecutionLimits limits = ActivationLimits();
        var scope = new BaseOwnedScopeSeekAuthority
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
        };
        BaseActivationDefinitionKey definition = new() { Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray() };
        OperationResult<BaseActivationDueObservation> effectObservationResult = await store.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
            AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
        });
        effectObservationResult.IsSuccess().Should().BeTrue(effectObservationResult.Error?.Code);
        BaseActivationDueObservation observed = effectObservationResult.Value!;
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker-1",
            Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
        };
        OperationResult<BaseActivationClaimResult> claimResult = await store.TryClaimNextAsync(new BaseActivationClaimRequest
        {
            Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1_000,
            Identity = RequestIdentity("effect-claim"), Limits = limits,
        });
        claimResult.IsSuccess().Should().BeTrue(claimResult.Error?.Code);
        var claimed = claimResult.Value.Should().BeOfType<BaseActivationClaimedResult>().Subject;
        BaseExecutorRegistrationResult executor = (await store.RegisterExecutorAsync(new BaseExecutorRegistrationRequest
        {
            ApplicationId = "activation-test", HostId = "host", ProcessIncarnationId = "process",
            WorkerDefinitionSetChecksum = new byte[32].ToImmutableArray(), RequestedHeartbeatMilliseconds = 100,
            AcceptedTime = AcceptedTime(20), Identity = RequestIdentity("executor"), Limits = limits,
        })).Value!;
        BaseActivationTransitionResult started = (await store.TransitionAsync(new BaseActivationBeginEffectRequest
        {
            ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim, Executor = executor.Executor,
            ExecutorHeartbeat = executor.Heartbeat, HeartbeatMilliseconds = 100, AcceptedTime = AcceptedTime(20),
            Identity = RequestIdentity("effect-start"), Limits = limits,
        })).Value!;
        started.State.Should().Be(BaseActivationState.EffectStarted);
        started.Effect.Should().NotBeNull();

        BaseActivationTransitionResult cancellation = (await store.TransitionAsync(new BaseActivationCancelRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            ExpectedGeneration = started.Generation,
            Propagation = BaseCancellationPropagation.None,
            AcceptedTime = AcceptedTime(30),
            Identity = RequestIdentity("effect-cancel"),
            Limits = limits,
        })).Value!;
        cancellation.State.Should().Be(BaseActivationState.EffectStarted,
            "cancellation cannot manufacture certainty about an external effect that may have run");
        cancellation.Effect.Should().NotBeNull();

        OperationResult<BaseActivationTransitionResult> premature = await store.TransitionAsync(new BaseActivationRecoverEffectRequest
        {
            ActivationId = claimed.Claim.ActivationId, Effect = started.Effect!, AcceptedTime = AcceptedTime(50),
            Identity = RequestIdentity("premature"), Limits = limits,
        });
        premature.Status.Should().Be(OperationStatus.Conflict);

        BaseActivationTransitionResult unknown = (await store.TransitionAsync(new BaseActivationRecoverEffectRequest
        {
            ActivationId = claimed.Claim.ActivationId, Effect = started.Effect!, AcceptedTime = AcceptedTime(200),
            Identity = RequestIdentity("recover"), Limits = limits,
        })).Value!;
        unknown.State.Should().Be(BaseActivationState.OutcomeUnknown);

        byte[] verification = "verified-complete"u8.ToArray();
        var reconciliation = new BaseActivationReconcileEffectRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            ExpectedEffectStartGeneration = started.Effect!.EffectStartGeneration,
            ExpectedEffectChecksum = started.Effect.Checksum,
            ExpectedGeneration = unknown.Generation,
            Disposition = BaseEffectReconciliationDisposition.Succeeded,
            VerificationEvidence = verification.ToImmutableArray(),
            VerificationChecksum = System.Security.Cryptography.SHA256.HashData(verification).ToImmutableArray(),
            AcceptedTime = AcceptedTime(210),
            Identity = RequestIdentity("reconcile"),
            Limits = limits,
        };
        OperationResult<BaseActivationTransitionResult> reconciliationResult = await store.TransitionAsync(reconciliation);
        reconciliationResult.IsSuccess().Should().BeTrue(reconciliationResult.Error?.Code);
        BaseActivationTransitionResult reconciled = reconciliationResult.Value!;
        OperationResult<BaseActivationTransitionResult> replayResult = await store.TransitionAsync(reconciliation with
        {
            AcceptedTime = AcceptedTime(220),
        });
        replayResult.IsSuccess().Should().BeTrue(replayResult.Error?.Code);
        BaseActivationTransitionResult replayed = replayResult.Value!;
        reconciled.State.Should().Be(BaseActivationState.Succeeded);
        replayed.State.Should().Be(BaseActivationState.Succeeded);
        replayed.Generation.Should().Be(reconciled.Generation);
        replayed.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleAndAtomicUseTheSameRestrictedSessionExecutor(bool atomic)
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var processor = new CallbackProcessor(async (session, token) =>
        {
            var create = await session.CreateAsync(
                collection,
                Create("one", "value"),
                Context(BaseRecordMutationKind.Create, "one"),
                token);
            return Ready(create);
        });

        var execution = atomic
            ? await store.ExecuteAtomicAsync(processor, ExecutionRequest)
            : await store.ExecuteSingleAsync(processor, ExecutionRequest);

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        processor.InvocationCount.Should().Be(1);
        var read = await store.GetAsync(
            collection,
            new RecordId("one"),
            InMemoryTestData.Operation(BaseOperationKind.Get));
        read.Status.Should().Be(OperationStatus.Ok);
    }

    [Fact]
    public async Task AtomicSessionProvidesOrderedReadYourWritesAcrossCollections()
    {
        var store = new InMemoryRecordStore();
        var firstCollection = InMemoryTestData.Collection("first");
        var secondCollection = InMemoryTestData.Collection("second");
        var processor = new CallbackProcessor(async (session, token) =>
        {
            var created = await session.CreateAsync(
                firstCollection,
                Create("shared", "before"),
                Context(BaseRecordMutationKind.Create, "create"),
                token);
            created.Status.Should().Be(OperationStatus.Created);

            var observed = await session.GetAsync(
                firstCollection,
                new RecordId("shared"),
                InMemoryTestData.Operation(BaseOperationKind.Get, "first"),
                token);
            observed.Value!.Payload.Fields!["title"].GetString().Should().Be("before");

            var patched = await session.PatchAsync(
                firstCollection,
                new RecordId("shared"),
                new RecordPatchRequest { Patch = InMemoryTestData.Patch("title", "after") },
                Context(BaseRecordMutationKind.Patch, "patch"),
                token);
            var second = await session.CreateAsync(
                secondCollection,
                Create("other", "second"),
                Context(BaseRecordMutationKind.Create, "second"),
                token);
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.ReadyToCommit,
                [created.Value!.Mutation, patched.Value!.Mutation, second.Value!.Mutation]);
        });

        var execution = await store.ExecuteAtomicAsync(processor, ExecutionRequest);

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        var first = await store.GetAsync(
            firstCollection,
            new RecordId("shared"),
            InMemoryTestData.Operation(BaseOperationKind.Get, "first"));
        var second = await store.GetAsync(
            secondCollection,
            new RecordId("other"),
            InMemoryTestData.Operation(BaseOperationKind.Get, "second"));
        first.Value!.Payload.Fields!["title"].GetString().Should().Be("after");
        second.Status.Should().Be(OperationStatus.Ok);
    }

    [Fact]
    public async Task FailedAtomicExecutionRollsBackRecordsAndCounters()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var failed = new CallbackProcessor(async (session, token) =>
        {
            var create = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    Payload = InMemoryTestData.Payload(("title", "discard"))
                },
                Context(BaseRecordMutationKind.Create, "discard"),
                token);
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                [create.Value!.Mutation],
                Error("test.rollback", "Force rollback."));
        });

        var failure = await store.ExecuteAtomicAsync(failed, ExecutionRequest);
        var committed = await InMemoryMutationTestDriver.CreateAsync(
            store,
            collection,
            new RecordCreateRequest
            {
                Payload = InMemoryTestData.Payload(("title", "kept"))
            },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        failure.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        committed.Value!.Id.Value.Should().Be("mem:0000000000000001");
        committed.Value.Metadata.Revision!.Value.Value.Should().Be("mem:0000000000000001");
    }

    [Fact]
    public async Task ConcurrentCommitCausesConfirmedConflictWithoutRetryOrLostWrite()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var losingProcessor = new CallbackProcessor(async (session, token) =>
        {
            var create = await session.CreateAsync(
                collection,
                Create("loser", "discard"),
                Context(BaseRecordMutationKind.Create, "loser"),
                token);
            staged.SetResult();
            await release.Task.WaitAsync(token);
            return Ready(create);
        });

        var losingExecution = store.ExecuteAtomicAsync(
            losingProcessor,
            ExecutionRequest).AsTask();
        await staged.Task;
        var winning = await InMemoryMutationTestDriver.CreateAsync(
            store,
            collection,
            Create("winner", "keep"),
            InMemoryTestData.Operation(BaseOperationKind.Create));
        release.SetResult();
        var conflict = await losingExecution;

        winning.Status.Should().Be(OperationStatus.Created);
        conflict.Outcome.Should().Be(RecordMutationExecutionOutcome.ConflictRollbackConfirmed);
        conflict.Error!.Code.Should().Be(BaseMutationErrorCodes.TransactionConflict);
        conflict.Error.Store!.Retryable.Should().BeFalse();
        losingProcessor.InvocationCount.Should().Be(1);
        (await store.GetAsync(
            collection,
            new RecordId("winner"),
            InMemoryTestData.Operation(BaseOperationKind.Get))).Status.Should().Be(OperationStatus.Ok);
        (await store.GetAsync(
            collection,
            new RecordId("loser"),
            InMemoryTestData.Operation(BaseOperationKind.Get))).Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task SessionFailsClosedAfterProviderInvocationCompletes()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        IAtomicRecordSession? retained = null;
        var processor = new CallbackProcessor(async (session, token) =>
        {
            retained = session;
            var create = await session.CreateAsync(
                collection,
                Create("one", "value"),
                Context(BaseRecordMutationKind.Create, "one"),
                token);
            return Ready(create);
        });

        var execution = await store.ExecuteSingleAsync(processor, ExecutionRequest);
        var escapedUse = await retained!.GetAsync(
            collection,
            new RecordId("one"),
            InMemoryTestData.Operation(BaseOperationKind.Get));

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        escapedUse.Status.Should().Be(OperationStatus.StoreError);
        escapedUse.Error!.Store!.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task PreparedModuleOperationIsSessionBoundAndSingleUse()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "module.application", [], limits, default)).Value!;
        var prepareOnly = new PreparedModuleProbe(authority, limits, applyTwice: false);
        await store.ExecuteAtomicAsync(prepareOnly, ExecutionRequest);
        prepareOnly.Prepared.Should().NotBeNull();

        var foreign = new ForeignPreparedModuleProbe(prepareOnly.Prepared!);
        await store.ExecuteAtomicAsync(foreign, ExecutionRequest);
        foreign.RejectedCode.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);

        var twice = new PreparedModuleProbe(authority, limits, applyTwice: true);
        await store.ExecuteAtomicAsync(twice, ExecutionRequest);
        twice.RejectedCode.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);
    }

    [Fact]
    public async Task ModuleGenerationAccountingIsEnforcedAtTheMeasuredBoundary()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits generous = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "module.application", [], generous, default)).Value!;
        var baseline = new PreparedModuleProbe(authority, generous, applyTwice: false);
        await store.ExecuteAtomicAsync(baseline, ExecutionRequest);
        BasePreparedAtomicMutationAccounting measured = baseline.Prepared!.Accounting;

        BaseAtomicMutationExecutionLimits exact = generous with
        {
            MaximumGenerationReads = measured.GenerationReads,
            MaximumGenerationComparisons = measured.GenerationComparisons,
            MaximumGenerationIncrements = measured.GenerationIncrements,
            MaximumGenerationBytes = measured.GenerationBytes,
            MaximumReadIntervals = measured.ReadIntervals,
            MaximumEvidenceBytes = measured.EvidenceBytes,
            MaximumTransientBytes = measured.TransientBytes,
        };
        var accepted = new PreparedModuleProbe(authority, exact, applyTwice: false);
        await store.ExecuteAtomicAsync(accepted, ExecutionRequest);
        accepted.Prepared.Should().NotBeNull();

        var rejected = new PreparedModuleProbe(authority, exact with
        {
            MaximumGenerationIncrements = checked(measured.GenerationIncrements - 1),
        }, applyTwice: false);
        await store.ExecuteAtomicAsync(rejected, ExecutionRequest);
        rejected.Prepared.Should().BeNull();
        rejected.RejectedCode.Should().Be(BaseSubjectErrorCodes.BudgetExceeded);

        rejected = new PreparedModuleProbe(authority, exact with
        {
            MaximumGenerationComparisons = checked(measured.GenerationComparisons - 1),
        }, applyTwice: false);
        await store.ExecuteAtomicAsync(rejected, ExecutionRequest);
        rejected.Prepared.Should().BeNull();
        rejected.RejectedCode.Should().Be(BaseSubjectErrorCodes.BudgetExceeded);
    }

    [Fact]
    public void CapabilitiesAdvertiseOnlyProvenL30GuaranteesAndBounds()
    {
        var capabilities = new InMemoryRecordStore().Capabilities;

        capabilities.Read.List.Should().BeTrue();
        capabilities.Read.Get.Should().BeTrue();
        capabilities.Mutation.Create.Should().BeTrue();
        capabilities.Revision!.Patch.Should().BeTrue();
        capabilities.Revision.Replace.Should().BeTrue();
        capabilities.Revision.Delete.Should().BeTrue();
        capabilities.Batch!.Modes.Should().Equal(BaseRecordBatchExecutionMode.Atomic);
        capabilities.Batch.MaxOperations.Should().Be(100);
        capabilities.Batch.MaxCanonicalPayloadBytes.Should().Be(1_048_576);
        capabilities.Batch.Ordered.Should().BeTrue();
        capabilities.Batch.CrossCollectionAtomic.Should().BeTrue();
        capabilities.Batch.ReadYourWrites.Should().BeTrue();
        capabilities.Batch.Durable.Should().BeFalse();
        capabilities.Batch.TransactionalJournal.Should().BeTrue();
        capabilities.Batch.Isolation.Should().Be(BaseTransactionIsolation.Serializable);
        capabilities.Batch.NestedTransactions.Should().BeFalse();
        capabilities.Batch.Savepoints.Should().BeFalse();
        capabilities.Upsert!.Atomic.Should().BeTrue();
        capabilities.Upsert.UpdateModes.Should().Equal(
            RecordUpsertUpdateMode.Patch,
            RecordUpsertUpdateMode.Replace);
        capabilities.Upsert.ExpectedRevision.Should().BeTrue();
        capabilities.Upsert.ExistenceConditions.Should().BeTrue();
    }

    [Fact]
    public void AtomicUpsertIsNotAdvertisedWhenRequestedIdsAreDisabled()
    {
        var capabilities = new InMemoryRecordStore(
            new HPDBaseInMemoryStoreOptions { AllowClientRequestedIds = false }).Capabilities;

        capabilities.Upsert.Should().BeNull();
    }

    private static RecordCreateRequest Create(string id, string title) => new()
    {
        RequestedId = new RecordId(id),
        Payload = InMemoryTestData.Payload(("title", title))
    };

    private static RecordMutationSessionContext Context(
        BaseRecordMutationKind mutation,
        string eventId) => new()
    {
        RequestedOperation = mutation,
        EventId = eventId,
        Operation = InMemoryTestData.Operation(
            mutation switch
            {
                BaseRecordMutationKind.Create => BaseOperationKind.Create,
                BaseRecordMutationKind.Patch => BaseOperationKind.Patch,
                BaseRecordMutationKind.Replace => BaseOperationKind.Replace,
                BaseRecordMutationKind.Delete => BaseOperationKind.Delete,
                _ => BaseOperationKind.Upsert
            })
    };

    private static AtomicMutationProcessingResult Ready(
        OperationResult<RecordMutationSessionResult> result)
    {
        result.Value.Should().NotBeNull();
        return new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            [result.Value!.Mutation]);
    }

    private static BaseError Error(string code, string message) => new()
    {
        Code = code,
        Message = message,
        Category = ErrorCategory.Unexpected
    };

    private static BaseAtomicMutationExecutionLimits ModuleLimits() =>
        DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits);

    private static BaseActivationExecutionLimits ActivationLimits() => new()
    {
        MaximumCandidates = 8,
        MaximumInputBytes = 4096,
        MaximumResultBytes = 4096,
        MaximumEvidenceBytes = 4096,
        MaximumTransientBytes = 16384,
        MaximumReadIntervals = 8,
        MaximumIndexOperations = 16,
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitObservationTimeout = TimeSpan.FromSeconds(5),
        ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
    };

    private static BaseAcceptedTimeReceipt AcceptedTime(long milliseconds)
    {
        const string applicationId = "activation-test";
        const long generation = 1;
        long monotonic = milliseconds;
        long sequence = checked(milliseconds + 1);
        const long maximumForwardSkew = 30_000;
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        Append(hash, "base.activation.acceptedTime.v2\0");
        Append(hash, applicationId);
        Append(hash, generation);
        Append(hash, milliseconds);
        Append(hash, monotonic);
        Append(hash, sequence);
        Append(hash, maximumForwardSkew);
        return new BaseAcceptedTimeReceipt(
            applicationId, generation, milliseconds, monotonic, sequence, maximumForwardSkew, hash.GetHashAndReset());
    }

    private static void Append(System.Security.Cryptography.IncrementalHash hash, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length));
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void Append(System.Security.Cryptography.IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static BaseMutationRequestIdentity RequestIdentity(string id) =>
        BaseMutationRequestIdentity.Create(
            "activation-test", "activation", id, BaseMutationRequestFingerprint.Create(new byte[32]));

    private static BaseModuleGenerationCellDefinition ModuleCell() => new()
    {
        Id = "module.generation", Version = 1, OwningModuleId = "module",
        Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32,
        MaximumCellsPerOperation = 1,
    };

    private static AtomicMutationProcessingResult ProbeFailure(BaseError? error) => new(
        AtomicMutationProcessingOutcome.Failed, [], error ?? new BaseError
        {
            Code = BaseSubjectErrorCodes.ProviderContractInvalid,
            Message = "The prepared-operation probe intentionally rolled back.",
            Category = ErrorCategory.Store,
        });

    private sealed class PreparedModuleProbe(
        BaseAtomicMutationAuthorityRequirement authority,
        BaseAtomicMutationExecutionLimits limits,
        bool applyTwice) : IAtomicMutationProcessor
    {
        public BasePreparedAtomicExecution? Prepared { get; private set; }
        public string? RejectedCode { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            var capture = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
                Intent = new BaseAtomicMutationIntent
                {
                    IntentDigest = "in-memory-l50-probe-intent", Authority = authority, Items = [],
                },
                Module = new BaseModuleMutationCaptureExtension
                {
                    OperationId = "module.increment", OperationVersion = 1,
                    OperationChecksum = new string('a', 64), RequestDigest = "in-memory-l50-probe-request",
                    Records = [], RelationTargets = [],
                    Generations = [new BaseModuleGenerationCaptureRequest
                    {
                        Ordinal = 0, CaptureId = "generation", Cell = ModuleCell(),
                        Scope = new BaseModuleGenerationScopeAuthority { Kind = BaseModuleGenerationScope.Application },
                        KeyUtf8 = [], Absence = BaseModuleGenerationAbsenceBehavior.AllowEither,
                    }],
                },
                Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured =
                await session.CaptureAtomicExecutionAsync(capture, cancellationToken);
            if (!captured.IsSuccess() || captured.Value is null)
            {
                RejectedCode = captured.Error?.Code;
                return ProbeFailure(captured.Error);
            }
            var plan = new BaseFinalizedAtomicExecutionPlan
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation, PlanDigest = "in-memory-l50-probe-plan",
                IntentDigest = capture.Intent.IntentDigest, CaptureDigest = captured.Value.CaptureDigest,
                PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]), Authority = authority,
                Items = [], SubjectValidations = [], Limits = limits,
                Module = new BaseFinalizedModuleMutationExtension
                {
                    OperationId = "module.increment", OperationVersion = 1,
                    OperationChecksum = new string('a', 64), Decisions = [], ItemBindings = [],
                    RelationTargets = [], Comparisons = [new BaseModuleGenerationComparison
                    {
                        CaptureOrdinal = 0, Kind = BaseModuleGenerationComparisonKind.MustBeMissing,
                    }],
                    Increments = [new BaseModuleGenerationIncrement { CaptureOrdinal = 0, CreateIfAbsent = true }],
                    ResultProjectionDigest = "in-memory-l50-probe-result",
                },
            };
            OperationResult<BasePreparedAtomicExecution> prepared =
                await session.PrepareAtomicExecutionAsync(captured.Value, plan, cancellationToken);
            if (!prepared.IsSuccess() || prepared.Value is null)
            {
                RejectedCode = prepared.Error?.Code;
                return ProbeFailure(prepared.Error);
            }
            Prepared = prepared.Value;
            if (!applyTwice) return ProbeFailure(null);
            OperationResult<BaseProvisionalAtomicExecution> first =
                await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            if (!first.IsSuccess()) return ProbeFailure(first.Error);
            OperationResult<BaseProvisionalAtomicExecution> second =
                await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            RejectedCode = second.Error?.Code;
            return ProbeFailure(second.Error);
        }
    }

    private sealed class ActivationCreationProbe(
        BaseAtomicMutationAuthorityRequirement authority,
        BaseAtomicMutationExecutionLimits limits,
        string inputText = "activation-input") : IAtomicMutationProcessor
    {
        public bool CapturedExisting { get; private set; }
        public int ProvisionalCount { get; private set; }
        public string? RejectedCode { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(inputText);
            var extension = new BaseActivationCreationExtension
            {
                StructuralDigest = new byte[32].ToImmutableArray(),
                Items = [new BaseActivationCreateIntent
                {
                    Ordinal = 0,
                    Definition = new BaseActivationDefinitionKey
                    {
                        Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray(),
                    },
                    CanonicalInput = input.ToImmutableArray(),
                    InputChecksum = System.Security.Cryptography.SHA256.HashData(input).ToImmutableArray(),
                    Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
                    RequestedDueAt = 1,
                    EffectiveDueAt = 1,
                    Identity = BaseMutationRequestIdentity.Create(
                        "activation-test", "enqueue", "activation-1",
                        BaseMutationRequestFingerprint.Create(new byte[32])),
                }],
            };
            var request = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ActivationCreation,
                Intent = new BaseAtomicMutationIntent
                {
                    IntentDigest = "activation-intent", Authority = authority, Items = [],
                },
                Activations = extension,
                Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured =
                await session.CaptureAtomicExecutionAsync(request, cancellationToken);
            if (!captured.IsSuccess() || captured.Value?.Activations is null)
            {
                RejectedCode = captured.Error?.Code;
                return ProbeFailure(captured.Error);
            }
            CapturedExisting = captured.Value.Activations.Items[0].Exists;
            var plan = new BaseFinalizedAtomicExecutionPlan
            {
                Kind = BaseAtomicMutationExecutionKind.ActivationCreation,
                PlanDigest = "activation-plan",
                IntentDigest = request.Intent.IntentDigest,
                CaptureDigest = captured.Value.CaptureDigest,
                PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]),
                Authority = authority,
                Items = [], SubjectValidations = [], Activations = extension, Limits = limits,
            };
            OperationResult<BasePreparedAtomicExecution> prepared =
                await session.PrepareAtomicExecutionAsync(captured.Value, plan, cancellationToken);
            if (!prepared.IsSuccess() || prepared.Value?.Activations is null)
                return ProbeFailure(prepared.Error);
            OperationResult<BaseProvisionalAtomicExecution> applied =
                await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            if (!applied.IsSuccess() || applied.Value?.Activations is null)
                return ProbeFailure(applied.Error);
            ProvisionalCount = applied.Value.Activations.Items.Length;
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []);
        }
    }

    private sealed class ForeignPreparedModuleProbe(BasePreparedAtomicExecution prepared) : IAtomicMutationProcessor
    {
        public string? RejectedCode { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            OperationResult<BaseProvisionalAtomicExecution> result =
                await session.ApplyPreparedAtomicExecutionAsync(prepared, cancellationToken);
            RejectedCode = result.Error?.Code;
            return ProbeFailure(result.Error);
        }
    }

    private sealed class CallbackProcessor(
        Func<IAtomicRecordSession, CancellationToken, ValueTask<AtomicMutationProcessingResult>> callback)
        : IAtomicMutationProcessor
    {
        public int InvocationCount { get; private set; }

        public ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return callback(session, cancellationToken);
        }
    }
}
