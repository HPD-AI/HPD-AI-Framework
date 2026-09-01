// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent.Evaluations.Tests.Infrastructure;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Tests.Integration;

/// <summary>
/// Tests for AgentBuilderEvalExtensions — the fluent API that registers
/// LiveEvaluationMiddleware on AgentBuilder.
///
/// Key behaviors:
/// 1. AddEvaluator registers LiveEvaluationMiddleware in builder.Middlewares.
/// 2. All AddEvaluator calls share ONE LiveEvaluationMiddleware instance.
/// 3. UseScoreStore injects store onto the middleware.
/// 4. Judge configuration is attached to evaluator registrations or the run aggregate.
/// 5. Multiple evaluators → all tracked on single middleware instance.
/// 6. LiveEvaluationMiddleware is also registered as an HPD.Events subscription (verified indirectly).
/// </summary>
public sealed class AgentBuilderEvalExtensionsTests
{
    private static AgentBuilder MakeBuilder() =>
        new(new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "Test",
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = TestProviderSelections.Anonymous(), ModelName = "test-model" } },
        },
        new StubProviderRegistry());

    // ── AddEvaluator ──────────────────────────────────────────────────────────

    [Fact]
    public void AddEvaluator_RegistersLiveEvaluationMiddleware()
    {
        var builder = MakeBuilder();

        builder.AddEvaluator(new StubDeterministicEvaluator("Score"));

        builder.Middlewares.OfType<LiveEvaluationMiddleware>()
            .Should().ContainSingle("AddEvaluator must register exactly one LiveEvaluationMiddleware");
    }

    [Fact]
    public void AddEvaluator_TwiceSameBuilder_SingleMiddlewareInstance()
    {
        var builder = MakeBuilder();

        builder
            .AddEvaluator(new StubDeterministicEvaluator("Score1"))
            .AddEvaluator(new StubDeterministicEvaluator("Score2"));

        builder.Middlewares.OfType<LiveEvaluationMiddleware>()
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
        var middleware = builder.Middlewares.OfType<LiveEvaluationMiddleware>().Single();
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

    [Fact]
    public void AddEvaluator_RegistersLiveEvaluationMiddlewareOutermost()
    {
        var builder = MakeBuilder();
        var existing = new NoopMiddleware();
        builder.WithMiddleware(existing);

        builder.AddEvaluator(new StubDeterministicEvaluator("Score"));

        builder.Middlewares[0].Should().BeOfType<LiveEvaluationMiddleware>(
            "evaluation must run first for Before* hooks and last for After* hooks");
        builder.Middlewares[1].Should().BeSameAs(existing);
    }

    [Fact]
    public void UseScoreStore_RePinsExistingLiveEvaluationMiddlewareOutermost()
    {
        var builder = MakeBuilder();
        builder.AddEvaluator(new StubDeterministicEvaluator("Score"));
        var insertedLater = new NoopMiddleware();
        builder.Middlewares.Insert(0, insertedLater);

        builder.UseScoreStore(new InMemoryScoreStore());

        builder.Middlewares[0].Should().BeOfType<LiveEvaluationMiddleware>(
            "subsequent eval configuration should preserve final AfterMessageTurn ordering");
        builder.Middlewares[1].Should().BeSameAs(insertedLater);
    }

    // ── UseScoreStore ─────────────────────────────────────────────────────────

    [Fact]
    public void UseScoreStore_SetsStoreOnMiddleware()
    {
        var builder = MakeBuilder();
        var store = new InMemoryScoreStore();

        builder.AddEvaluator(new StubDeterministicEvaluator("Score")).UseScoreStore(store);

        var middleware = builder.Middlewares.OfType<LiveEvaluationMiddleware>().Single();
        middleware.ScoreStore.Should().BeSameAs(store);
    }

    [Fact]
    public void UseScoreStore_WithoutPriorAddEvaluator_CreatesMiddleware()
    {
        // UseScoreStore alone should still create the shared middleware instance
        var builder = MakeBuilder();
        var store = new InMemoryScoreStore();

        builder.UseScoreStore(store);

        var middleware = builder.Middlewares.OfType<LiveEvaluationMiddleware>().Single();
        middleware.ScoreStore.Should().BeSameAs(store);
    }

    [Fact]
    public void UseScoreStore_ReturnsBuilderForChaining()
    {
        var builder = MakeBuilder();
        var result = builder.UseScoreStore(new InMemoryScoreStore());
        result.Should().BeSameAs(builder);
    }

    // ── Sampling / policy stored correctly ────────────────────────────────────

    [Fact]
    public void AddEvaluator_SamplingRate_StoredOnRegistration()
    {
        var builder = MakeBuilder();
        builder.AddEvaluator(new StubDeterministicEvaluator("Score"), samplingRate: 0.5);

        var middleware = builder.Middlewares.OfType<LiveEvaluationMiddleware>().Single();
        var regs = GetRegistrations(middleware);
        regs.Single().SamplingRate.Should().Be(0.5);
    }

    [Fact]
    public void AddEvaluator_Policy_StoredOnRegistration()
    {
        var builder = MakeBuilder();
        builder.AddEvaluator(new StubDeterministicEvaluator("Score"), policy: EvalPolicy.TrackTrend);

        var middleware = builder.Middlewares.OfType<LiveEvaluationMiddleware>().Single();
        var regs = GetRegistrations(middleware);
        regs.Single().Policy.Should().Be(EvalPolicy.TrackTrend);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads LiveEvaluationMiddleware._evaluators via reflection (it's internal/private).
    /// </summary>
    private static IReadOnlyList<EvaluatorRegistration> GetRegistrations(LiveEvaluationMiddleware middleware)
    {
        var field = typeof(LiveEvaluationMiddleware)
            .GetField("_evaluators", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull("_evaluators field must exist on LiveEvaluationMiddleware");

        var list = field!.GetValue(middleware) as System.Collections.IEnumerable;
        return list!.Cast<EvaluatorRegistration>().ToList();
    }


    private sealed class NoopMiddleware : IAgentMiddleware;
}
