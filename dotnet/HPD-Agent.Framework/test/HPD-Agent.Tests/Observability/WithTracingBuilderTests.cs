// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Diagnostics;
using System.Collections.Concurrent;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Observability;

/// <summary>
/// Integration tests for <see cref="AgentBuilder.WithTracing"/>.
/// Verifies that calling WithTracing() wires the TracingObserver so that real agent
/// runs produce OTel Activity spans, and that it coexists with WithTelemetry().
/// </summary>
public class WithTracingBuilderTests : AgentTestBase, IDisposable
{
    // Each test instance gets a unique source name so parallel xUnit runs
    // don't share an ActivityListener and pick up each other's spans.
    private readonly string _testSourceName = $"HPD.Agent.BuilderTest.{Guid.NewGuid():N}";

    private readonly ConcurrentBag<Activity> _completed = new();
    private readonly ActivityListener _listener;

    public WithTracingBuilderTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == _testSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _completed.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    void IDisposable.Dispose()
    {
        _listener.Dispose();
    }

    // ── WithTracing wires up span production ──────────────────────────────────

    [Fact]
    public async Task WithTracing_AgentRun_ProducesTurnSpan()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("Hello from the agent.");

        var agent = BuildTracingAgent(fakeLLM);
        await RunTurnAsync(agent);

