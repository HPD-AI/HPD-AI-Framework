// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent.Evaluations.Tests.Infrastructure;
using HPD.Agent.Providers;

namespace HPD.Agent.Evaluations.Tests.Integration;

/// <summary>
/// Tests for AgentBuilderEvalExtensions — the fluent API that registers
/// EvaluationMiddleware on AgentBuilder.
///
/// Key behaviors:
/// 1. AddEvaluator registers EvaluationMiddleware in builder.Middlewares.
/// 2. All AddEvaluator calls share ONE EvaluationMiddleware instance.
/// 3. UseScoreStore injects store onto the middleware.
/// 4. UseEvalJudgeConfig injects judge config onto the middleware.
/// 5. Multiple evaluators → all tracked on single middleware instance.
/// 6. EvaluationMiddleware is also registered as IAgentEventObserver (verified indirectly).
/// </summary>
public sealed class AgentBuilderEvalExtensionsTests
{
    private static AgentBuilder MakeBuilder() =>
        new(new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "Test",
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" },
        },
        new StubProviderRegistry());

    // ── AddEvaluator ──────────────────────────────────────────────────────────

    [Fact]
    public void AddEvaluator_RegistersEvaluationMiddleware()
    {
        var builder = MakeBuilder();

        builder.AddEvaluator(new StubDeterministicEvaluator("Score"));

        builder.Middlewares.OfType<EvaluationMiddleware>()
            .Should().ContainSingle("AddEvaluator must register exactly one EvaluationMiddleware");
    }

    [Fact]
    public void AddEvaluator_TwiceSameBuilder_SingleMiddlewareInstance()
    {
        var builder = MakeBuilder();

        builder
            .AddEvaluator(new StubDeterministicEvaluator("Score1"))
            .AddEvaluator(new StubDeterministicEvaluator("Score2"));

        builder.Middlewares.OfType<EvaluationMiddleware>()
            .Should().ContainSingle("multiple AddEvaluator calls must share ONE middleware");
    }

    [Fact]
    public void AddEvaluator_ThreeTimes_AllEvaluatorsRegistered()
    {
        var builder = MakeBuilder();
        var ev1 = new StubDeterministicEvaluator("A");
        var ev2 = new StubDeterministicEvaluator("B");
        var ev3 = new StubDeterministicEvaluator("C");

        builder.AddEvaluator(ev1).AddEvaluator(ev2).AddEvaluator(ev3);

        // The middleware holds all three — verify by inspecting its internal state via reflection
        var middleware = builder.Middlewares.OfType<EvaluationMiddleware>().Single();
        var registrations = GetRegistrations(middleware);
        registrations.Should().HaveCount(3, "all three evaluators must be tracked");
    }

    [Fact]
    public void AddEvaluator_ReturnsBuilderForChaining()
    {
        var builder = MakeBuilder();
        var result = builder.AddEvaluator(new StubDeterministicEvaluator("X"));

        result.Should().BeSameAs(builder);
    }

    // ── UseScoreStore ─────────────────────────────────────────────────────────

    [Fact]
    public void UseScoreStore_SetsStoreOnMiddleware()
    {
        var builder = MakeBuilder();
        var store = new InMemoryScoreStore();

        builder.AddEvaluator(new StubDeterministicEvaluator("Score")).UseScoreStore(store);

        var middleware = builder.Middlewares.OfType<EvaluationMiddleware>().Single();
        middleware.ScoreStore.Should().BeSameAs(store);
    }

    [Fact]
    public void UseScoreStore_WithoutPriorAddEvaluator_CreatesMiddleware()
    {
        // UseScoreStore alone should still create the shared middleware instance
        var builder = MakeBuilder();
        var store = new InMemoryScoreStore();

        builder.UseScoreStore(store);

        var middleware = builder.Middlewares.OfType<EvaluationMiddleware>().Single();
        middleware.ScoreStore.Should().BeSameAs(store);
    }

    [Fact]
    public void UseScoreStore_ReturnsBuilderForChaining()
    {
        var builder = MakeBuilder();
        var result = builder.UseScoreStore(new InMemoryScoreStore());
        result.Should().BeSameAs(builder);
    }

    // ── UseEvalJudgeConfig ────────────────────────────────────────────────────

    [Fact]
    public void UseEvalJudgeConfig_SetsConfigOnMiddleware()
    {
        var builder = MakeBuilder();
        var judgeConfig = new EvalJudgeConfig { TimeoutSeconds = 60 };

        builder.AddEvaluator(new StubDeterministicEvaluator("Score")).UseEvalJudgeConfig(judgeConfig);

        var middleware = builder.Middlewares.OfType<EvaluationMiddleware>().Single();
        middleware.GlobalJudgeConfig.Should().BeSameAs(judgeConfig);
    }

    [Fact]
    public void UseEvalJudgeConfig_ReturnsBuilderForChaining()
    {
        var builder = MakeBuilder();
        var result = builder.UseEvalJudgeConfig(new EvalJudgeConfig());
        result.Should().BeSameAs(builder);
    }

    // ── Sampling / policy stored correctly ────────────────────────────────────

    [Fact]
    public void AddEvaluator_SamplingRate_StoredOnRegistration()
    {
        var builder = MakeBuilder();
        builder.AddEvaluator(new StubDeterministicEvaluator("Score"), samplingRate: 0.5);

        var middleware = builder.Middlewares.OfType<EvaluationMiddleware>().Single();
        var regs = GetRegistrations(middleware);
        regs.Single().SamplingRate.Should().Be(0.5);
    }

    [Fact]
    public void AddEvaluator_Policy_StoredOnRegistration()
    {
        var builder = MakeBuilder();
        builder.AddEvaluator(new StubDeterministicEvaluator("Score"), policy: EvalPolicy.TrackTrend);

        var middleware = builder.Middlewares.OfType<EvaluationMiddleware>().Single();
        var regs = GetRegistrations(middleware);
        regs.Single().Policy.Should().Be(EvalPolicy.TrackTrend);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads EvaluationMiddleware._evaluators via reflection (it's internal/private).
    /// </summary>
    private static IReadOnlyList<EvaluatorRegistration> GetRegistrations(EvaluationMiddleware middleware)
    {
        var field = typeof(EvaluationMiddleware)
            .GetField("_evaluators", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull("_evaluators field must exist on EvaluationMiddleware");

        var list = field!.GetValue(middleware) as System.Collections.IEnumerable;
        return list!.Cast<EvaluatorRegistration>().ToList();
    }
}
