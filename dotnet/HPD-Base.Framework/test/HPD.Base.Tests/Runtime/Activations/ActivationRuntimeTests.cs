using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace HPD.Base.Tests.Runtime.Activations;

public sealed partial class ActivationRuntimeTests
{
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
        OperationResult<BaseActivationClaimResult> claimed = await worker.ClaimAsync(
            session, registration.Definition, observed.Value!.Token, Identity("claim", "one"), default);
        var delivery = claimed.Value.Should().BeOfType<BaseActivationClaimedResult>().Subject;
        OperationResult<BaseActivationTransitionResult> completed = await worker.CompleteAsync(
            session, registration.Definition, delivery.Claim,
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Result("done"), Json.Default.Result).ToImmutableArray(),
            Identity("complete", "one"), default);

        created.IsSuccess().Should().BeTrue(created.Error?.Code);
        observed.IsSuccess().Should().BeTrue(observed.Error?.Code);
        claimed.IsSuccess().Should().BeTrue(claimed.Error?.Code);
        completed.IsSuccess().Should().BeTrue(completed.Error?.Code);
        completed.Value!.State.Should().Be(BaseActivationState.Succeeded);
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
        OperationResult<BaseScheduleMaintenancePage> advanced = await runtime.AdvanceAsync(
            Session(), schedule, Identity("schedule-advance", "one"), default);

        created.IsSuccess().Should().BeTrue(created.Error?.Code);
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
            EnqueueGrantId = "test.activation.enqueue", ExecuteGrantId = "test.activation.execute",
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

    private static DefaultBasePolicyOrchestrator Policy()
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "activation.policy", Version = 1, OwningModuleId = "test.module",
            EvaluatorContractId = "activation.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicy());
        builder.AddStaticGrant(new BaseGrantAuthorityDefinition
        {
            Id = "test.activation.enqueue", Version = 1, OwningModuleId = "test.module",
            SourceContractId = "activation.grants", SourceContractVersion = 1,
        }, new AccessGrant
        {
            Id = "test.activation.enqueue", ApplicationId = "activation-test", ModuleId = "test.module",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
            Action = "test.activation", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        });
        builder.AddStaticGrant(new BaseGrantAuthorityDefinition
        {
            Id = "test.activation.execute", Version = 1, OwningModuleId = "test.module",
            SourceContractId = "activation.grants", SourceContractVersion = 1,
        }, new AccessGrant
        {
            Id = "test.activation.execute", ApplicationId = "activation-test", ModuleId = "test.module",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
            Action = "test.activation", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        });
        AddGrant(builder, "test.schedule.manage", "test.schedule");
        AddGrant(builder, "test.schedule.materialize", "test.schedule");
        return new DefaultBasePolicyOrchestrator(builder.Freeze("activation-test"));
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
