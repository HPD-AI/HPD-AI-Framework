using Xunit;
using FluentAssertions;
using HPD.Agent;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Tests for automatic middleware state persistence via LoadFromSession/SaveToSession.
/// Verifies that states marked with [MiddlewareState(Persistent = true)] are
/// automatically serialized and restored across agent runs.
/// </summary>
public class SessionPersistenceTests
{
    // Create test factories for persistent states
    // PermissionPersistentStateData is session-scoped (shared across branches)
    // HistoryReductionStateData is branch-scoped (per-conversation path, the default)
    private static readonly IReadOnlyDictionary<string, MiddlewareStateFactory> TestFactories =
        new Dictionary<string, MiddlewareStateFactory>
        {
            ["HPD.Agent.PermissionPersistentStateData"] = new MiddlewareStateFactory(
                FullyQualifiedName: "HPD.Agent.PermissionPersistentStateData",
                StateType: typeof(PermissionPersistentStateData),
                PropertyName: "PermissionPersistent",
                Version: 1,
                Persistent: true,
                Scope: StateScope.Session,
                Deserialize: json => JsonSerializer.Deserialize<PermissionPersistentStateData>(json, AIJsonUtilities.DefaultOptions),
                Serialize: state => JsonSerializer.Serialize((PermissionPersistentStateData)state, AIJsonUtilities.DefaultOptions)
            ),
            ["HPD.Agent.HistoryReductionStateData"] = new MiddlewareStateFactory(
                FullyQualifiedName: "HPD.Agent.HistoryReductionStateData",
                StateType: typeof(HistoryReductionStateData),
                PropertyName: "HistoryReduction",
                Version: 1,
                Persistent: true,
                Scope: StateScope.Branch,
                Deserialize: json => JsonSerializer.Deserialize<HistoryReductionStateData>(json, AIJsonUtilities.DefaultOptions),
                Serialize: state => JsonSerializer.Serialize((HistoryReductionStateData)state, AIJsonUtilities.DefaultOptions)
            )
        }.ToImmutableDictionary();

    [Fact]
    public void LoadFromSession_WithNullSession_ReturnsEmptyState()
    {
        // Act
        var state = MiddlewareState.LoadFromSession(null, TestFactories);

        // Assert
        state.Should().NotBeNull();
        state.PermissionPersistent().Should().BeNull("no session data to load from");
        state.HistoryReduction().Should().BeNull("no session data to load from");
    }

    [Fact]
    public void LoadFromSession_RestoresPermissionState()
    {
        // Arrange: Create session with permission state
        var session = new global::HPD.Agent.Session();
        var permState = new PermissionPersistentStateData()
            .WithPermission("Bash", PermissionChoice.AlwaysAllow)
            .WithPermission("Read", PermissionChoice.AlwaysDeny);

        var middlewareState = new MiddlewareState()
            .WithPermissionPersistent(permState);

        middlewareState.SaveToSession(session, TestFactories);

        // Act: Load from session
        var restored = MiddlewareState.LoadFromSession(session, TestFactories);

        // Assert: Permission state is restored
        restored.PermissionPersistent().Should().NotBeNull();
        restored.PermissionPersistent()!.GetPermission("Bash")
            .Should().Be(PermissionChoice.AlwaysAllow);
        restored.PermissionPersistent().GetPermission("Read")
            .Should().Be(PermissionChoice.AlwaysDeny);
        restored.PermissionPersistent().GetPermission("Write")
            .Should().BeNull("not stored");
    }

    [Fact]
    public void LoadFromBranch_RestoresHistoryReduction()
    {
        // Arrange: Create branch with history reduction state (branch-scoped)
        var branch = new global::HPD.Agent.Branch("test-session");
        // Create enough messages for the test
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>();
        for (int i = 0; i < 100; i++)
        {
            messages.Add(new(Microsoft.Extensions.AI.ChatRole.User, $"message {i}"));
        }

        var reduction = CachedReduction.Create(
            messages: messages,
            summaryContent: "Test summary",
            summarizedUpToIndex: 90,
            targetCount: 100,
            reductionThreshold: 5,
            countAtReduction: messages.Count,
            countingUnit: HistoryCountingUnit.Messages);

        var hrState = new HistoryReductionStateData().WithReduction(reduction);
        var middlewareState = new MiddlewareState().WithHistoryReduction(hrState);

        middlewareState.SaveToBranch(branch, TestFactories);

        // Act: Load from branch
        var restored = MiddlewareState.LoadFromBranch(branch, TestFactories);

        // Assert: History reduction is restored
        restored.HistoryReduction().Should().NotBeNull();
        restored.HistoryReduction()!.LastReduction.Should().NotBeNull();
        restored.HistoryReduction().LastReduction!.SummarizedUpToIndex.Should().Be(90);
        restored.HistoryReduction().LastReduction.CountAtReduction.Should().Be(100);
        restored.HistoryReduction().LastReduction.SummaryContent.Should().Be("Test summary");
    }

