// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using System.Text.Json;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// CRITICAL CHECKPOINT ROUND-TRIP TEST .
/// Verifies that middleware state survives serialization → deserialization → access cycle.
/// This is the single most important test for the checkpoint/resume feature.
/// </summary>
public class CheckpointRoundTripTests
{
    /// <summary>
    /// The golden path: State is written, checkpointed, resumed, and accessible.
    /// </summary>
    [Fact]
    public async Task CriticalPath_StateRoundTrip_PreservesAllMiddlewareState()
    {
        //      
        // PHASE 1: RUNTIME - Set middleware state during execution
        //      

        var errorState = new ErrorTrackingStateData { ConsecutiveFailures = 3 };
        var cbState = new CircuitBreakerStateData()
            .RecordToolCall("ReadFile", "ReadFile(path=/foo/bar.txt)")
            .RecordToolCall("ReadFile", "ReadFile(path=/foo/bar.txt)")
            .RecordToolCall("ReadFile", "ReadFile(path=/foo/bar.txt)");
        var permState = ContinuationPermissionStateData.WithInitialLimit(20)
            .ExtendLimit(10); // User approved continuation

        var runtimeState = AgentLoopState.InitialSafe(
            messages: new[] { new ChatMessage(ChatRole.User, "Test message") },
            runId: "checkpoint-test-run",
            conversationId: "checkpoint-test-conv",
            agentName: "CheckpointAgent")
            with
            {
                MiddlewareState = new MiddlewareState()
                    .WithErrorTracking(errorState)
                    .WithCircuitBreaker(cbState)
                    .WithContinuationPermission(permState)
            };

        // Verify runtime state is correct
        Assert.Equal(3, runtimeState.MiddlewareState.ErrorTracking()?.ConsecutiveFailures);
        Assert.Equal(3, runtimeState.MiddlewareState.CircuitBreaker()?.ConsecutiveCountPerTool["ReadFile"]);
        Assert.Equal(30, runtimeState.MiddlewareState.ContinuationPermission()?.CurrentExtendedLimit);

        //      
        // PHASE 2: CHECKPOINT - Serialize to durable storage
        //      

        var json = JsonSerializer.Serialize(runtimeState, AIJsonUtilities.DefaultOptions);
        Assert.NotEmpty(json);

        // DEBUG: Print JSON to see what's actually serialized
        Console.WriteLine("=== SERIALIZED JSON ===");
        Console.WriteLine(json);
        Console.WriteLine("=======================");

        // Verify JSON contains middleware state (camelCase property names due to HPDFFIJsonContext)
        Assert.Contains("\"middlewareState\"", json);
        Assert.Contains("\"states\"", json);  // The States property from container

        // The states dictionary uses fully-qualified type names as keys
        Assert.Contains("\"HPD.Agent.ErrorTrackingStateData\"", json);
        Assert.Contains("\"consecutiveFailures\": 3", json);  // camelCase property (note: space after colon due to WriteIndented)
        Assert.Contains("\"HPD.Agent.CircuitBreakerStateData\"", json);
        Assert.Contains("\"HPD.Agent.ContinuationPermissionStateData\"", json);
        Assert.Contains("\"currentExtendedLimit\": 30", json);  // camelCase property (note: space after colon due to WriteIndented)

        //      
        // PHASE 3: RESUME - Deserialize from durable storage
        //      

        var deserializedState = JsonSerializer.Deserialize<AgentLoopState>(json, AIJsonUtilities.DefaultOptions);
        Assert.NotNull(deserializedState);

        //      
        // PHASE 4: ACCESS - Smart accessor handles JsonElement → T
        //      

        // First access: JsonElement → ErrorTrackingStateData (with caching)
        var deserializedErrorState = deserializedState.MiddlewareState.ErrorTracking();
        Assert.NotNull(deserializedErrorState);
        Assert.Equal(3, deserializedErrorState.ConsecutiveFailures);

        // First access: JsonElement → CircuitBreakerStateData (with caching)
        var deserializedCbState = deserializedState.MiddlewareState.CircuitBreaker();
        Assert.NotNull(deserializedCbState);
        Assert.Equal(3, deserializedCbState.ConsecutiveCountPerTool["ReadFile"]);
        Assert.Equal("ReadFile(path=/foo/bar.txt)", deserializedCbState.LastSignaturePerTool["ReadFile"]);

        // First access: JsonElement → ContinuationPermissionStateData (with caching)
        var deserializedPermState = deserializedState.MiddlewareState.ContinuationPermission();
        Assert.NotNull(deserializedPermState);
        Assert.Equal(30, deserializedPermState.CurrentExtendedLimit);

        //      
        // PHASE 5: CACHE VERIFICATION - Second access uses cache
        //      

        var cachedErrorState = deserializedState.MiddlewareState.ErrorTracking();
        Assert.Same(deserializedErrorState, cachedErrorState); // Same instance = cache hit

        var cachedCbState = deserializedState.MiddlewareState.CircuitBreaker();
        Assert.Same(deserializedCbState, cachedCbState);

        var cachedPermState = deserializedState.MiddlewareState.ContinuationPermission();
        Assert.Same(deserializedPermState, cachedPermState);

        //      
        // PHASE 6: STATE UPDATE - Middleware can modify state
        //      

        var updatedErrorState = deserializedErrorState.IncrementFailures(); // 3 → 4
        var updatedState = deserializedState with
        {
            MiddlewareState = deserializedState.MiddlewareState.WithErrorTracking(updatedErrorState)
        };

        Assert.Equal(4, updatedState.MiddlewareState.ErrorTracking()?.ConsecutiveFailures);

        //      
        // SUCCESS: All phases passed!
        //      
    }

