using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Events.Core;

namespace HPD.Agent.Tests;

public sealed class PermissionCheckpointOwnershipTests
{
    [Fact]
    public async Task NoApprovalChangeRequiresNoSessionOrSerialization()
    {
        var context = Context();
        await PermissionStateCheckpoint.SaveAsync(context,
            new Dictionary<string, MiddlewareStateFactory>(), CancellationToken.None);
        Assert.False(PermissionStateCheckpoint.IsPending(context));
    }

    [Fact]
    public async Task PendingApprovalCannotBeSkippedWhenStoreIsUnavailable()
    {
        var context = Context(new HPD.Agent.Session("checkpoint-test"));
        PermissionStateCheckpoint.MarkPending(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PermissionStateCheckpoint.SaveAsync(context,
                new Dictionary<string, MiddlewareStateFactory>(), CancellationToken.None));
        Assert.True(PermissionStateCheckpoint.IsPending(context));
    }

    [Fact]
    public void PendingMarkerBelongsToItsExecution()
    {
        var first = Context();
        var second = Context();
        PermissionStateCheckpoint.MarkPending(first);
        Assert.True(PermissionStateCheckpoint.IsPending(first));
        Assert.False(PermissionStateCheckpoint.IsPending(second));
    }

    private static AgentContext Context(HPD.Agent.Session? session = null) => new(
        agentName: "test",
        conversationId: "test",
        initialState: AgentLoopState.Initial([], Guid.NewGuid().ToString(), "test", "test"),
        eventCoordinator: new EventCoordinator(),
        session: session,
        thread: null,
        cancellationToken: CancellationToken.None);
}
