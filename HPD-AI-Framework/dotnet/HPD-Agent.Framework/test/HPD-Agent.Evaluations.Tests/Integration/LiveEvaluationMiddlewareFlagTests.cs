// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Evaluators.LlmJudge;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent.Evaluations.Tests.Infrastructure;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;

namespace HPD.Agent.Evaluations.Tests.Integration;

/// <summary>
/// Tests for LiveEvaluationMiddleware flags and direct API.
///
/// Key behaviors tested:
/// 1. DisableEvaluators = true → evaluator NOT called (agent run, flag fires before context building).
/// 2. IsInternalEvalJudgeCall = true → evaluator NOT called.
/// 3. LiveEvaluationMiddleware.ScoreStore property is readable/writable.
/// 4. LiveEvaluationMiddleware.AddEvaluator stores registrations with correct metadata.
///
/// Note: The happy-path "evaluator IS called" flow is implicitly covered by the
/// RetroactiveScorerTests which call evaluators through the full scoring pipeline.
/// The fire-and-forget path (AfterMessageTurnAsync → Task.Run) requires a fully
/// wired production agent with session/thread/context building — not tested here.
/// </summary>
public sealed class LiveEvaluationMiddlewareFlagTests
{
    private static async Task RunAgentAsync(Agent agent, string input, AgentRunConfig? runConfig = null)
    {
        await agent.RunAsync(input, runConfig: runConfig ?? new AgentRunConfig());
    }

    private static AgentConfig MakeConfig(string name = "FlagTestAgent") => new()
    {
        Name = name,
        SystemInstructions = "You are a test agent.",
        MaxAgenticIterations = 3,
        Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" } },
        AgenticLoop = new AgenticLoopConfig { MaxTurnDuration = TimeSpan.FromSeconds(10) },
    };

    // ── Flag: DisableEvaluators ───────────────────────────────────────────────

    [Fact]
    public async Task DisableEvaluators_EvaluatorNotCalled()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var evaluator = new StubDeterministicEvaluator("FlagTest");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder.AddEvaluator(evaluator);
        var agent = await builder.BuildAsync(CancellationToken.None);

        await RunAgentAsync(agent, "Hello", new AgentRunConfig { DisableEvaluators = true });

