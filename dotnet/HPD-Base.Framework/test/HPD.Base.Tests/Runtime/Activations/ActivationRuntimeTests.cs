using System.Collections.Immutable;
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
        return new DefaultBasePolicyOrchestrator(builder.Freeze("activation-test"));
    }

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