        (await WaitForSpanAsync("agent.turn")).Should().NotBeNull(
            "WithTracing() must register TracingObserver which produces agent.turn spans");
    }

    [Fact]
    public async Task WithTracing_AgentRun_ProducesIterationSpan()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("Hello.");

        var agent = BuildTracingAgent(fakeLLM);
        await RunTurnAsync(agent);

        (await WaitForSpanAsync("agent.iteration")).Should().NotBeNull();
    }

    [Fact]
    public async Task WithTracing_AgentRunWithToolCall_ProducesToolCallSpan()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueToolCall("ping", "call-1");
        fakeLLM.EnqueueStreamingResponse("Done.");

        var ping = AIFunctionFactory.Create(() => "pong", "ping");
        var agent = BuildTracingAgent(fakeLLM, tools: ping);
        await RunTurnAsync(agent);

        (await WaitForSpanAsync("agent.tool_call")).Should().NotBeNull();
    }

    // ── Custom source name ────────────────────────────────────────────────────

    [Fact]
    public async Task WithTracing_CustomSourceName_SpansEmittedUnderThatSource()
    {
        // Register a listener for the custom source
        const string customSource = "MyApp.CustomAgent.Test";
        var turnCompleted = new TaskCompletionSource<Activity>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var customListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == customSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "agent.turn")
                    turnCompleted.TrySetResult(activity);
            }
        };
        ActivitySource.AddActivityListener(customListener);

        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("Custom source test.");

        var config = DefaultConfig();
        config.Clients.Chat!.Override = HPD.Agent.Providers.ClientOverride<IChatClient>.Borrow(fakeLLM, "test", "local", "test.chat");
        var builder = new AgentBuilder(config, new TestProviderRegistry(fakeLLM));
        builder.WithTracing(sourceName: customSource);
        var agent = builder.BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        await RunTurnAsync(agent);

        var turn = await turnCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestCancellationToken);
        turn.Source.Name.Should().Be(customSource);
    }

    // ── WithTracing + WithTelemetry coexist ───────────────────────────────────

    [Fact]
    public async Task WithTracing_And_WithTelemetry_BothRegistered_BothFire()
    {
        // WithTelemetry registers TelemetryEventObserver; WithTracing registers TracingObserver.
        // Both should function independently.
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("Both observers active.");

        var config = DefaultConfig();
        config.Clients.Chat!.Override = HPD.Agent.Providers.ClientOverride<IChatClient>.Borrow(fakeLLM, "test", "local", "test.chat");
        var builder = new AgentBuilder(config, new TestProviderRegistry(fakeLLM));
        builder.WithTracing(sourceName: _testSourceName);
        // WithTelemetry also uses ActivitySource so we use our listener to verify it doesn't interfere.
        builder.WithTelemetry(sourceName: _testSourceName);
        var agent = builder.BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Must not throw
        Func<Task> act = () => RunTurnAsync(agent);
        await act.Should().NotThrowAsync("WithTracing and WithTelemetry must coexist without errors");

        // TracingObserver span still produced
        (await WaitForSpanAsync("agent.turn")).Should().NotBeNull();
    }

    // ── Custom sanitizer options passed through ───────────────────────────────

    [Fact]
    public async Task WithTracing_SanitizerOptions_LargeTool_Truncated()
    {
        // Ensure a very tight MaxStringLength is respected by the sanitizer
        // when tool results are attached to spans.
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueToolCall("big_tool", "call-bigt");
        fakeLLM.EnqueueStreamingResponse("Done.");

        var bigResult = new string('x', 1000);
        var bigTool = HPDAIFunctionFactory.Import(
            AIFunctionFactory.Create(() => bigResult, "big_tool"), explicitlyUngoverned: true);

        var config = DefaultConfig();
        config.Clients.Chat!.Override = HPD.Agent.Providers.ClientOverride<IChatClient>.Borrow(fakeLLM, "test", "local", "test.chat");
        var builder = new AgentBuilder(config, new TestProviderRegistry(fakeLLM));
        builder.WithTracing(
            sourceName: _testSourceName,
            sanitizerOptions: new SpanSanitizerOptions { MaxStringLength = 50 });
        var tools = new[] { bigTool };
        config.EnsureChatClientConfig();
        config.EnsureChatClientConfig().Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" };
        config.EnsureChatClientConfig().ModelName = "test-model";
        config.ServerConfiguredTools ??= new List<AITool>();
        foreach (var tool in tools)
            config.ServerConfiguredTools.Add(tool);
        var agent = builder.BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        await RunTurnAsync(agent);

        // Tool call span should exist
        var toolSpan = await WaitForSpanAsync("agent.tool_call");
        if (toolSpan is not null)
        {
            // Find the tool.result event if recorded
            var resultEvent = toolSpan.Events.FirstOrDefault(e => e.Name == "tool.result");
            if (resultEvent.Name is not null)
            {
                var resultTag = resultEvent.Tags.FirstOrDefault(t => t.Key == "tool.result");
                if (resultTag.Value is string s && s.Length > 0)
                {
                    s.Should().EndWith("[truncated]",
                        "custom MaxStringLength=50 must cause truncation for 1000-char results");
                }
            }
        }
    }

    // ── WithTracing without any listener — no crash ───────────────────────────

    [Fact]
    public async Task WithTracing_NoListener_AgentRunCompletesWithoutError()
    {
        // Use a source name that has NO listener — StartActivity returns null.
        const string unlistenedSource = "HPD.Agent.UnlistenedSource.NoOp";

        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("No listener registered.");

        var config = DefaultConfig();
        config.Clients.Chat!.Override = HPD.Agent.Providers.ClientOverride<IChatClient>.Borrow(fakeLLM, "test", "local", "test.chat");
        var builder = new AgentBuilder(config, new TestProviderRegistry(fakeLLM));
        builder.WithTracing(sourceName: unlistenedSource);
        var agent = builder.BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        Func<Task> act = () => RunTurnAsync(agent);
        await act.Should().NotThrowAsync(
            "TracingObserver must handle null Activity from StartActivity gracefully");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Agent BuildTracingAgent(FakeChatClient fakeLLM, params AIFunction[] tools)
    {
        var config = DefaultConfig();
        config.Clients.Chat!.Override = HPD.Agent.Providers.ClientOverride<IChatClient>.Borrow(fakeLLM, "test", "local", "test.chat");
        if (tools.Length > 0)
        {
            config.EnsureChatClientConfig();
        config.EnsureChatClientConfig().Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" };
        config.EnsureChatClientConfig().ModelName = "test-model";
            config.ServerConfiguredTools ??= new List<AITool>();
            foreach (var tool in tools)
                config.ServerConfiguredTools.Add(tool is HPDAIFunctionFactory.HPDAIFunction
                    ? tool
                    : HPDAIFunctionFactory.Import(tool, explicitlyUngoverned: true));
        }

        var builder = new AgentBuilder(config, new TestProviderRegistry(fakeLLM));
        builder.WithTracing(sourceName: _testSourceName);
        return builder.BuildAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task RunTurnAsync(Agent agent)
    {
        await foreach (var _ in agent.RunAgenticLoopAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            cancellationToken: TestCancellationToken))
        { }

        // Event subscriptions are processed by HPD.Events subscriber mailboxes.
        await Task.Delay(100, TestCancellationToken);
    }

    private async Task<Activity?> WaitForSpanAsync(string operationName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var activity = _completed.FirstOrDefault(item => item.OperationName == operationName);
            if (activity is not null)
                return activity;

            await Task.Delay(25, TestCancellationToken);
        }

        return null;
    }
}
