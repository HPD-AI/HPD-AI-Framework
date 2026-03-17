// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;

namespace HPD.Agent.Evaluations.Tests.Integration;

/// <summary>
/// Tests for EvaluationMiddleware flags and direct API.
///
/// Key behaviors tested:
/// 1. DisableEvaluators = true → evaluator NOT called (agent run, flag fires before context building).
/// 2. IsInternalEvalJudgeCall = true → evaluator NOT called.
/// 3. EvaluationMiddleware.ScoreStore property is readable/writable.
/// 4. EvaluationMiddleware.AddEvaluator stores registrations with correct metadata.
///
/// Note: The happy-path "evaluator IS called" flow is implicitly covered by the
/// RetroactiveScorerTests which call evaluators through the full scoring pipeline.
/// The fire-and-forget path (AfterMessageTurnAsync → Task.Run) requires a fully
/// wired production agent with session/branch/context building — not tested here.
/// </summary>
public sealed class EvaluationMiddlewareFlagTests
{
    private static async Task RunAgentAsync(Agent agent, string input, AgentRunConfig? runConfig = null)
    {
        await foreach (var _ in agent.RunAsync(input, options: runConfig ?? new AgentRunConfig())) { }
    }

    private static AgentConfig MakeConfig(string name = "FlagTestAgent") => new()
    {
        Name = name,
        SystemInstructions = "You are a test agent.",
        MaxAgenticIterations = 3,
        Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" },
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

    // ── EvaluationMiddleware direct API ───────────────────────────────────────

    [Fact]
    public void EvaluationMiddleware_ScoreStore_PropertySetAndReadable()
    {
        var store = new InMemoryScoreStore();
        var middleware = new EvaluationMiddleware();

        middleware.ScoreStore = store;

        middleware.ScoreStore.Should().BeSameAs(store);
    }

    [Fact]
    public void EvaluationMiddleware_GlobalJudgeConfig_PropertySetAndReadable()
    {
        var config = new EvalJudgeConfig { TimeoutSeconds = 45 };
        var middleware = new EvaluationMiddleware();

        middleware.GlobalJudgeConfig = config;

        middleware.GlobalJudgeConfig.Should().BeSameAs(config);
        middleware.GlobalJudgeConfig!.TimeoutSeconds.Should().Be(45);
    }

    [Fact]
    public void EvaluationMiddleware_AddEvaluator_RegistrationsStoredCorrectly()
    {
        var middleware = new EvaluationMiddleware();
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
    public void EvaluationMiddleware_IsIAgentMiddleware_AndIAgentEventObserver()
    {
        var middleware = new EvaluationMiddleware();

        middleware.Should().BeAssignableTo<IAgentMiddleware>();
        middleware.Should().BeAssignableTo<IAgentEventObserver>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<EvaluatorRegistration> GetRegistrations(EvaluationMiddleware middleware)
    {
        var field = typeof(EvaluationMiddleware)
            .GetField("_evaluators", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (field!.GetValue(middleware) as System.Collections.IEnumerable)!;
        return list.Cast<EvaluatorRegistration>().ToList();
    }
}