        await Task.Delay(100);
        evaluator.Calls.Should().Be(0, "DisableEvaluators=true must skip all evaluators");
    }

    // ── Flag: IsInternalEvalJudgeCall ─────────────────────────────────────────

    [Fact]
    public async Task IsInternalEvalJudgeCall_EvaluatorNotCalled()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var evaluator = new StubDeterministicEvaluator("FlagTest");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder.AddEvaluator(evaluator);
        var agent = await builder.BuildAsync(CancellationToken.None);

        await RunAgentAsync(agent, "Hello", new AgentRunConfig { IsInternalEvalJudgeCall = true });

        await Task.Delay(100);
        evaluator.Calls.Should().Be(0, "IsInternalEvalJudgeCall=true must skip all evaluators");
    }

    [Fact]
    public async Task AdditionalEvaluators_RunForSingleAgentRun()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var registered = new StubDeterministicEvaluator("Registered");
        var additional = new StubDeterministicEvaluator("Additional");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder.AddEvaluator(registered, samplingRate: 0.0);
        var agent = await builder.BuildAsync(CancellationToken.None);

        var runConfig = new AgentRunConfig()
            .WithAdditionalEvaluators(additional);

        await RunAgentAsync(agent, "Hello", runConfig);

        await Task.Delay(200);
        registered.Calls.Should().Be(0);
        additional.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AdditionalEvaluators_RunAlongsideRegisteredEvaluators()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var registered = new StubDeterministicEvaluator("Registered");
        var additional = new StubDeterministicEvaluator("Additional");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder.AddEvaluator(registered, samplingRate: 1.0);
        var agent = await builder.BuildAsync(CancellationToken.None);

        var runConfig = new AgentRunConfig()
            .WithAdditionalEvaluators(additional);

        await RunAgentAsync(agent, "Hello", runConfig);

        await Task.Delay(200);
        registered.Calls.Should().Be(1);
        additional.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AdditionalEvaluators_WrongObjectType_IgnoredWithoutCrashing()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var registered = new StubDeterministicEvaluator("Registered");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder.AddEvaluator(registered, samplingRate: 0.0);
        var agent = await builder.BuildAsync(CancellationToken.None);

        await RunAgentAsync(agent, "Hello", new AgentRunConfig
        {
            AdditionalEvaluators = [new object()],
        });

        await Task.Delay(200);
        registered.Calls.Should().Be(0);
    }

    [Fact]
    public async Task EvaluatorSamplingOverride_ForcesRegisteredEvaluatorToRun()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var evaluator = new StubDeterministicEvaluator("SamplingOverride");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder.AddEvaluator(evaluator, samplingRate: 0.0);
        var agent = await builder.BuildAsync(CancellationToken.None);

        var runConfig = new AgentRunConfig()
            .WithEvaluatorSamplingOverride(1.0);

        await RunAgentAsync(agent, "Hello", runConfig);

        await Task.Delay(200);
        evaluator.Calls.Should().Be(1);
    }

    [Fact]
    public async Task EvaluatorSamplingOverride_ZeroSuppressesRegisteredEvaluator()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var evaluator = new StubDeterministicEvaluator("SamplingOverride");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder.AddEvaluator(evaluator, samplingRate: 1.0);
        var agent = await builder.BuildAsync(CancellationToken.None);

        var runConfig = new AgentRunConfig()
            .WithEvaluatorSamplingOverride(0.0);

        await RunAgentAsync(agent, "Hello", runConfig);

        await Task.Delay(200);
        evaluator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DisableEvaluators_SuppressesRegisteredAndAdditionalEvaluators()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var registered = new StubDeterministicEvaluator("Registered");
        var additional = new StubDeterministicEvaluator("Additional");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder.AddEvaluator(registered, samplingRate: 1.0);
        var agent = await builder.BuildAsync(CancellationToken.None);

        var runConfig = new AgentRunConfig { DisableEvaluators = true }
            .WithAdditionalEvaluators(additional);

        await RunAgentAsync(agent, "Hello", runConfig);

        await Task.Delay(200);
        registered.Calls.Should().Be(0);
        additional.Calls.Should().Be(0);
    }

    [Fact]
    public async Task EvalJudgeConfigOverride_OverridesGlobalJudgeForRun()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var globalJudge = new FakeJudgeChatClient();
        var runJudge = new FakeJudgeChatClient();
        runJudge.EnqueueResponse("<S0>ok</S0><S1>run override</S1><S2>true</S2>");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder
            .AddEvaluator(new AspectCriticEvaluator("passes"), policy: EvalPolicy.TrackTrend)
            .UseEvalJudgeConfig(new EvalJudgeConfig { OverrideChatClient = globalJudge });
        var agent = await builder.BuildAsync(CancellationToken.None);

        var runConfig = new AgentRunConfig()
            .WithEvalJudgeConfigOverride(new EvalJudgeConfig { OverrideChatClient = runJudge });

        await RunAgentAsync(agent, "Hello", runConfig);

        await Task.Delay(200);
        runJudge.CallCount.Should().Be(1);
        globalJudge.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PerEvaluatorJudgeConfig_WinsOverRunAndGlobalJudgeConfig()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var perEvaluatorJudge = new FakeJudgeChatClient();
        var runJudge = new FakeJudgeChatClient();
        var globalJudge = new FakeJudgeChatClient();
        perEvaluatorJudge.EnqueueResponse("<S0>ok</S0><S1>per evaluator</S1><S2>true</S2>");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder
            .AddEvaluator(
                new AspectCriticEvaluator("passes"),
                policy: EvalPolicy.TrackTrend,
                judgeConfig: new EvalJudgeConfig { OverrideChatClient = perEvaluatorJudge })
            .UseEvalJudgeConfig(new EvalJudgeConfig { OverrideChatClient = globalJudge });
        var agent = await builder.BuildAsync(CancellationToken.None);

        var runConfig = new AgentRunConfig()
            .WithEvalJudgeConfigOverride(new EvalJudgeConfig { OverrideChatClient = runJudge });

        await RunAgentAsync(agent, "Hello", runConfig);

        await Task.Delay(200);
        perEvaluatorJudge.CallCount.Should().Be(1);
        runJudge.CallCount.Should().Be(0);
        globalJudge.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task OnlineScoring_PersistsJudgeCallsOnScoreRecord()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var store = new InMemoryScoreStore();
        var judge = new FakeJudgeChatClient();
        judge.EnqueueResponse("<S0>ok</S0><S1>online captured</S1><S2>true</S2>");

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder
            .UseScoreStore(store)
            .AddEvaluator(
                new AspectCriticEvaluator("passes"),
                policy: EvalPolicy.TrackTrend,
                judgeConfig: new EvalJudgeConfig { OverrideChatClient = judge });
        var agent = await builder.BuildAsync(CancellationToken.None);

        await RunAgentAsync(agent, "Hello");

        var scores = await WaitForScoresAsync(store, nameof(AspectCriticEvaluator));
        var score = scores.Should().ContainSingle().Which;
        var call = score.JudgeCalls.Should().ContainSingle().Which;
        call.EvaluatorName.Should().Be(nameof(AspectCriticEvaluator));
        call.Phase.Should().Be("judge");
        call.Succeeded.Should().BeTrue();
        call.Response!.Text.Should().Contain("<S2>true</S2>");
    }

    // ── LiveEvaluationMiddleware direct API ───────────────────────────────────────

    [Fact]
    public void LiveEvaluationMiddleware_ScoreStore_PropertySetAndReadable()
    {
        var store = new InMemoryScoreStore();
        var middleware = new LiveEvaluationMiddleware();

        middleware.ScoreStore = store;

        middleware.ScoreStore.Should().BeSameAs(store);
    }

    [Fact]
    public void LiveEvaluationMiddleware_GlobalJudgeConfig_PropertySetAndReadable()
    {
        var config = new EvalJudgeConfig { TimeoutSeconds = 45 };
        var middleware = new LiveEvaluationMiddleware();

        middleware.GlobalJudgeConfig = config;

        middleware.GlobalJudgeConfig.Should().BeSameAs(config);
        middleware.GlobalJudgeConfig!.TimeoutSeconds.Should().Be(45);
    }

    [Fact]
    public void LiveEvaluationMiddleware_AddEvaluator_RegistrationsStoredCorrectly()
    {
        var middleware = new LiveEvaluationMiddleware();
        middleware.AddEvaluator(new StubDeterministicEvaluator("A"), 1.0, EvalPolicy.MustAlwaysPass, null);
        middleware.AddEvaluator(new StubDeterministicEvaluator("B"), 0.5, EvalPolicy.TrackTrend, null);

        var regs = GetRegistrations(middleware);

        regs.Should().HaveCount(2);
        regs[0].Policy.Should().Be(EvalPolicy.MustAlwaysPass);
        regs[0].SamplingRate.Should().Be(1.0);
        regs[1].SamplingRate.Should().Be(0.5);
        regs[1].Policy.Should().Be(EvalPolicy.TrackTrend);
    }

    [Fact]
    public void LiveEvaluationMiddleware_IsIAgentMiddleware_AndSubscriptionHandler()
    {
        var middleware = new LiveEvaluationMiddleware();

        middleware.Should().BeAssignableTo<IAgentMiddleware>();
        middleware.HandleAsync(new TextDeltaEvent("x", "message-1")).IsCompletedSuccessfully.Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<EvaluatorRegistration> GetRegistrations(LiveEvaluationMiddleware middleware)
    {
        var field = typeof(LiveEvaluationMiddleware)
            .GetField("_evaluators", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (field!.GetValue(middleware) as System.Collections.IEnumerable)!;
        return list.Cast<EvaluatorRegistration>().ToList();
    }

    private static async Task<List<ScoreRecord>> WaitForScoresAsync(
        IScoreStore store,
        string evaluatorName,
        int maxAttempts = 20)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var scores = new List<ScoreRecord>();
            await foreach (var score in store.GetScoresAsync(evaluatorName: evaluatorName))
                scores.Add(score);

            if (scores.Count > 0)
                return scores;

            await Task.Delay(50);
        }

        return [];
    }
}
