// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Middleware.V2;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Tests for MiddlewareStateExtensions - the simplified middleware state update API.
/// Verifies that auto-instantiation, delegation, and safety mechanisms work correctly.
/// </summary>
public class MiddlewareStateExtensionsTests
{
    //
    // CORE FUNCTIONALITY TESTS
    //

    [Fact]
    public void UpdateMiddlewareState_SimpleIncrement_Works()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act - Simple increment (state is null, should auto-instantiate)
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s =>
            s.IncrementFailures()
        );

        // Assert
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();
        Assert.NotNull(state);
        Assert.Equal(1, state.ConsecutiveFailures);
    }

    [Fact]
    public void UpdateMiddlewareState_NullState_AutoInstantiates()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act - Update on null state (should auto-instantiate)
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s with
        {
            ConsecutiveFailures = 42
        });

        // Assert
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();
        Assert.NotNull(state);
        Assert.Equal(42, state.ConsecutiveFailures);
    }

    [Fact]
    public void UpdateMiddlewareState_ExistingState_UpdatesCorrectly()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Set initial state
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s with
        {
            ConsecutiveFailures = 5
        });

        // Act - Update existing state
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s =>
            s.IncrementFailures()
        );

        // Assert
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();
        Assert.NotNull(state);
        Assert.Equal(6, state.ConsecutiveFailures);
    }

    [Fact]
    public void UpdateMiddlewareState_MultipleUpdates_AllApplied()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act - Multiple sequential updates
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());

        // Assert
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();
        Assert.NotNull(state);
        Assert.Equal(3, state.ConsecutiveFailures);
    }

    [Fact]
    public void UpdateMiddlewareState_Reset_Works()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s with
        {
            ConsecutiveFailures = 10
        });

        // Act - Reset to defaults
        context.UpdateMiddlewareState<ErrorTrackingStateData>(_ =>
            new ErrorTrackingStateData()
        );

        // Assert
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();
        Assert.NotNull(state);
        Assert.Equal(0, state.ConsecutiveFailures);
    }

    [Fact]
    public void GetMiddlewareState_NullState_ReturnsNull()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public void GetMiddlewareState_ExistingState_ReturnsCorrectValue()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s with
        {
            ConsecutiveFailures = 42
        });

        // Act
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();

        // Assert
        Assert.NotNull(state);
        Assert.Equal(42, state.ConsecutiveFailures);
    }

    [Fact]
    public void UpdateMiddlewareState_DelegatesToUpdateState()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act - Update via extension method
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());

        // Assert - Verify state was actually updated in the context
        var stateViaAnalyze = context.Analyze(s =>
            s.MiddlewareState.ErrorTracking()?.ConsecutiveFailures ?? -1
        );
        Assert.Equal(1, stateViaAnalyze);
    }

    [Fact]
    public void GetMiddlewareState_DelegatesToAnalyze()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Set state directly via UpdateState
        context.UpdateState(s => s with
        {
            MiddlewareState = s.MiddlewareState.WithErrorTracking(
                new ErrorTrackingStateData { ConsecutiveFailures = 99 }
            )
        });

        // Act - Read via extension method
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();

        // Assert
        Assert.NotNull(state);
        Assert.Equal(99, state.ConsecutiveFailures);
    }

    [Fact]
    public void UpdateMiddlewareState_PreservesImmutability()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s with
        {
            ConsecutiveFailures = 5
        });

        // Capture original state
        var originalState = context.GetMiddlewareState<ErrorTrackingStateData>();

        // Act - Update state
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());

        // Assert - Original state unchanged (immutability)
        Assert.Equal(5, originalState!.ConsecutiveFailures);

        // New state has updated value
        var newState = context.GetMiddlewareState<ErrorTrackingStateData>();
        Assert.Equal(6, newState!.ConsecutiveFailures);
    }

    [Fact]
    public void UpdateMiddlewareState_WorksWithDifferentStateTypes()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act - Update multiple different state types
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        context.UpdateMiddlewareState<CircuitBreakerStateData>(s =>
            s.RecordToolCall("test_tool", "test_tool({})")
        );

        // Assert
        var errorState = context.GetMiddlewareState<ErrorTrackingStateData>();
        var cbState = context.GetMiddlewareState<CircuitBreakerStateData>();

        Assert.Equal(1, errorState!.ConsecutiveFailures);
        Assert.Equal(1, cbState!.ConsecutiveCountPerTool["test_tool"]);
    }

    //
    // ARGUMENT VALIDATION TESTS
    //

    [Fact]
    public void UpdateMiddlewareState_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        BeforeIterationContext? context = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            context!.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures())
        );
    }

    [Fact]
    public void UpdateMiddlewareState_NullTransform_ThrowsArgumentNullException()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            context.UpdateMiddlewareState<ErrorTrackingStateData>(null!)
        );
    }

    [Fact]
    public void GetMiddlewareState_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        BeforeIterationContext? context = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            context!.GetMiddlewareState<ErrorTrackingStateData>()
        );
    }

    //
    // EDGE CASE TESTS (from evaluator feedback)
    //

    [Fact]
    public void UpdateMiddlewareState_NullReturn_ThrowsArgumentException()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            context.UpdateMiddlewareState<ErrorTrackingStateData>(_ => null!)
        );

        Assert.Contains("Transform cannot return null", exception.Message);
        Assert.Contains("ErrorTrackingStateData", exception.Message);
        Assert.Contains("new ErrorTrackingStateData()", exception.Message);
    }

    [Fact]
    public void UpdateMiddlewareState_TransformThrows_PropagatesException()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            context.UpdateMiddlewareState<ErrorTrackingStateData>(s =>
                throw new InvalidOperationException("Test error")
            )
        );

        Assert.Equal("Test error", exception.Message);
    }

    [Fact]
    public void UpdateMiddlewareState_CapturedState_UsesStaleData()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Set initial state
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s with
        {
            ConsecutiveFailures = 10
        });

        // Capture state BEFORE update
        var captured = context.GetMiddlewareState<ErrorTrackingStateData>();

        // Simulate work that changes state
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());

        // Act - Using captured state (anti-pattern but legal)
        context.UpdateMiddlewareState<ErrorTrackingStateData>(_ => captured ?? new());

        // Assert - State reverted to captured value (demonstrates the problem)
        var finalState = context.GetMiddlewareState<ErrorTrackingStateData>();
        Assert.Equal(10, finalState!.ConsecutiveFailures); // Back to captured value, not 11

        // NOTE: This test documents the anti-pattern. The correct approach is:
        // context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.with { ... })
        // where 's' is always fresh
    }

    [Fact]
    public void UpdateMiddlewareState_NestedCalls_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act & Assert - Nested update throws due to generation counter
        var exception = Assert.Throws<InvalidOperationException>(() =>
            context.UpdateMiddlewareState<ErrorTrackingStateData>(s =>
            {
                // Nested update triggers generation counter violation
                context.UpdateMiddlewareState<CircuitBreakerStateData>(cb =>
                    cb.RecordToolCall("nested", "nested({})")
                );

                return s.IncrementFailures();
            })
        );

        Assert.Contains("State was modified during UpdateState transform execution", exception.Message);

        // NOTE: This test documents that nested calls are PREVENTED by the generation counter.
        // Better alternatives:
        // 1. Sequential updates: Update CB, then Error
        // 2. Use UpdateState for atomic multi-state updates
    }

    [Fact]
    public void UpdateMiddlewareState_TransformWithSideEffects_NotRecommended()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();
        var sideEffectFlag = false;

        // Act - Transform with side effect (not recommended but legal)
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s =>
        {
            sideEffectFlag = true; // Side effect!
            return s.IncrementFailures();
        });

        // Assert - Side effect occurred
        Assert.True(sideEffectFlag);

        // State update also worked
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();
        Assert.Equal(1, state!.ConsecutiveFailures);

        // NOTE: This test documents that side effects are possible but NOT recommended.
        // Best practice: Keep transforms pure (no side effects)
    }

    [Fact]
    public void UpdateMiddlewareState_ComplexTransformation_Works()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act - Complex multi-field update using RecordToolCall
        context.UpdateMiddlewareState<CircuitBreakerStateData>(s =>
            s.RecordToolCall("tool1", "tool1(arg1)").RecordToolCall("tool2", "tool2(arg2)")
        );

        // Assert
        var state = context.GetMiddlewareState<CircuitBreakerStateData>();
        Assert.NotNull(state);
        Assert.Equal("tool1(arg1)", state.LastSignaturePerTool["tool1"]);
        Assert.Equal("tool2(arg2)", state.LastSignaturePerTool["tool2"]);
        Assert.Equal(1, state.ConsecutiveCountPerTool["tool1"]);
        Assert.Equal(1, state.ConsecutiveCountPerTool["tool2"]);
    }

    //
    // THREAD SAFETY & GENERATION COUNTER TESTS
    //

    [Fact]
    public void UpdateMiddlewareState_PreservesGenerationCounter()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // This test verifies that the generation counter mechanism still works
        // The extension method delegates to UpdateState, which checks the generation counter

        // Act - Multiple updates should all succeed (generation counter increments each time)
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());

        // Assert - All updates applied successfully
        var state = context.GetMiddlewareState<ErrorTrackingStateData>();
        Assert.Equal(3, state!.ConsecutiveFailures);
    }

    //
    // INTEGRATION TESTS WITH REAL MIDDLEWARE
    //

    [Fact]
    public void Integration_CircuitBreakerMiddleware_UsesExtensionMethods()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeToolExecutionContext(
            toolCalls: new List<FunctionCallContent>
            {
                new("test_tool", "call1", new Dictionary<string, object?> { ["arg"] = "value" })
            }
        );

        // Simulate circuit breaker recording a call using extension method
        context.UpdateMiddlewareState<CircuitBreakerStateData>(s =>
            s.RecordToolCall("test_tool", "test_tool({\"arg\":\"value\"})")
        );

        // Act - Read state using extension method
        var cbState = context.GetMiddlewareState<CircuitBreakerStateData>();

        // Assert
        Assert.NotNull(cbState);
        Assert.Equal(1, cbState.ConsecutiveCountPerTool.GetValueOrDefault("test_tool"));
        Assert.Equal("test_tool({\"arg\":\"value\"})", cbState.LastSignaturePerTool["test_tool"]);
    }

    [Fact]
    public void Integration_ErrorTracking_IncrementAndReset()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act - Simulate error tracking lifecycle
        // 1. Record first error
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        Assert.Equal(1, context.GetMiddlewareState<ErrorTrackingStateData>()!.ConsecutiveFailures);

        // 2. Record second error
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        Assert.Equal(2, context.GetMiddlewareState<ErrorTrackingStateData>()!.ConsecutiveFailures);

        // 3. Success - reset failures
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.ResetFailures());
        Assert.Equal(0, context.GetMiddlewareState<ErrorTrackingStateData>()!.ConsecutiveFailures);
    }

    [Fact]
    public void Integration_MultipleStateTypes_IndependentUpdates()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act - Update different state types independently
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());

        context.UpdateMiddlewareState<CircuitBreakerStateData>(s =>
            s.RecordToolCall("tool1", "sig1")
             .RecordToolCall("tool2", "sig2")
        );

        // Assert - Both states updated correctly
        var errorState = context.GetMiddlewareState<ErrorTrackingStateData>();
        var cbState = context.GetMiddlewareState<CircuitBreakerStateData>();

        Assert.Equal(3, errorState!.ConsecutiveFailures);
        Assert.Equal(1, cbState!.ConsecutiveCountPerTool["tool1"]);
        Assert.Equal(1, cbState.ConsecutiveCountPerTool["tool2"]);
    }

    [Fact]
    public void Integration_BeforeAfterComparison_VerifiesCodeReduction()
    {
        // This test demonstrates the code reduction benefit

        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // BEFORE approach (verbose, but still works)
        context.UpdateState(s =>
        {
            var errState = s.MiddlewareState.ErrorTracking() ?? new ErrorTrackingStateData();
            return s with
            {
                MiddlewareState = s.MiddlewareState.WithErrorTracking(
                    errState.IncrementFailures()
                )
            };
        });

        Assert.Equal(1, context.GetMiddlewareState<ErrorTrackingStateData>()!.ConsecutiveFailures);

        // AFTER approach (concise)
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());

        Assert.Equal(2, context.GetMiddlewareState<ErrorTrackingStateData>()!.ConsecutiveFailures);

        // Both approaches produce the same result - backward compatible!
    }

    [Fact]
    public void Integration_SequentialUpdates_BetterThanNested()
    {
        // Arrange
        var context = MiddlewareTestHelpers.CreateBeforeIterationContext();

        // Act - Sequential updates (recommended approach)
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());
        context.UpdateMiddlewareState<CircuitBreakerStateData>(s =>
            s.RecordToolCall("tool", "sig")
        );

        // Assert - Both updates applied successfully
        Assert.Equal(1, context.GetMiddlewareState<ErrorTrackingStateData>()!.ConsecutiveFailures);
        Assert.Equal(1, context.GetMiddlewareState<CircuitBreakerStateData>()!.ConsecutiveCountPerTool["tool"]);

        // This is the recommended pattern for multi-state updates
        // Better than nested calls (which throw) or complex UpdateState (more verbose)
    }
}

