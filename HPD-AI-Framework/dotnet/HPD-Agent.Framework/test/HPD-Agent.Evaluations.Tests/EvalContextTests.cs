// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;

namespace HPD.Agent.Evaluations.Tests;

/// <summary>
/// Tests for EvalContext — the AsyncLocal mid-run instrumentation API.
///
/// Key invariants:
/// 1. SetAttribute / IncrementMetric are no-ops when no context is active.
/// 2. After Activate(), values are captured and readable via the returned EvalContextData.
/// 3. After Deactivate(), the context is cleared and subsequent calls are no-ops again.
/// 4. Parallel tasks sharing the same AsyncLocal scope all write to the same accumulator.
/// </summary>
public sealed class EvalContextTests
{
    [Fact]
    public void SetAttribute_WhenNoContextActive_IsNoOp()
    {
        // Arrange — ensure no context is active
        EvalContext.Deactivate();

        // Act — should not throw
        EvalContext.SetAttribute("key", "value");

        // Assert — no exception, nothing to verify (no context = no-op)
    }

    [Fact]
    public void IncrementMetric_WhenNoContextActive_IsNoOp()
    {
        EvalContext.Deactivate();

        // Should not throw
        EvalContext.IncrementMetric("calls", 1.0);
    }

    [Fact]
    public void SetAttribute_WhenContextActive_StoresValue()
    {
        // Arrange
        var data = EvalContext.Activate();

        try
        {
            // Act
            EvalContext.SetAttribute("retrieved_chunks", new[] { "chunk1", "chunk2" });
            EvalContext.SetAttribute("model_id", "claude-sonnet-4-6");

            // Assert
            data.Attributes.Should().ContainKey("retrieved_chunks");
            data.Attributes.Should().ContainKey("model_id");
            data.Attributes["model_id"].Should().Be("claude-sonnet-4-6");
        }
        finally
        {
            EvalContext.Deactivate();
        }
    }

    [Fact]
    public void SetAttribute_OverwritesPreviousValue()
    {
        var data = EvalContext.Activate();
        try
        {
            EvalContext.SetAttribute("key", "first");
            EvalContext.SetAttribute("key", "second");

            data.Attributes["key"].Should().Be("second");
        }
        finally
        {
            EvalContext.Deactivate();
        }
    }

    [Fact]
    public void IncrementMetric_WhenContextActive_AccumulatesValue()
    {
        var data = EvalContext.Activate();
        try
        {
            EvalContext.IncrementMetric("retrieval_calls", 1.0);
            EvalContext.IncrementMetric("retrieval_calls", 1.0);
            EvalContext.IncrementMetric("retrieval_calls", 1.0);

            data.Metrics["retrieval_calls"].Should().Be(3.0);
        }
        finally
        {
            EvalContext.Deactivate();
        }
    }

    [Fact]
    public void IncrementMetric_MultipleKeys_TrackIndependently()
    {
        var data = EvalContext.Activate();
        try
        {
            EvalContext.IncrementMetric("tokens", 100.0);
            EvalContext.IncrementMetric("tokens", 50.0);
            EvalContext.IncrementMetric("latency_ms", 200.0);

            data.Metrics["tokens"].Should().Be(150.0);
            data.Metrics["latency_ms"].Should().Be(200.0);
        }
        finally
        {
            EvalContext.Deactivate();
        }
    }

    [Fact]
    public void Deactivate_ClearsContext_SubsequentCallsAreNoOps()
    {
        var data = EvalContext.Activate();
        EvalContext.SetAttribute("key", "value");
        data.Attributes.Should().ContainKey("key");

        EvalContext.Deactivate();

        // After deactivation, SetAttribute must not throw and must not add to old data
        EvalContext.SetAttribute("new_key", "new_value");
        data.Attributes.Should().NotContainKey("new_key");
    }

    [Fact]
    public async Task ParallelToolTasks_AllWriteToSameAccumulator()
    {
        // AsyncLocal flows the reference into child tasks — mutations on
        // the shared ConcurrentDictionary are visible to the parent.
        var data = EvalContext.Activate();
        try
        {
            // Simulate parallel tool calls all calling IncrementMetric
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => EvalContext.IncrementMetric("parallel_calls", 1.0)));

            await Task.WhenAll(tasks);

            // All 10 increments should have reached the shared accumulator
            data.Metrics["parallel_calls"].Should().Be(10.0);
        }
        finally
        {
            EvalContext.Deactivate();
        }
    }

    [Fact]
    public async Task ChildTaskAttribute_IsVisibleInParent()
    {
        // Demonstrates that the AsyncLocal reference propagates into child tasks
        // and mutations on the shared object flow back to the parent scope.
        var data = EvalContext.Activate();
        try
        {
            await Task.Run(() => EvalContext.SetAttribute("from_child", "value"));

            data.Attributes.Should().ContainKey("from_child",
                "mutations to the shared ConcurrentDictionary from a child task are visible to parent");
        }
        finally
        {
            EvalContext.Deactivate();
        }
    }
}
