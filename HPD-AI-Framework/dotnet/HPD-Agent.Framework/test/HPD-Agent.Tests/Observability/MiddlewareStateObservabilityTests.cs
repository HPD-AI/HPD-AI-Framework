using System.Text.Json;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Observability;

public class MiddlewareStateObservabilityTests
{
    [Fact]
    public void BuildMiddlewareStateSnapshotEvent_UsesFactoryMetadataAndSerializedJson()
    {
        var state = new MiddlewareState()
            .WithErrorTracking(new ErrorTrackingStateData { ConsecutiveFailures = 2 });
        var factories = CreateFactories();

        var evt = Agent.BuildMiddlewareStateSnapshotEvent(
            agentName: "TestAgent",
            stateFactories: factories,
            state: state,
            sessionId: "session-1",
            threadId: "main",
            iteration: 3,
            phase: "before_model_call",
            batchId: null,
            functionCallId: null,
            toolCallIndex: null);

        Assert.Equal("TestAgent", evt.AgentName);
        Assert.Equal("session-1", evt.SessionId);
        Assert.Equal("main", evt.ThreadId);
        Assert.Equal(3, evt.Iteration);
        Assert.Equal("before_model_call", evt.Phase);
        Assert.Equal(1, evt.StateCount);

        var entry = Assert.Single(evt.States);
        Assert.Equal(typeof(ErrorTrackingStateData).FullName, entry.Key);
        Assert.Equal(typeof(ErrorTrackingStateData).FullName, entry.Type);
        Assert.Equal("ErrorTracking", entry.PropertyName);
        Assert.Equal(StateScope.Thread, entry.Scope);
        Assert.False(entry.Persistent);
        Assert.Equal(1, entry.Version);
        Assert.False(entry.Redacted);
        Assert.Null(entry.Error);
        Assert.NotNull(entry.Json);
        Assert.Equal(2, entry.Json!.Value.GetProperty(nameof(ErrorTrackingStateData.ConsecutiveFailures)).GetInt32());
    }

    [Fact]
    public void BuildMiddlewareStateChanges_DetectsAddedUpdatedAndRemovedStates()
    {
        var empty = new MiddlewareState();
        var oneFailure = empty.WithErrorTracking(new ErrorTrackingStateData { ConsecutiveFailures = 1 });
        var twoFailures = empty.WithErrorTracking(new ErrorTrackingStateData { ConsecutiveFailures = 2 });
        var factories = CreateFactories();

        var added = Assert.Single(Agent.BuildMiddlewareStateChanges(factories, empty, oneFailure));
        Assert.Equal("added", added.ChangeType);
        Assert.Null(added.Before);
        Assert.NotNull(added.After);
        Assert.Equal(1, added.After!.Value.GetProperty(nameof(ErrorTrackingStateData.ConsecutiveFailures)).GetInt32());

        var updated = Assert.Single(Agent.BuildMiddlewareStateChanges(factories, oneFailure, twoFailures));
        Assert.Equal("updated", updated.ChangeType);
        Assert.Equal(1, updated.Before!.Value.GetProperty(nameof(ErrorTrackingStateData.ConsecutiveFailures)).GetInt32());
        Assert.Equal(2, updated.After!.Value.GetProperty(nameof(ErrorTrackingStateData.ConsecutiveFailures)).GetInt32());

        var removed = Assert.Single(Agent.BuildMiddlewareStateChanges(factories, oneFailure, empty));
        Assert.Equal("removed", removed.ChangeType);
        Assert.NotNull(removed.Before);
        Assert.Null(removed.After);
    }

    [Fact]
    public void BuildMiddlewareStateChanges_ReturnsEmptyWhenSerializedStateIsUnchanged()
    {
        var before = new MiddlewareState()
            .WithErrorTracking(new ErrorTrackingStateData { ConsecutiveFailures = 1 });
        var after = new MiddlewareState()
            .WithErrorTracking(new ErrorTrackingStateData { ConsecutiveFailures = 1 });

        var changes = Agent.BuildMiddlewareStateChanges(CreateFactories(), before, after);

        Assert.Empty(changes);
    }

    private static IReadOnlyDictionary<string, MiddlewareStateFactory> CreateFactories()
    {
        var key = typeof(ErrorTrackingStateData).FullName!;
        return new Dictionary<string, MiddlewareStateFactory>
        {
            [key] = new(
                FullyQualifiedName: key,
                StateType: typeof(ErrorTrackingStateData),
                PropertyName: "ErrorTracking",
                Version: 1,
                Persistent: false,
                Scope: StateScope.Thread,
                Deserialize: json => JsonSerializer.Deserialize<ErrorTrackingStateData>(json),
                Serialize: state => JsonSerializer.Serialize((ErrorTrackingStateData)state))
        };
    }
}