    /// <summary>
    /// Edge case: Empty middleware state checkpoints and resumes correctly.
    /// </summary>
    [Fact]
    public void EmptyMiddlewareState_RoundTrip_ReturnsNullForAllStates()
    {
        var state = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "empty-run",
            conversationId: "empty-conv",
            agentName: "EmptyAgent");

        var json = JsonSerializer.Serialize(state, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AgentLoopState>(json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.MiddlewareState.ErrorTracking());
        Assert.Null(deserialized.MiddlewareState.CircuitBreaker());
        Assert.Null(deserialized.MiddlewareState.ContinuationPermission());
    }

    /// <summary>
    /// Edge case: Partial middleware state (only some states set).
    /// </summary>
    [Fact]
    public void PartialMiddlewareState_RoundTrip_PreservesOnlySetStates()
    {
        var state = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "partial-run",
            conversationId: "partial-conv",
            agentName: "PartialAgent")
            with
            {
                MiddlewareState = new MiddlewareState()
                    .WithErrorTracking(new ErrorTrackingStateData { ConsecutiveFailures = 1 })
                // Note: CircuitBreaker and ContinuationPermission NOT set
            };

        var json = JsonSerializer.Serialize(state, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AgentLoopState>(json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.MiddlewareState.ErrorTracking());
        Assert.Equal(1, deserialized.MiddlewareState.ErrorTracking().ConsecutiveFailures);
        Assert.Null(deserialized.MiddlewareState.CircuitBreaker());
        Assert.Null(deserialized.MiddlewareState.ContinuationPermission());
    }

    /// <summary>
    /// Performance check: Repeated access uses cache (no repeated deserialization).
    /// </summary>
    [Fact]
    public void RepeatedAccess_UsesCache_ReturnsSameInstance()
    {
        var state = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "cache-run",
            conversationId: "cache-conv",
            agentName: "CacheAgent")
            with
            {
                MiddlewareState = new MiddlewareState()
                    .WithErrorTracking(new ErrorTrackingStateData { ConsecutiveFailures = 2 })
            };

        var json = JsonSerializer.Serialize(state, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AgentLoopState>(json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);

        // Access state 10 times
        var instances = new List<ErrorTrackingStateData?>();
        for (int i = 0; i < 10; i++)
        {
            instances.Add(deserialized.MiddlewareState.ErrorTracking());
        }

        // All instances should be the same (cache hit)
        var firstInstance = instances[0];
        Assert.NotNull(firstInstance);
        Assert.All(instances, instance => Assert.Same(firstInstance, instance));
    }