    [Fact]
    public void SavePersistsMultipleStates_AcrossSessionAndBranch()
    {
        // Arrange: Create multiple persistent states with different scopes
        var session = new global::HPD.Agent.Session();
        var branch = new global::HPD.Agent.Branch(session.Id);

        var permState = new PermissionPersistentStateData()
            .WithPermission("Bash", PermissionChoice.AlwaysAllow);

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.User, "msg"),
        };

        var reduction = CachedReduction.Create(
            messages: messages,
            summaryContent: "Summary",
            summarizedUpToIndex: 50,
            targetCount: 60,
            reductionThreshold: 10,
            countAtReduction: messages.Count,
            countingUnit: HistoryCountingUnit.Messages);
        var hrState = new HistoryReductionStateData().WithReduction(reduction);

        var middlewareState = new MiddlewareState()
            .WithPermissionPersistent(permState)
            .WithHistoryReduction(hrState);

        // Act: Save session-scoped to session, branch-scoped to branch
        middlewareState.SaveToSession(session, TestFactories);
        middlewareState.SaveToBranch(branch, TestFactories);

        // Load back from respective stores
        var restoredFromSession = MiddlewareState.LoadFromSession(session, TestFactories);
        var restoredFromBranch = MiddlewareState.LoadFromBranch(branch, TestFactories);

        // Assert: Permission state restored from session (session-scoped)
        restoredFromSession.PermissionPersistent().Should().NotBeNull();
        restoredFromSession.PermissionPersistent()!.GetPermission("Bash")
            .Should().Be(PermissionChoice.AlwaysAllow);

        // Assert: History reduction restored from branch (branch-scoped)
        restoredFromBranch.HistoryReduction().Should().NotBeNull();
        restoredFromBranch.HistoryReduction()!.LastReduction!.SummarizedUpToIndex.Should().Be(50);
    }

    [Fact]
    public void SaveToSession_DoesNotPersistTransientStates()
    {
        // Arrange: Create transient batch permission state
        var session = new global::HPD.Agent.Session();
        var batchState = new BatchPermissionStateData()
            .RecordApproval("Bash")
            .RecordApproval("Read");

        var middlewareState = new MiddlewareState()
            .WithBatchPermission(batchState);

        // Act: Save to session (batch state should NOT be saved)
        middlewareState.SaveToSession(session, TestFactories);

        // Load back
        var restored = MiddlewareState.LoadFromSession(session, TestFactories);

        // Assert: Batch state is NOT restored (it's transient)
        restored.BatchPermission().Should().BeNull("batch state is transient and should not persist");
    }

    [Fact]
    public void SaveToSession_WithNullState_DoesNotSaveAnything()
    {
        // Arrange
        var session = new global::HPD.Agent.Session();
        var middlewareState = new MiddlewareState(); // All states null

        // Act
        middlewareState.SaveToSession(session, TestFactories);

        // Load back
        var restored = MiddlewareState.LoadFromSession(session, TestFactories);

        // Assert: Nothing restored (nothing was saved)
        restored.PermissionPersistent().Should().BeNull();
        restored.HistoryReduction().Should().BeNull();
    }

    [Fact]
    public void SaveToSession_WithNullSession_ThrowsArgumentNullException()
    {
        // Arrange
        var middlewareState = new MiddlewareState();

        // Act & Assert
        Action act = () => middlewareState.SaveToSession(null!, TestFactories);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PermissionState_RoundTrip_PreservesAllData()
    {
        // Arrange: Create complex permission state
        var session = new global::HPD.Agent.Session();
        var permState = new PermissionPersistentStateData()
            .WithPermission("Bash", PermissionChoice.AlwaysAllow)
            .WithPermission("Read", PermissionChoice.AlwaysAllow)
            .WithPermission("Write", PermissionChoice.AlwaysDeny)
            .WithPermission("Delete", PermissionChoice.Ask);

        var middlewareState = new MiddlewareState().WithPermissionPersistent(permState);

        // Act: Round-trip through session
        middlewareState.SaveToSession(session, TestFactories);
        var restored = MiddlewareState.LoadFromSession(session, TestFactories);

        // Assert: All permission choices preserved
        restored.PermissionPersistent()!.GetPermission("Bash").Should().Be(PermissionChoice.AlwaysAllow);
        restored.PermissionPersistent().GetPermission("Read").Should().Be(PermissionChoice.AlwaysAllow);
        restored.PermissionPersistent().GetPermission("Write").Should().Be(PermissionChoice.AlwaysDeny);
        restored.PermissionPersistent().GetPermission("Delete").Should().Be(PermissionChoice.Ask);
    }

    [Fact]
    public void SessionPersistence_MultipleRoundTrips_PreservesData()
    {
        // Arrange
        var session = new global::HPD.Agent.Session();
        var permState1 = new PermissionPersistentStateData()
            .WithPermission("Bash", PermissionChoice.AlwaysAllow);

        // Act: First round-trip
        var state1 = new MiddlewareState().WithPermissionPersistent(permState1);
        state1.SaveToSession(session, TestFactories);
        var restored1 = MiddlewareState.LoadFromSession(session, TestFactories);

        // Add more permissions
        var permState2 = restored1.PermissionPersistent()!
            .WithPermission("Read", PermissionChoice.AlwaysDeny);
        var state2 = new MiddlewareState().WithPermissionPersistent(permState2);
        state2.SaveToSession(session, TestFactories);
        var restored2 = MiddlewareState.LoadFromSession(session, TestFactories);

        // Assert: Both permissions present after multiple round-trips
        restored2.PermissionPersistent().Should().NotBeNull();
        restored2.PermissionPersistent()!.GetPermission("Bash").Should().Be(PermissionChoice.AlwaysAllow);
        restored2.PermissionPersistent().GetPermission("Read").Should().Be(PermissionChoice.AlwaysDeny);
    }
}
