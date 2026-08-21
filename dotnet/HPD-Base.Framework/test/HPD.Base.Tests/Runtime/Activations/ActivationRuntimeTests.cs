using System.Collections.Immutable;
using System.Security.Cryptography;
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
            ActivationId = "activation", AttemptNumber = 1, ClaimEpoch = 1,
            FencingToken = new byte[32].ToImmutableArray(), WorkerIdentity = "worker",
            CancellationGeneration = 0, StoreInstanceId = "store", RestoreEpoch = 1,
            DefinitionChecksum = new byte[32].ToImmutableArray(),
        };
        var initial = new BaseActivationLeaseObservation
        { Revision = 1, ExpiresAt = 100, Checksum = new byte[32].ToImmutableArray() };
        var replacement = initial with { Revision = 2, ExpiresAt = 200 };
        var context = new BaseActivationContext(
            new BaseActivationDefinitionKey { Id = "definition", Version = 1, Checksum = new byte[32].ToImmutableArray() },
            claim, initial, null, 0, 0, 1,
            (_, _) => ValueTask.FromResult(OperationResults.Ok(new BaseActivationRenewResult
            {
                Claim = claim, Lease = replacement, Accounting = new BaseActivationAccounting
                {
                    Candidates = 1, Comparisons = 1, IndexOperations = 1, ReadIntervals = 0,
                    EvidenceBytes = 1, TransientBytes = 1,
                },
                Disposition = BaseMutationRequestDisposition.Committed,
            })),
            CancellationToken.None, 1);

        OperationResult<BaseActivationLeaseObservation> renewed = await context.RenewAsync();
        OperationResult<BaseActivationLeaseObservation> exceeded = await context.RenewAsync();

        renewed.Value!.Revision.Should().Be(2);
        context.Lease.Revision.Should().Be(2);
        exceeded.Error!.Code.Should().Be("base.activation.budgetExceeded");
    }

    [Fact]
    public void Transactional_activation_is_handler_free_and_target_bound()
    {
        BaseActivationDefinition worker = Registration().Definition;
        BaseTransactionalActivationRegistration<Input, Result> registration = BaseActivationDefinitionBuilder.CreateTransactional(
            worker with
            {
                Id = "test.activation.transactional",
                ExecutionClass = BaseActivationExecutionClass.TransactionalOperation,
                Handler = null,
                TransactionalTarget = new BaseModuleMutationActivationTarget
                {
                    OperationId = "test.module.operation",
                    OperationVersion = 1,
                    OperationChecksum = new string('a', 64),
                },
                Checksum = [],
            }, Json.Default.Input, Json.Default.Result);

        registration.Definition.Handler.Should().BeNull();
        registration.Definition.TransactionalTarget.Should().BeOfType<BaseModuleMutationActivationTarget>();
        registration.Definition.Checksum.Should().HaveCount(32);
        Action invalid = () => BaseActivationDefinitionBuilder.CreateTransactional(
            worker with { ExecutionClass = BaseActivationExecutionClass.TransactionalOperation, TransactionalTarget = null, Handler = null, Checksum = [] },
            Json.Default.Input, Json.Default.Result);
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
        OperationResult<BaseActivationEnqueueResult> duplicate = await runtime.EnqueueAsync(
            session, registration.Definition, registration.Identity, new Input("work"), identity, null, default);

        first.IsSuccess().Should().BeTrue(first.Error?.Code);
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
            session, registration.Definition, completionIdentity, default);
        BaseActivationTransitionResult resolvedTransition = System.Text.Json.JsonSerializer.Deserialize(
            resolved.Value!.CanonicalResult.AsSpan(), HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult)!;

        created.IsSuccess().Should().BeTrue(created.Error?.Code);
        observed.IsSuccess().Should().BeTrue(observed.Error?.Code);
        claimed.IsSuccess().Should().BeTrue(claimed.Error?.Code);
        completed.IsSuccess().Should().BeTrue(completed.Error?.Code);
        completed.Value!.State.Should().Be(BaseActivationState.Succeeded);
        completedReplay.IsSuccess().Should().BeTrue(completedReplay.Error?.Code);
        completedReplay.Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        resolved.IsSuccess().Should().BeTrue(resolved.Error?.Code);
        resolved.Value!.OperationKind.Should().Be("activation-completed");
        resolvedTransition.CanonicalResult.Should().Equal(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Result("done"), Json.Default.Result));
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
        BaseActivationHandlerRegistration<Input, Result> ordinary = Registration();
        BaseActivationHandlerRegistration<Input, Result> registration = BaseActivationDefinitionBuilder.Create(
            ordinary.Definition with
            {
                ExecutionClass = BaseActivationExecutionClass.AtMostOnceEffect,
                Checksum = [],
            }, Json.Default.Input, Json.Default.Result, static _ => new Handler());
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
        BaseScheduleDefinition schedule = BaseScheduleDefinitionBuilder.Create(new BaseScheduleDefinition
        {
            Id = "test.schedule", Version = 1, OwningModuleId = "test.module",
            ManageGrantId = "test.schedule.manage", MaterializeGrantId = "test.schedule.materialize",
            Activation = new BaseActivationDefinitionKey { Id = target.Definition.Id, Version = target.Definition.Version, Checksum = target.Definition.Checksum },
            CanonicalInput = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Input("scheduled"), Json.Default.Input).ToImmutableArray(),
            InputChecksum = [], Expression = new BaseOnceSchedule(due),
            GapPolicy = BaseTimeGapPolicy.Skip, TimeOverlapPolicy = BaseTimeOverlapPolicy.EarlierOffset,
            MisfirePolicy = BaseScheduleMisfirePolicy.RunAll, ActivationOverlapPolicy = BaseScheduleOverlapPolicy.Allow,
            OverlapKeyKind = BaseScheduleOverlapKeyKind.CanonicalConcurrencyKey, ConcurrencyKey = concurrencyKey.ToImmutableArray(),
            Priority = 0, MaximumSplayMilliseconds = 0, Checksum = [],
        } with
        {
            InputChecksum = SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Input("scheduled"), Json.Default.Input)).ToImmutableArray(),
        });
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

        BaseScheduleDefinition skippedSchedule = BaseScheduleDefinitionBuilder.Create(schedule with
        {
            Version = 2, Expression = new BaseOnceSchedule(due + 1),
            ActivationOverlapPolicy = BaseScheduleOverlapPolicy.SkipWhileActive, Checksum = [],
        });
        (await runtime.MutateAsync(Session(), skippedSchedule, BaseScheduleMutationKind.Create, null,
            Identity("schedule-create", "two"), default)).IsSuccess().Should().BeTrue();
        OperationResult<BaseScheduleMaintenancePage> skipped = await runtime.AdvanceAsync(
            Session(), skippedSchedule, Identity("schedule-advance", "two"), default);
        skipped.IsSuccess().Should().BeTrue(skipped.Error?.Code);
        skipped.Value!.Occurrences[0].Disposition.Should().BeOfType<BaseOccurrenceSkippedOverlap>();

        BaseScheduleDefinition replacementSchedule = BaseScheduleDefinitionBuilder.Create(schedule with
        {
            Version = 3, Expression = new BaseOnceSchedule(due + 2),
            ActivationOverlapPolicy = BaseScheduleOverlapPolicy.CancelPrevious, Checksum = [],
        });
        (await runtime.MutateAsync(Session(), replacementSchedule, BaseScheduleMutationKind.Create, null,
            Identity("schedule-create", "three"), default)).IsSuccess().Should().BeTrue();
        OperationResult<BaseScheduleMaintenancePage> replacement = await runtime.AdvanceAsync(
            Session(), replacementSchedule, Identity("schedule-advance", "three"), default);
        replacement.IsSuccess().Should().BeTrue(replacement.Error?.Code);
        replacement.Value!.Occurrences[0].Disposition.Should().BeOfType<BaseOccurrenceMaterialized>();
    }

    private static BaseActivationHandlerRegistration<Input, Result> Registration() =>
        BaseActivationDefinitionBuilder.Create(new BaseActivationDefinition
        {
            Id = "test.activation", Version = 1, OwningModuleId = "test.module",
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
            InputTypeId = "test.input", ResultTypeId = "test.result",
            Grants = Grants(),
            SourceGrantIds = [],
            Retry = new BaseActivationRetryProfile
            {
                MaximumAttempts = 3, InitialDelayMilliseconds = 100, MaximumDelayMilliseconds = 1_000,
                MultiplierNumerator = 2, MultiplierDenominator = 1, JitterBasisPoints = 0,
                RetryableFailureCodes = ["test.retry"],
            },
            Limits = new BaseActivationLimits
            {
                MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 3,
                MaximumRenewalsPerAttempt = 8, MaximumChildrenPerAttempt = 8, MaximumLineageDepth = 8,
                LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromMinutes(5),
                Provider = ProviderLimits(), AtomicCreation = AtomicLimits(),
            },
            Handler = new BaseActivationHandlerBinding
            {
                Id = "test.handler", Version = 1, FactoryId = "test.handler.factory",
                InputTypeId = "test.input", ResultTypeId = "test.result", WorkerSubjectKind = AccessSubjectKind.System,
                Checksum = new byte[32].ToImmutableArray(),
            },
            Checksum = [],
        }, Json.Default.Input, Json.Default.Result, static _ => new Handler());

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
        Fail = "test.activation.fail", Cancel = "test.activation.cancel",
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

    public sealed record Input(string Value);
    public sealed record Result(string Value);

    private sealed class Handler : IBaseActivationHandler<Input, Result>
    {
        public ValueTask<BaseActivationHandlerResult<Result>> ExecuteAsync(
            BaseActivationContext context, Input input, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BaseActivationHandlerResult<Result> { Result = new Result(input.Value) });
    }

    private sealed class AllowPolicy : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
    }

    [JsonSerializable(typeof(Input))]
    [JsonSerializable(typeof(Result))]
    internal sealed partial class Json : JsonSerializerContext;
}