    /// <summary>
    /// Complex scenario: Multiple state updates through checkpoint/resume cycle.
    /// </summary>
    [Fact]
    public void ComplexScenario_MultipleUpdates_PreservesStateCorrectly()
    {
        // Initial state
        var state1 = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "complex-run",
            conversationId: "complex-conv",
            agentName: "ComplexAgent")
            with
            {
                MiddlewareState = new MiddlewareState()
                    .WithErrorTracking(new ErrorTrackingStateData { ConsecutiveFailures = 0 })
            };

        // Checkpoint 1
        var json1 = JsonSerializer.Serialize(state1, AIJsonUtilities.DefaultOptions);
        var state2 = JsonSerializer.Deserialize<AgentLoopState>(json1, AIJsonUtilities.DefaultOptions)!;

        // Update: Error occurred
        var errorState = state2.MiddlewareState.ErrorTracking()!.IncrementFailures();
        var state3 = state2 with
        {
            MiddlewareState = state2.MiddlewareState.WithErrorTracking(errorState)
        };

        // Checkpoint 2
        var json2 = JsonSerializer.Serialize(state3, AIJsonUtilities.DefaultOptions);
        var state4 = JsonSerializer.Deserialize<AgentLoopState>(json2, AIJsonUtilities.DefaultOptions)!;

        // Update: Another error
        var errorState2 = state4.MiddlewareState.ErrorTracking()!.IncrementFailures();
        var state5 = state4 with
        {
            MiddlewareState = state4.MiddlewareState.WithErrorTracking(errorState2)
        };

        // Checkpoint 3
        var json3 = JsonSerializer.Serialize(state5, AIJsonUtilities.DefaultOptions);
        var finalState = JsonSerializer.Deserialize<AgentLoopState>(json3, AIJsonUtilities.DefaultOptions)!;

        // Verify final state has 2 consecutive failures
        Assert.Equal(2, finalState.MiddlewareState.ErrorTracking()?.ConsecutiveFailures);
    }

    /// <summary>
    /// Integration test: Real middleware workflow with checkpoint/resume.
    /// </summary>
    [Fact]
    public async Task RealMiddlewareWorkflow_WithCheckpoint_WorksCorrectly()
    {
        // Setup: Initial state with some middleware state
        var initialState = AgentLoopState.InitialSafe(
            messages: new[] { new ChatMessage(ChatRole.User, "Test") },
            runId: "workflow-run",
            conversationId: "workflow-conv",
            agentName: "WorkflowAgent")
            with
            {
                MiddlewareState = new MiddlewareState()
                    .WithErrorTracking(new ErrorTrackingStateData { ConsecutiveFailures = 1 })
                    .WithCircuitBreaker(new CircuitBreakerStateData()
                        .RecordToolCall("Tool1", "sig1"))
            };

        // Checkpoint
        var checkpointJson = JsonSerializer.Serialize(initialState, AIJsonUtilities.DefaultOptions);

        // Simulate crash/restart...

        // Resume
        var resumedState = JsonSerializer.Deserialize<AgentLoopState>(checkpointJson, AIJsonUtilities.DefaultOptions)!;

        // Verify middleware can read state
        Assert.Equal(1, resumedState.MiddlewareState.ErrorTracking()?.ConsecutiveFailures);
        Assert.Equal(1, resumedState.MiddlewareState.CircuitBreaker()?.ConsecutiveCountPerTool["Tool1"]);

        // Verify middleware can update state
        var cbState = resumedState.MiddlewareState.CircuitBreaker()!;
        var updatedCbState = cbState.RecordToolCall("Tool1", "sig1"); // Identical call

        var updatedState = resumedState with
        {
            MiddlewareState = resumedState.MiddlewareState.WithCircuitBreaker(updatedCbState)
        };

        Assert.Equal(2, updatedState.MiddlewareState.CircuitBreaker()?.ConsecutiveCountPerTool["Tool1"]);
    }
}
