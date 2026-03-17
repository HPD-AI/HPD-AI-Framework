using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Tests for CoalesceDeltas feature that combines streaming text/reasoning deltas
/// into single complete events.
/// </summary>
public class CoalesceDeltasTests : AgentTestBase
{
    /// <summary>
    /// Test that CoalesceDeltas = false (default) emits multiple text delta events
    /// </summary>
    [Fact]
    public async Task CoalesceDeltas_False_EmitsMultipleTextDeltas()
    {
        // Arrange: Create a fake client that returns text in chunks
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("Hello", " ", "world", "!");

        var agent = CreateAgent(client: fakeClient);
        var options = new AgentRunConfig
        {
            CoalesceDeltas = false // Explicit, though this is default
        };

        // Act: Collect all events
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAsync("test", options: options, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Should have multiple TextDeltaEvent
        var textDeltas = events.OfType<TextDeltaEvent>().ToList();
        Assert.True(textDeltas.Count > 1, $"Expected multiple text deltas, got {textDeltas.Count}");

        // Verify the chunks are separate
        Assert.Contains(textDeltas, e => e.Text == "Hello");
        Assert.Contains(textDeltas, e => e.Text == " ");
        Assert.Contains(textDeltas, e => e.Text == "world");
        Assert.Contains(textDeltas, e => e.Text == "!");
    }

    /// <summary>
    /// Test that CoalesceDeltas = true emits a single complete text delta event
    /// </summary>
    [Fact]
    public async Task CoalesceDeltas_True_EmitsSingleTextDelta()
    {
        // Arrange: Create a fake client that returns text in chunks
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("Hello", " ", "world", "!");

        var agent = CreateAgent(client: fakeClient);
        var options = new AgentRunConfig
        {
            CoalesceDeltas = true
        };

        // Act: Collect all events
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAsync("test", options: options, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Should have exactly ONE TextDeltaEvent with complete text
        var textDeltas = events.OfType<TextDeltaEvent>().ToList();
        Assert.Single(textDeltas);
        Assert.Equal("Hello world!", textDeltas[0].Text);
    }

    /// <summary>
    /// Test that CoalesceDeltas preserves message start/end events
    /// </summary>
    [Fact]
    public async Task CoalesceDeltas_True_PreservesMessageStartEndEvents()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("Test", " ", "message");

        var agent = CreateAgent(client: fakeClient);
        var options = new AgentRunConfig { CoalesceDeltas = true };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAsync("test", options: options, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Should still have start and end events
        Assert.Contains(events, e => e is TextMessageStartEvent);
        Assert.Contains(events, e => e is TextMessageEndEvent);

        // And one coalesced text delta
        var textDeltas = events.OfType<TextDeltaEvent>().ToList();
        Assert.Single(textDeltas);
        Assert.Equal("Test message", textDeltas[0].Text);
    }

    /// <summary>
    /// Test that CoalesceDeltas in AgentConfig acts as default
    /// </summary>
    [Fact]
    public async Task CoalesceDeltas_Config_ActsAsDefault()
    {
        // Arrange: Create config with CoalesceDeltas = true
        var config = DefaultConfig();
        config.CoalesceDeltas = true;

        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("Config", " ", "default");

        var agent = CreateAgent(config: config, client: fakeClient);

        // Act: No run options specified, should use config default
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAsync("test", cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Should coalesce because config has it enabled
        var textDeltas = events.OfType<TextDeltaEvent>().ToList();
        Assert.Single(textDeltas);
        Assert.Equal("Config default", textDeltas[0].Text);
    }

    /// <summary>
    /// Test that AgentRunConfig.CoalesceDeltas overrides AgentConfig.CoalesceDeltas
    /// </summary>
    [Fact]
    public async Task CoalesceDeltas_RunConfig_OverridesConfig()
    {
        // Arrange: Config says coalesce=true, but run options says false
        var config = DefaultConfig();
        config.CoalesceDeltas = true;  // Config wants coalescing

        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("Override", " ", "test");

        var agent = CreateAgent(config: config, client: fakeClient);
        var options = new AgentRunConfig
        {
            CoalesceDeltas = false  // Run options overrides to false
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAsync("test", options: options, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Should NOT coalesce because run options explicitly disabled it
        var textDeltas = events.OfType<TextDeltaEvent>().ToList();
        Assert.True(textDeltas.Count > 1, "Expected multiple deltas when run options overrides config");
        Assert.Contains(textDeltas, e => e.Text == "Override");
        Assert.Contains(textDeltas, e => e.Text == " ");
        Assert.Contains(textDeltas, e => e.Text == "test");
    }
}
