using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HPD.Base.Tests.Runtime.Activations;

public sealed partial class ActivationRuntimeTests
{
    [Fact]
    public void Hosted_dispatch_is_explicit_and_provider_backed()
    {
        var services = new ServiceCollection();

        services.AddHPDBaseActivationWorkers(options =>
        {
            options.WorkerSubjectId = "test.activation.worker";
            options.EmptyPollInterval = TimeSpan.FromMilliseconds(25);
        });

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(BaseActivationHostedDispatcher));
    }

    [Fact]
    public async Task Handler_context_publishes_replacement_lease_and_bounds_renewal()
    {
        var claim = new BaseActivationClaimAuthority
        {
            ActivationId = "activation", AttemptNumber = 1, ActivationGeneration = 1, ClaimEpoch = 1,
            FencingToken = new byte[32].ToImmutableArray(), WorkerIdentity = "worker",
            CancellationGeneration = 0, StoreInstanceId = "store", RestoreEpoch = 1,
            DefinitionChecksum = new byte[32].ToImmutableArray(),
            ExecutionSliceOrdinal = 1, AttemptStartedAt = 1, SliceStartedAt = 1,
            YieldCount = 0, MaximumYields = 0,
        };
        var initial = new BaseActivationLeaseObservation
        { LeaseRevision = 1, LeaseExpiresAt = 100, Checksum = new byte[32].ToImmutableArray() };
        var replacement = initial with { LeaseRevision = 2, LeaseExpiresAt = 200 };
        var context = new BaseActivationContext(
            new BaseActivationDefinitionKey { Id = "definition", Version = 1, Checksum = new byte[32].ToImmutableArray() },
            claim, initial, new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global }, null, 0, 0, 1,
            (_, _) => ValueTask.FromResult(OperationResults.Ok(new BaseActivationRenewResult
            {
                Claim = claim, Lease = replacement, Accounting = new BaseActivationAccounting
                {
                    Candidates = 1, Comparisons = 1, IndexOperations = 1, ReadIntervals = 0,
                    EvidenceBytes = 1, TransientBytes = 1,
                },
                Disposition = BaseMutationRequestDisposition.Committed,
            })),
            CancellationToken.None, 1, null!);

        OperationResult<BaseActivationLeaseObservation> renewed = await context.RenewAsync();
        OperationResult<BaseActivationLeaseObservation> exceeded = await context.RenewAsync();

        renewed.Value!.LeaseRevision.Should().Be(2);
        context.Lease.LeaseRevision.Should().Be(2);
        exceeded.Error!.Code.Should().Be("base.activation.budgetExceeded");
    }

    [Fact]
    public void Handler_context_owns_every_same_store_child_guard_binding()
    {
        BaseActivationContext context = Context(maximumChildren: 5);
        BaseMutationRequestIdentity recordIdentity = Identity("record-child", "one");
        BaseMutationRequestIdentity selectionIdentity = Identity("selection-child", "one");
        BaseMutationRequestIdentity lifecycleIdentity = Identity("lifecycle-child", "one");
        BaseMutationRequestIdentity retirementIdentity = Identity("retirement-child", "one");

        BaseBatchBuilder record = context.GuardRecordMutations("record-child", 1, recordIdentity);
        BaseSelectionMutationExecutionOptions selection = context.GuardSelectionMutation(
            "selection-child", 2, selectionIdentity);
        BaseActivationGuard lifecycle = context.GuardLifecycleCheckpoint(
            "lifecycle-child", 3, lifecycleIdentity);
        BaseActivationGuard retirement = context.GuardRetirementAcknowledgement(
            "retirement-child", 4, retirementIdentity);

        record.Should().NotBeNull();
        selection.ActivationGuard!.ChildRequestFingerprint.Should().Equal(selectionIdentity.Fingerprint.ToArray());
        lifecycle.ChildRequestFingerprint.Should().Equal(lifecycleIdentity.Fingerprint.ToArray());
        retirement.ChildRequestFingerprint.Should().Equal(retirementIdentity.Fingerprint.ToArray());
        context.GuardSelectionMutation("selection-child", 2, selectionIdentity)
            .ActivationGuard!.ChildRequestFingerprint.Should().Equal(selectionIdentity.Fingerprint.ToArray());
        BaseMutationRequestIdentity substituted = BaseMutationRequestIdentity.Create(
            selectionIdentity.Scope, selectionIdentity.Operation, selectionIdentity.IdempotencyKey,
            BaseMutationRequestFingerprint.Create(SHA256.HashData("substituted"u8)));
        Action conflict = () => context.GuardSelectionMutation("selection-child", 2, substituted);
        conflict.Should().Throw<InvalidOperationException>().WithMessage("base.activation.childIdentityConflict");
    }

    [Fact]
    public void Handler_context_derives_child_receipt_identity_from_the_exact_execution_slice()
    {
        BaseMutationRequestFingerprint fingerprint = BaseMutationRequestFingerprint.Create(
            SHA256.HashData("slice-bound-child"u8));
        BaseMutationRequestIdentity first = Context(maximumChildren: 1, executionSliceOrdinal: 1)
            .DeriveChildIdentity("cohort", 1, fingerprint);
        BaseMutationRequestIdentity second = Context(maximumChildren: 1, executionSliceOrdinal: 2)
            .DeriveChildIdentity("cohort", 1, fingerprint);

        first.Scope.Should().Be("activation:activation:slice:1");
        second.Scope.Should().Be("activation:activation:slice:2");
        second.Scope.Should().NotBe(first.Scope);
    }

    [Fact]
    public void Activation_session_resolves_exact_declared_child_source_grants()
    {
        BaseActivationHandlerRegistration<Input, Result> registration =
            BaseActivationDefinitionBuilder.CreateGenerated(
                DefinitionDraft("test.activation.sources", BaseActivationExecutionClass.AtLeastOnceWorker) with
                {
                    SourceGrantIds = ["base.subjectLifecycle.finalizeRetirement", "example.user.retirement.purge.source"],
                },
                RuntimeActivationDtos.HPDBaseActivationDtoAuthority,
                static _ => new Handler());
        var registry = new BaseActivationRegistry([new BaseActivationRegistration<Input, Result>(registration)]);
        using ServiceProvider services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
        var claim = new BaseActivationClaimAuthority
        {
            ActivationId = "activation", AttemptNumber = 1, ActivationGeneration = 1, ClaimEpoch = 1,
            FencingToken = new byte[32].ToImmutableArray(), WorkerIdentity = "worker",
            CancellationGeneration = 0, StoreInstanceId = "store", RestoreEpoch = 1,
            DefinitionChecksum = registration.Definition.Checksum,
            ExecutionSliceOrdinal = 1, AttemptStartedAt = 1, SliceStartedAt = 1,
            YieldCount = 0, MaximumYields = 0,
        };
        BaseSession session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "system",
            },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            services: services,
            applicationId: "activation-test").WithActivationProvenance(claim);

        session.ActivationDeclaresSourceGrants("base.subjectLifecycle.finalizeRetirement").Should().BeTrue();
        session.ActivationDeclaresSourceGrants(
            "base.subjectLifecycle.finalizeRetirement",
            "example.user.retirement.purge.source").Should().BeTrue();
        session.ActivationDeclaresSourceGrants("base.subjectRetirement.purge").Should().BeFalse();
    }

    [Fact]
    public void Captured_guard_evidence_rejects_every_static_authority_substitution()
    {
        BaseActivationContext context = Context(maximumChildren: 1);
        BaseActivationGuard guard = context.GuardLifecycleCheckpoint("child", 1, Identity("child", "one"));
        BaseCapturedActivationGuardEvidence evidence = BaseActivationGuardEvidenceContract.Create(guard, 7, 3, 100);

        BaseActivationGuardEvidenceContract.Matches(guard, evidence).Should().BeTrue();
        BaseActivationGuard[] substitutions =
        [
            guard with { Claim = guard.Claim with { AttemptNumber = 2 } },
            guard with { Claim = guard.Claim with { ClaimEpoch = 2 } },
            guard with { Claim = guard.Claim with { FencingToken = SHA256.HashData("fence"u8).ToImmutableArray() } },
            guard with { Claim = guard.Claim with { WorkerIdentity = "other" } },
            guard with { Claim = guard.Claim with { CancellationGeneration = 1 } },
            guard with { Claim = guard.Claim with { StoreInstanceId = "other" } },
            guard with { Claim = guard.Claim with { RestoreEpoch = 2 } },
            guard with { Claim = guard.Claim with { DefinitionChecksum = SHA256.HashData("definition"u8).ToImmutableArray() } },
            guard with { StepId = "other" },
            guard with { ChildOrdinal = 2 },
            guard with { ChildRequestFingerprint = SHA256.HashData("request"u8).ToImmutableArray() },
        ];

        substitutions.Should().OnlyContain(value => !BaseActivationGuardEvidenceContract.Matches(value, evidence));
        BaseActivationGuardEvidenceContract.Matches(
            guard, evidence with { Checksum = SHA256.HashData("evidence"u8).ToImmutableArray() }).Should().BeFalse();
    }

    private static BaseActivationContext Context(int maximumChildren, long executionSliceOrdinal = 1)
    {
        var claim = new BaseActivationClaimAuthority
        {
            ActivationId = "activation", AttemptNumber = 1, ActivationGeneration = 1, ClaimEpoch = 1,
            FencingToken = new byte[32].ToImmutableArray(), WorkerIdentity = "worker",
            CancellationGeneration = 0, StoreInstanceId = "store", RestoreEpoch = 1,
            DefinitionChecksum = new byte[32].ToImmutableArray(),
            ExecutionSliceOrdinal = executionSliceOrdinal, AttemptStartedAt = 1, SliceStartedAt = 1,
            YieldCount = 0, MaximumYields = 0,
        };
        return new BaseActivationContext(
            new BaseActivationDefinitionKey { Id = "definition", Version = 1, Checksum = new byte[32].ToImmutableArray() },
            claim,
            new BaseActivationLeaseObservation { LeaseRevision = 1, LeaseExpiresAt = 100, Checksum = new byte[32].ToImmutableArray() },
            new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global }, null, 0, 0, 1,
            (_, _) => throw new InvalidOperationException(), CancellationToken.None, maximumChildren, null!);
    }

    [Fact]
    public void Transactional_activation_is_handler_free_and_target_bound()
    {
        BaseTransactionalActivationRegistration<Input, Result> registration =
            BaseActivationDefinitionBuilder.CreateGeneratedTransactional(
            DefinitionDraft("test.activation.transactional", BaseActivationExecutionClass.TransactionalOperation) with
            {
                Handler = null, TransactionalTarget = new BaseModuleMutationActivationTarget
                {
                    OperationId = "test.module.operation",
                    OperationVersion = 1,
                    OperationChecksum = new string('a', 64),
                },
            }, RuntimeActivationDtos.HPDBaseActivationDtoAuthority);

        registration.Definition.Handler.Should().BeNull();
        registration.Definition.TransactionalTarget.Should().BeOfType<BaseModuleMutationActivationTarget>();
        registration.Definition.Checksum.Should().HaveCount(32);
        Action invalid = () => BaseActivationDefinitionBuilder.CreateGeneratedTransactional(
            DefinitionDraft("test.activation.transactional.invalid", BaseActivationExecutionClass.TransactionalOperation) with
            { TransactionalTarget = null, Handler = null }, RuntimeActivationDtos.HPDBaseActivationDtoAuthority);
        invalid.Should().Throw<InvalidOperationException>().WithMessage("base.activation.definitionInvalid");
    }

    [Fact]
    public async Task Enqueue_is_principal_bound_durable_and_exactly_replayed()
    {
        var store = new InMemoryRecordStore();
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "activation-store", Store = store });
        DefaultBasePolicyOrchestrator policy = Policy();
        var runtime = new DefaultBaseActivationRuntime(stores, policy, TimeProvider.System);
        BaseActivationHandlerRegistration<Input, Result> registration = Registration();
        BaseSession session = Session();
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "activation-test", "test.activation", "one", BaseMutationRequestFingerprint.Create(new byte[32]));

        OperationResult<BaseActivationEnqueueResult> first = await runtime.EnqueueAsync(
            session, registration.Definition, registration.Identity, new Input("work"), identity, null, default);
        BaseActivationDependencyResult dependencies = (await store.ReadDependenciesAsync(
            new BaseActivationDependencyRequest
            {
                ApplicationId = "activation-test", MaximumDefinitions = 8,
                DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            })).Value!;
        OperationResult<BaseActivationEnqueueResult> duplicate = await runtime.EnqueueAsync(
            session, registration.Definition, registration.Identity, new Input("work"), identity, null, default);

        first.IsSuccess().Should().BeTrue(first.Error?.Code);
        dependencies.Dependencies.Should().ContainSingle(item =>
            item.ReferencedByActivation && !item.ReferencedBySchedule
            && item.Definition.Id == registration.Definition.Id
            && item.Definition.Version == registration.Definition.Version);
        duplicate.IsSuccess().Should().BeTrue(duplicate.Error?.Code);
        first.Value!.State.Should().Be(BaseActivationState.Pending);
        first.Value.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        duplicate.Value!.ActivationId.Should().Be(first.Value.ActivationId);
        duplicate.Value.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
    }

    [Fact]
    public async Task Worker_observes_claims_and_completes_one_typed_activation()
    {
        var store = new InMemoryRecordStore();
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "activation-store", Store = store });
        DefaultBasePolicyOrchestrator policy = Policy();
        var enqueue = new DefaultBaseActivationRuntime(stores, policy, TimeProvider.System);
        var worker = new DefaultBaseActivationWorkerRuntime(
            stores, policy, new BaseActivationAcceptedTimeAuthority(TimeProvider.System));
        BaseActivationHandlerRegistration<Input, Result> registration = Registration();
        BaseSession session = Session();

        OperationResult<BaseActivationEnqueueResult> created = await enqueue.EnqueueAsync(
            session, registration.Definition, registration.Identity, new Input("work"),
            Identity("enqueue", "one"), null, default);
        OperationResult<BaseActivationDueObservation> observed = await worker.ObserveAsync(
            session, registration.Definition, default);
        BaseMutationRequestIdentity claimIdentity = Identity("claim", "one");
        OperationResult<BaseActivationClaimResult> claimed = await worker.ClaimAsync(
            session, registration.Definition, observed.Value!.Token, claimIdentity, default);
        var delivery = claimed.Value.Should().BeOfType<BaseActivationClaimedResult>().Subject;
        (await worker.ClaimAsync(session, registration.Definition, observed.Value.Token, claimIdentity, default)).Value
            .Should().BeOfType<BaseActivationClaimedResult>();
        BaseMutationRequestIdentity renewIdentity = Identity("renew", "one");
        OperationResult<BaseActivationRenewResult> renewed = await worker.RenewAsync(
            session, registration.Definition, delivery.Claim, delivery.Lease, renewIdentity, default);
        OperationResult<BaseActivationRenewResult> renewedReplay = await worker.RenewAsync(
            session, registration.Definition, delivery.Claim, delivery.Lease, renewIdentity, default);
        renewed.IsSuccess().Should().BeTrue(renewed.Error?.Code);
        renewedReplay.Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        BaseMutationRequestIdentity completionIdentity = Identity("complete", "one");
        OperationResult<BaseActivationTransitionResult> completed = await worker.CompleteAsync(
            session, registration.Definition, delivery.Claim,
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Result("done"), Json.Default.Result).ToImmutableArray(),
            completionIdentity, default);
        OperationResult<BaseActivationTransitionResult> completedReplay = await worker.CompleteAsync(
            session, registration.Definition, delivery.Claim,
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Result("done"), Json.Default.Result).ToImmutableArray(),
            completionIdentity, default);
        OperationResult<BaseActivationReceiptResolution> resolved = await worker.ResolveReceiptAsync(
            session, registration.Definition, registration.Identity.ResultBindings, completionIdentity, default);
        OperationResult<BaseActivationReceiptResolution> disclosureDenied = await worker.ResolveReceiptAsync(
            session, registration.Definition,
            [BaseModuleDtoPropertyBinding.Create<Result, string>(
                "test.result.value", "value", BaseGeneratedModuleScalarManifest.Primitive<string>(),
                BaseFieldConfidentiality.Confidential, BaseRecordDisclosure.Omit)],
            completionIdentity, default);
        BaseActivationTransitionResult resolvedTransition = System.Text.Json.JsonSerializer.Deserialize(
            resolved.Value!.CanonicalResult.AsSpan(), HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult)!;
        BaseActivationAdministrationPage administration = (await store.ReadAdministrationAsync(
            new BaseActivationAdministrationQueryRequest
            {
                ApplicationId = "activation-test",
                Scope = new BaseOwnedScopeSeekAuthority
                {
                    Kind = BaseSubjectScopeKind.Global,
                    ProtectedIndexDigest = SHA256.HashData(Encoding.UTF8.GetBytes(
                        $"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
                },
                Definition = new BaseActivationDefinitionKey
                {
                    Id = registration.Definition.Id, Version = registration.Definition.Version,
                    Checksum = registration.Definition.Checksum,
                },
                States = BaseActivationStateSelector.Terminal,
                Take = 8,
                AcceptedTime = new BaseActivationAcceptedTimeAuthority(TimeProvider.System).Capture("activation-test"),
                Limits = registration.Definition.Limits.Provider,
            })).Value!;

        created.IsSuccess().Should().BeTrue(created.Error?.Code);
        observed.IsSuccess().Should().BeTrue(observed.Error?.Code);
        claimed.IsSuccess().Should().BeTrue(claimed.Error?.Code);
        completed.IsSuccess().Should().BeTrue(completed.Error?.Code);
        completed.Value!.State.Should().Be(BaseActivationState.Succeeded);
        completedReplay.IsSuccess().Should().BeTrue(completedReplay.Error?.Code);
        completedReplay.Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        resolved.IsSuccess().Should().BeTrue(resolved.Error?.Code);
        resolved.Value!.OperationKind.Should().Be("activation-completed");
        disclosureDenied.Status.Should().Be(OperationStatus.PolicyDenied);
        resolvedTransition.CanonicalResult.Should().Equal(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Result("done"), Json.Default.Result));
        administration.Items.Should().ContainSingle(item =>
            item.ActivationId == created.Value!.ActivationId && item.ResultRetained);
        (await worker.ClaimAsync(session, registration.Definition, observed.Value.Token, claimIdentity, default)).Value
            .Should().BeOfType<BaseActivationClaimTerminalResult>();
    }

    [Fact]
    public async Task At_most_once_effect_is_durably_started_before_external_execution()
    {
        var store = new InMemoryRecordStore();
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "activation-store", Store = store });
        DefaultBasePolicyOrchestrator policy = Policy();
        BaseActivationHandlerRegistration<Input, Result> registration =
            Registration(BaseActivationExecutionClass.AtMostOnceEffect);
        var registry = new BaseActivationRegistry([new BaseActivationRegistration<Input, Result>(registration)]);
        var enqueue = new DefaultBaseActivationRuntime(stores, policy, TimeProvider.System);
        var worker = new DefaultBaseActivationWorkerRuntime(
            stores, policy, new BaseActivationAcceptedTimeAuthority(TimeProvider.System), registry,
            new BaseModuleMutationRegistry([], []));
        BaseSession session = Session();

        OperationResult<BaseActivationEnqueueResult> enqueued = await enqueue.EnqueueAsync(
            session, registration.Definition, registration.Identity,
            new Input("effect"), Identity("enqueue", "effect"), null, default);
        enqueued.IsSuccess().Should().BeTrue(enqueued.Error?.Code);
        BaseActivationDueObservation observed = (await worker.ObserveAsync(session, registration.Definition, default)).Value!;
        BaseActivationClaimedResult claimed = (await worker.ClaimAsync(session, registration.Definition,
            observed.Token, Identity("claim", "effect"), default)).Value.Should().BeOfType<BaseActivationClaimedResult>().Subject;

        OperationResult<BaseActivationTransitionResult> begun = await worker.BeginEffectAsync(
            session, registration.Definition, claimed.Claim, Identity("effect-start", "effect"), default);
        OperationResult<BaseActivationTransitionResult> replay = await worker.BeginEffectAsync(
            session, registration.Definition, claimed.Claim, Identity("effect-start", "effect"), default);

        begun.IsSuccess().Should().BeTrue(begun.Error?.Code);
        begun.Value!.State.Should().Be(BaseActivationState.EffectStarted);
        begun.Value.Effect.Should().NotBeNull();
        replay.IsSuccess().Should().BeTrue(replay.Error?.Code);
        replay.Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        replay.Value.Effect!.Checksum.Should().Equal(begun.Value.Effect!.Checksum);
    }

    [Theory]
    [InlineData("test.activation.observe")]
    [InlineData("test.activation.claim")]
    [InlineData("test.activation.execute")]
    public async Task Worker_operations_require_their_exact_installed_grants(string missingGrant)
    {
        var store = new InMemoryRecordStore();
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "activation-store", Store = store });
        DefaultBasePolicyOrchestrator policy = Policy(missingGrant);
        var enqueue = new DefaultBaseActivationRuntime(stores, policy, TimeProvider.System);
        var worker = new DefaultBaseActivationWorkerRuntime(
            stores, policy, new BaseActivationAcceptedTimeAuthority(TimeProvider.System));
        BaseActivationHandlerRegistration<Input, Result> registration = Registration();
        BaseSession session = Session();
        (await enqueue.EnqueueAsync(session, registration.Definition, registration.Identity,
            new Input("work"), Identity("enqueue", missingGrant), null, default)).IsSuccess().Should().BeTrue();

        OperationResult<BaseActivationDueObservation> observed = await worker.ObserveAsync(
            session, registration.Definition, default);
        if (missingGrant == registration.Definition.Grants.Observe)
        {
            observed.Status.Should().Be(OperationStatus.PolicyDenied);
            return;
        }

        observed.IsSuccess().Should().BeTrue(observed.Error?.Code);
        OperationResult<BaseActivationClaimResult> claimed = await worker.ClaimAsync(
            session, registration.Definition, observed.Value!.Token,
            Identity("claim", missingGrant), default);
        claimed.Status.Should().Be(OperationStatus.PolicyDenied);
        claimed.Error!.Code.Should().Be("base.activation.unauthorized");
    }

    [Fact]
    public async Task Schedule_create_and_advance_materializes_one_due_activation()
    {
        var store = new InMemoryRecordStore();
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "activation-store", Store = store });
        BaseActivationHandlerRegistration<Input, Result> target = Registration();
        long due = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        byte[] concurrencyKey = SHA256.HashData("test-overlap"u8);
        BaseScheduleDefinition schedule = Schedule(target, 1, due, BaseScheduleOverlapPolicy.Allow, concurrencyKey);
        var runtime = new DefaultBaseScheduleRuntime(stores, Policy(),
            new BaseActivationAcceptedTimeAuthority(TimeProvider.System),
            new BaseActivationRegistry([new BaseActivationRegistration<Input, Result>(target)]), new BaseTimeZoneRegistry(null));

        OperationResult<BaseScheduleMutationResult> created = await runtime.MutateAsync(
            Session(), schedule, BaseScheduleMutationKind.Create, null, Identity("schedule-create", "one"), default);
        OperationResult<BaseScheduleMutationResult> createReplay = await runtime.MutateAsync(
            Session(), schedule, BaseScheduleMutationKind.Create, null, Identity("schedule-create", "one"), default);
        OperationResult<BaseScheduleMaintenancePage> advanced = await runtime.AdvanceAsync(
            Session(), schedule, Identity("schedule-advance", "one"), default);

        created.IsSuccess().Should().BeTrue(created.Error?.Code);
        createReplay.IsSuccess().Should().BeTrue(createReplay.Error?.Code);
        createReplay.Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        OperationResult<BaseScheduleMutationResult> createCollision = await runtime.MutateAsync(
            Session(), schedule, BaseScheduleMutationKind.Create, null,
            Identity("schedule-create", "one") with
            { Fingerprint = BaseMutationRequestFingerprint.Create(Enumerable.Repeat((byte)9, 32).ToArray()) }, default);
        createCollision.IsSuccess().Should().BeFalse();
        createCollision.Error!.Code.Should().Be("base.activation.fingerprintConflict");
        advanced.IsSuccess().Should().BeTrue(advanced.Error?.Code);
        advanced.Value!.Occurrences.Should().ContainSingle();
        advanced.Value.Occurrences[0].Disposition.Should().BeOfType<BaseOccurrenceMaterialized>();

        BaseScheduleDefinition skippedSchedule = Schedule(
            target, 2, due + 1, BaseScheduleOverlapPolicy.SkipWhileActive, concurrencyKey);
        (await runtime.MutateAsync(Session(), skippedSchedule, BaseScheduleMutationKind.Create, null,
            Identity("schedule-create", "two"), default)).IsSuccess().Should().BeTrue();
        OperationResult<BaseScheduleMaintenancePage> skipped = await runtime.AdvanceAsync(
            Session(), skippedSchedule, Identity("schedule-advance", "two"), default);
        skipped.IsSuccess().Should().BeTrue(skipped.Error?.Code);
        skipped.Value!.Occurrences[0].Disposition.Should().BeOfType<BaseOccurrenceSkippedOverlap>();

        BaseScheduleDefinition replacementSchedule = Schedule(
            target, 3, due + 2, BaseScheduleOverlapPolicy.CancelPrevious, concurrencyKey);
        (await runtime.MutateAsync(Session(), replacementSchedule, BaseScheduleMutationKind.Create, null,
            Identity("schedule-create", "three"), default)).IsSuccess().Should().BeTrue();
        OperationResult<BaseScheduleMaintenancePage> replacement = await runtime.AdvanceAsync(
            Session(), replacementSchedule, Identity("schedule-advance", "three"), default);
        replacement.IsSuccess().Should().BeTrue(replacement.Error?.Code);
        replacement.Value!.Occurrences[0].Disposition.Should().BeOfType<BaseOccurrenceMaterialized>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Schedule_provider_input_tampering_is_rejected_before_materialization_and_quarantines_gate(
        bool tamperChecksum)
    {
        var retained = new InMemoryRecordStore();
        var hostile = new HostileScheduleProvider(retained);
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "activation-store", Store = hostile });
        BaseActivationHandlerRegistration<Input, Result> target = Registration();
        long due = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        BaseScheduleDefinition schedule = Schedule(
            target, 41, due, BaseScheduleOverlapPolicy.Allow, SHA256.HashData("hostile-schedule"u8));
        var gate = new BaseActivationProviderExecutionGate();
        var runtime = new DefaultBaseScheduleRuntime(stores, Policy(),
            new BaseActivationAcceptedTimeAuthority(TimeProvider.System),
            new BaseActivationRegistry([new BaseActivationRegistration<Input, Result>(target)]),
            new BaseTimeZoneRegistry(null), gate);

        (await runtime.MutateAsync(Session(), schedule, BaseScheduleMutationKind.Create, null,
            Identity("hostile-schedule-create", tamperChecksum.ToString()), default)).IsSuccess().Should().BeTrue();
        hostile.TamperChecksum = tamperChecksum;
        hostile.TamperScheduleInput = true;

        OperationResult<BaseScheduleMaintenancePage> rejected = await runtime.AdvanceAsync(
            Session(), schedule, Identity("hostile-schedule-advance", tamperChecksum.ToString()), default);

        rejected.IsSuccess().Should().BeFalse();
        rejected.Error!.Code.Should().Be("base.activation.providerContractInvalid");
        hostile.AdvanceCalls.Should().Be(0);
        gate.IsQuarantined.Should().BeTrue();

        OperationResult<BaseScheduleAuthority> quarantined = await runtime.ReadAsync(Session(), schedule, default);
        quarantined.IsSuccess().Should().BeFalse();
        quarantined.Error!.Code.Should().Be("base.activation.quarantined");
    }

    private static BaseActivationHandlerRegistration<Input, Result> Registration(
        BaseActivationExecutionClass executionClass = BaseActivationExecutionClass.AtLeastOnceWorker) =>
        BaseActivationDefinitionBuilder.CreateGenerated(
            DefinitionDraft("test.activation", executionClass),
            RuntimeActivationDtos.HPDBaseActivationDtoAuthority,
            static _ => new Handler());

    private static BaseActivationDefinitionDraft DefinitionDraft(
        string id, BaseActivationExecutionClass executionClass) => new()
        {
            Id = id, Version = 1, OwningModuleId = "test.module", ExecutionClass = executionClass,
            Grants = Grants(),
            SourceGrantIds = [],
            Retry = new BaseActivationRetryProfile
            {
                MaximumAttempts = 3, InitialDelayMilliseconds = 100, MaximumDelayMilliseconds = 1_000,
                MultiplierNumerator = 2, MultiplierDenominator = 1, JitterBasisPoints = 0,
                RetryableFailureCodes = ["test.retry"],
            },
            ReceiptRetention = new BaseActivationReceiptRetentionPolicy
            {
                FormatVersion = 1, DuplicateResolutionLifetime = TimeSpan.FromHours(24),
                ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
            },
            Limits = new BaseActivationLimits
            {
                MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 3, MaximumYields = 0,
                MaximumRenewalsPerSlice = 8, MaximumChildrenPerSlice = 8, MaximumLineageDepth = 8,
                LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromMinutes(5),
                Provider = ProviderLimits(), AtomicCreation = AtomicLimits(),
            },
            Handler = new BaseActivationHandlerDraft
            {
                Id = "test.handler", Version = 1, FactoryId = "test.handler.factory",
                WorkerSubjectKind = AccessSubjectKind.System,
                SemanticAuthority = BaseActivationHandlerSemanticAuthority.Create("test.handler.semantics", 1),
            },
        };

    private static BaseScheduleDefinition Schedule(
        BaseActivationHandlerRegistration<Input, Result> target, int version, long due,
        BaseScheduleOverlapPolicy overlapPolicy, byte[] concurrencyKey) =>
        BaseScheduleDefinitionBuilder.CreateGenerated(new BaseScheduleDefinitionDraft
        {
            Id = "test.schedule", Version = version, OwningModuleId = "test.module",
            ManageGrantId = "test.schedule.manage", MaterializeGrantId = "test.schedule.materialize",
            Expression = new BaseOnceSchedule(due), GapPolicy = BaseTimeGapPolicy.Skip,
            TimeOverlapPolicy = BaseTimeOverlapPolicy.EarlierOffset,
            MisfirePolicy = BaseScheduleMisfirePolicy.RunAll, ActivationOverlapPolicy = overlapPolicy,
            OverlapKeyKind = BaseScheduleOverlapKeyKind.CanonicalConcurrencyKey,
            ConcurrencyKey = concurrencyKey.ToImmutableArray(), Priority = 0, MaximumSplayMilliseconds = 0,
        }, target, RuntimeActivationDtos.HPDBaseActivationDtoAuthority, new Input("scheduled")).Definition;

    private static IReadOnlyList<BaseModuleDtoPropertyBinding> InputBindings() =>
        [BaseModuleDtoPropertyBinding.Create<Input, string>("test.input.value", "value", BaseGeneratedModuleScalarManifest.Primitive<string>())];

    private static IReadOnlyList<BaseModuleDtoPropertyBinding> ResultBindings() =>
        [BaseModuleDtoPropertyBinding.Create<Result, string>("test.result.value", "value", BaseGeneratedModuleScalarManifest.Primitive<string>())];

    private static BaseSession Session() => new(null!, TimeProvider.System,
        new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "system",
        },
        new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
        applicationId: "activation-test");

    private static BaseActivationGrantSet Grants() => new()
    {
        Enqueue = "test.activation.enqueue", Observe = "test.activation.observe",
        Claim = "test.activation.claim", Execute = "test.activation.execute",
        Renew = "test.activation.renew", Complete = "test.activation.complete",
        Fail = "test.activation.fail", Yield = "test.activation.yield", Cancel = "test.activation.cancel",
        Inspect = "test.activation.inspect", Replay = "test.activation.replay",
        Migrate = "test.activation.migrate", Reconcile = "test.activation.reconcile",
        Retry = "test.activation.retry",
        Dispose = "test.activation.dispose", Remove = "test.activation.remove",
        Repair = "test.activation.repair",
    };

    private static DefaultBasePolicyOrchestrator Policy(string? excludedGrant = null)
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "activation.policy", Version = 1, OwningModuleId = "test.module",
            EvaluatorContractId = "activation.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicy());
        foreach (string grant in ActivationGrantIds())
            if (!string.Equals(grant, excludedGrant, StringComparison.Ordinal))
                AddGrant(builder, grant, "test.activation");
        AddGrant(builder, "test.schedule.manage", "test.schedule");
        AddGrant(builder, "test.schedule.materialize", "test.schedule");
        return new DefaultBasePolicyOrchestrator(builder.Freeze("activation-test"));
    }

    private static IEnumerable<string> ActivationGrantIds()
    {
        BaseActivationGrantSet grants = Grants();
        yield return grants.Enqueue; yield return grants.Observe; yield return grants.Claim;
        yield return grants.Execute; yield return grants.Renew; yield return grants.Complete;
        yield return grants.Fail; yield return grants.Cancel; yield return grants.Inspect;
        yield return grants.Replay; yield return grants.Migrate; yield return grants.Reconcile; yield return grants.Retry;
        yield return grants.Dispose; yield return grants.Remove; yield return grants.Repair;
    }

    private static void AddGrant(BasePolicyAuthorityBuilder builder, string id, string action) =>
        builder.AddStaticGrant(new BaseGrantAuthorityDefinition
        {
            Id = id, Version = 1, OwningModuleId = "test.module",
            SourceContractId = "activation.grants", SourceContractVersion = 1,
        }, new AccessGrant
        {
            Id = id, ApplicationId = "activation-test", ModuleId = "test.module",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
            Action = action, Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        });

    private static BaseMutationRequestIdentity Identity(string operation, string key) =>
        BaseMutationRequestIdentity.Create("activation-test", operation, key, BaseMutationRequestFingerprint.Create(new byte[32]));

    private static BaseActivationExecutionLimits ProviderLimits() => new()
    {
        MaximumCandidates = 8, MaximumInputBytes = 4096, MaximumResultBytes = 4096,
        MaximumEvidenceBytes = 8192, MaximumTransientBytes = 16384,
        MaximumReadIntervals = 8, MaximumIndexOperations = 16,
        AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
    };

    private static BaseAtomicMutationExecutionLimits AtomicLimits() =>
        DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits);

    public sealed record Input
    {
        [BaseField("test.input.value", MaximumUtf8Bytes = 256), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
        public required string Value { get; init; }
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public Input(string value) => Value = value;
    }
    public sealed record Result
    {
        [BaseField("test.result.value", MaximumUtf8Bytes = 256), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
        public required string Value { get; init; }
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public Result(string value) => Value = value;
    }

    private sealed class Handler : IBaseActivationHandler<Input, Result>
    {
        public ValueTask<BaseActivationHandlerResult<Result>> ExecuteAsync(
            BaseActivationContext context, Input input, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BaseActivationHandlerResult<Result>>(new BaseActivationSucceeded<Result> { Result = new Result(input.Value) });
    }

    private sealed class AllowPolicy : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
    }

    private sealed class HostileScheduleProvider(InMemoryRecordStore inner) : IRecordStore, IBaseActivationProvider
    {
        public bool TamperScheduleInput { get; set; }
        public bool TamperChecksum { get; set; }
        public int AdvanceCalls { get; private set; }
        public StoreCapabilityDescriptor Capabilities => inner.Capabilities;
        public BaseActivationProviderDescriptor Descriptor => ((IBaseActivationProvider)inner).Descriptor;
        public ValueTask<OperationResult<BaseActivationYieldReservationState>> ReadYieldReservationStateAsync(
            CancellationToken cancellationToken = default) => inner.ReadYieldReservationStateAsync(cancellationToken);
        public ValueTask<OperationResult<BaseActivationReceiptCompactionAuthority>> CaptureReceiptCompactionAuthorityAsync(
            BaseActivationReceiptCompactionAuthorityRequest request, CancellationToken cancellationToken = default) =>
            inner.CaptureReceiptCompactionAuthorityAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationReceiptCompactionResult>> CompactActivationReceiptsAsync(
            BaseActivationReceiptCompactionRequest request, CancellationToken cancellationToken = default) =>
            inner.CompactActivationReceiptsAsync(request, cancellationToken);

        public ValueTask<OperationResult<RecordPage>> ListAsync(CollectionDefinition collection, RecordQuery query,
            OperationContext context, CancellationToken cancellationToken = default) =>
            inner.ListAsync(collection, query, context, cancellationToken);
        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(CollectionDefinition collection, RecordId id,
            OperationContext context, CancellationToken cancellationToken = default) =>
            inner.GetAsync(collection, id, context, cancellationToken);
        public ValueTask<OperationResult<BaseActivationDependencyResult>> ReadDependenciesAsync(BaseActivationDependencyRequest request,
            CancellationToken cancellationToken = default) => inner.ReadDependenciesAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(BaseActivationDueObservationRequest request,
            CancellationToken cancellationToken = default) => inner.ObserveDueAsync(request, cancellationToken);
        public ValueTask<BaseDueWaitResult> WaitForDueChangeAsync(BaseDueObservationToken token, DateTimeOffset deadline,
            CancellationToken cancellationToken = default) => inner.WaitForDueChangeAsync(token, deadline, cancellationToken);
        public ValueTask<OperationResult<BaseActivationClaimResult>> TryClaimNextAsync(BaseActivationClaimRequest request,
            CancellationToken cancellationToken = default) => inner.TryClaimNextAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseTransactionalActivationCandidate>> ReadTransactionalCandidateAsync(
            BaseTransactionalActivationCandidateRequest request, CancellationToken cancellationToken = default) =>
            inner.ReadTransactionalCandidateAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(BaseActivationRenewRequest request,
            CancellationToken cancellationToken = default) => inner.RenewAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseExecutorRegistrationResult>> RegisterExecutorAsync(BaseExecutorRegistrationRequest request,
            CancellationToken cancellationToken = default) => inner.RegisterExecutorAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(BaseExecutorHeartbeatRequest request,
            CancellationToken cancellationToken = default) => inner.HeartbeatExecutorAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(BaseExecutorRetirementRequest request,
            CancellationToken cancellationToken = default) => inner.RetireExecutorAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(BaseActivationTransitionRequest request,
            CancellationToken cancellationToken = default) => inner.TransitionAsync(request, cancellationToken);

        public async ValueTask<OperationResult<BaseScheduleAuthority>> ReadScheduleAsync(string scheduleId, int scheduleVersion,
            CancellationToken cancellationToken = default)
        {
            OperationResult<BaseScheduleAuthority> result = await inner.ReadScheduleAsync(scheduleId, scheduleVersion, cancellationToken);
            if (!TamperScheduleInput || !result.IsSuccess() || result.Value is null) return result;
            BaseScheduleDefinition definition = result.Value.Definition;
            definition = TamperChecksum
                ? definition with { InputChecksum = Enumerable.Repeat((byte)0xA5, 32).ToImmutableArray() }
                : definition with { CanonicalInput = "{\"value\":\"tampered\"}"u8.ToArray().ToImmutableArray() };
            return result with { Value = result.Value with { Definition = definition } };
        }

        public ValueTask<OperationResult<BaseScheduleMutationResult>> MutateScheduleAsync(BaseScheduleMutationRequest request,
            CancellationToken cancellationToken = default) => inner.MutateScheduleAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceSchedulesAsync(BaseScheduleMaintenanceRequest request,
            CancellationToken cancellationToken = default)
        {
            AdvanceCalls++;
            return inner.AdvanceSchedulesAsync(request, cancellationToken);
        }
        public ValueTask<OperationResult<BaseScheduleCancellationMaintenancePage>> AdvanceScheduleCancellationAsync(
            BaseScheduleCancellationMaintenanceRequest request, CancellationToken cancellationToken = default) =>
            inner.AdvanceScheduleCancellationAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationAdministrationPage>> ReadAdministrationAsync(
            BaseActivationAdministrationQueryRequest request, CancellationToken cancellationToken = default) =>
            inner.ReadAdministrationAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationMigrationCandidate>> ReadMigrationCandidateAsync(
            BaseActivationMigrationCandidateRequest request, CancellationToken cancellationToken = default) =>
            inner.ReadMigrationCandidateAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationMigrationResult>> MigrateAsync(BaseActivationMigrationRequest request,
            CancellationToken cancellationToken = default) => inner.MigrateAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationReceiptResolution>> ResolveReceiptAsync(
            BaseActivationReceiptResolutionRequest request, CancellationToken cancellationToken = default) =>
            inner.ResolveReceiptAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationMaintenancePage>> AdvanceMaintenanceAsync(
            BaseActivationMaintenanceRequest request, CancellationToken cancellationToken = default) =>
            inner.AdvanceMaintenanceAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationPrunePage>> PruneAsync(BaseActivationPruneRequest request,
            CancellationToken cancellationToken = default) => inner.PruneAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationIndeterminateResolution>> ResolveIndeterminateAsync(
            BaseActivationIndeterminateRequest request, CancellationToken cancellationToken = default) =>
            inner.ResolveIndeterminateAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationQuarantinePage>> ReadQuarantineAsync(BaseActivationQuarantineRequest request,
            CancellationToken cancellationToken = default) => inner.ReadQuarantineAsync(request, cancellationToken);
    }

    [JsonSerializable(typeof(Input))]
    [JsonSerializable(typeof(Result))]
    internal sealed partial class Json : JsonSerializerContext;
}

[BaseActivationDtoAuthority("test.activation.dto", 1, "test.module", "test.input", "test.result",
    typeof(ActivationRuntimeTests.Json), typeof(ActivationRuntimeTests.Input), typeof(ActivationRuntimeTests.Result))]
internal static partial class RuntimeActivationDtos;
