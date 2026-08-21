using System.Collections.Immutable;

namespace HPD.Base.Tests.InMemory.Activations;

public sealed class ExecutorRegistryTests
{
    [Fact]
    public async Task Registration_heartbeat_and_retirement_preserve_stable_incarnation_authority()
    {
        var store = new InMemoryRecordStore();
        var clock = new BaseActivationAcceptedTimeAuthority(TimeProvider.System);
        BaseActivationExecutionLimits limits = Limits();
        var registrationRequest = new BaseExecutorRegistrationRequest
        {
            ApplicationId = "executor-test", HostId = "host-one", ProcessIncarnationId = "process-one",
            WorkerDefinitionSetChecksum = new byte[32].ToImmutableArray(), RequestedHeartbeatMilliseconds = 60_000,
            AcceptedTime = clock.Capture("executor-test"), Identity = Identity("register"), Limits = limits,
        };
        OperationResult<BaseExecutorRegistrationResult> registered = await store.RegisterExecutorAsync(registrationRequest);
        registered.IsSuccess().Should().BeTrue(registered.Error?.Code);
        (await store.RegisterExecutorAsync(registrationRequest with
        { AcceptedTime = clock.Capture("executor-test") })).Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);

        var heartbeatRequest = new BaseExecutorHeartbeatRequest
        {
            Executor = registered.Value!.Executor, ExpectedHeartbeatRevision = registered.Value.Heartbeat.HeartbeatRevision,
            ExtensionMilliseconds = 60_000, AcceptedTime = clock.Capture("executor-test"),
            Identity = Identity("heartbeat"), Limits = limits,
        };
        OperationResult<BaseExecutorHeartbeatResult> heartbeat = await store.HeartbeatExecutorAsync(heartbeatRequest);
        heartbeat.IsSuccess().Should().BeTrue(heartbeat.Error?.Code);
        heartbeat.Value!.Executor.Should().BeEquivalentTo(registered.Value.Executor);
        heartbeat.Value.Heartbeat.HeartbeatRevision.Should().Be(2);
        (await store.HeartbeatExecutorAsync(heartbeatRequest with
        { AcceptedTime = clock.Capture("executor-test") })).Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);

        var retirementRequest = new BaseExecutorRetirementRequest
        {
            Executor = registered.Value.Executor, ExpectedHeartbeatRevision = heartbeat.Value.Heartbeat.HeartbeatRevision,
            AcceptedTime = clock.Capture("executor-test"), Identity = Identity("retire"), Limits = limits,
        };
        OperationResult<BaseExecutorRetirementResult> retired = await store.RetireExecutorAsync(retirementRequest);
        retired.IsSuccess().Should().BeTrue(retired.Error?.Code);
        (await store.RetireExecutorAsync(retirementRequest with
        { AcceptedTime = clock.Capture("executor-test") })).Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        (await store.HeartbeatExecutorAsync(new BaseExecutorHeartbeatRequest
        {
            Executor = registered.Value.Executor, ExpectedHeartbeatRevision = heartbeat.Value.Heartbeat.HeartbeatRevision,
            ExtensionMilliseconds = 60_000, AcceptedTime = clock.Capture("executor-test"),
            Identity = Identity("late"), Limits = limits,
        })).Status.Should().Be(OperationStatus.Conflict);
    }

    private static BaseMutationRequestIdentity Identity(string operation) =>
        BaseMutationRequestIdentity.Create("executor-test", operation, "one", BaseMutationRequestFingerprint.Create(new byte[32]));

    private static BaseActivationExecutionLimits Limits() => new()
    {
        MaximumCandidates = 8, MaximumInputBytes = 4096, MaximumResultBytes = 4096,
        MaximumEvidenceBytes = 8192, MaximumTransientBytes = 16384, MaximumReadIntervals = 8,
        MaximumIndexOperations = 16, AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5), CommitObservationTimeout = TimeSpan.FromSeconds(5),
        ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
    };
}
