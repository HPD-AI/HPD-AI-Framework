using HPD.Agent.Evaluations.Tracing;

namespace HPD.Agent.Evaluations.Tests.Tracing;

public sealed class TurnEventBufferOperationTests
{
    [Fact]
    public void ProjectsEffectiveCapabilityIdentityAndUnifiedOperationTimingFacts()
    {
        var buffer = new TurnEventBuffer();
        var identity = new AgentTurnCapabilityIdentity
        {
            AgentEpoch = 4,
            OverlayRevision = "overlay",
            EffectiveSnapshotId = "snapshot",
            SourceRevisions = [new AgentCapabilitySourceRevision("mcp:docs", 7)]
        };
        var registered = DateTimeOffset.Parse("2026-08-26T10:00:00Z");
        var operation = Snapshot(registered, AgentOperationProviderStatus.InputRequired, version: 1);

        buffer.RecordCapabilities(identity);
        buffer.RecordOperation(operation);
        buffer.RecordOperation(Snapshot(registered, AgentOperationProviderStatus.Completed, version: 2));

        Assert.Same(identity, buffer.CapabilityIdentity);
        var trace = Assert.Single(buffer.GetOperationTraces());
        Assert.Equal("operation-1", trace.OperationId);
        Assert.Equal("provider-1", trace.ProviderOperationId);
        Assert.Equal(TimeSpan.FromSeconds(1), trace.AcceptedToStartLatency);
        Assert.Equal(TimeSpan.FromSeconds(4), trace.ProviderExecutionLatency);
        Assert.Equal(TimeSpan.FromSeconds(5), trace.ObservationLatency);
        Assert.Equal(1, trace.InputRoundCount);
        Assert.True(trace.IsTerminal);
    }

    private static AgentOperationSnapshot Snapshot(
        DateTimeOffset registered,
        AgentOperationProviderStatus status,
        long version) => new()
    {
        OperationId = "operation-1",
        ProviderOperationId = "provider-1",
        SourceKind = AgentOperationSourceKind.McpTask,
        Name = "remote",
        Address = new AgentExecutionAddress("agent", "session", "thread"),
        ProviderStatus = status,
        ObservationStatus = AgentOperationObservationStatus.Attached,
        Control = new AgentOperationControl("provider-1", AgentOperationKind.Provider,
            AgentOperationCapabilities.Reconcile),
        Notification = new AgentOperationNotificationPolicy(),
        RegisteredAt = registered,
        StartedAt = registered.AddSeconds(1),
        UpdatedAt = registered.AddSeconds(5),
        FinishedAt = status == AgentOperationProviderStatus.Completed
            ? registered.AddSeconds(5)
            : null,
        Completion = status == AgentOperationProviderStatus.Completed
            ? new AgentOperationCompletion("done")
            : null,
        Version = version
    };
}
