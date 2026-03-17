using HPD.RAG.Core.DTOs;
using HPD.RAG.Evaluation;
using HPD.RAG.Evaluation.Handlers;
using HPD.RAG.Handlers.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Evaluation;

/// <summary>
/// Test T-116 — WriteEvalResultHandler stores metrics via IEvaluationResultStore.
/// </summary>
public sealed class WriteEvalResultHandlerTests
{
    [Fact] // T-116
    public async Task WriteEvalResultHandler_StoresMetrics_ViaMockStore()
    {
        var mockStore = new FakeEvaluationResultStore();

        var services = new ServiceCollection();
        services.AddSingleton<IEvaluationResultStore>(mockStore);
        var ctx = HandlerTestContext.Create(services);

        var handler = new WriteEvalResultHandler();
        var metrics = new MragMetricsDto
        {
            Scores = new Dictionary<string, double> { ["relevance"] = 0.85 }
        };

        var output = await handler.ExecuteAsync(
            Scenario: "qa-finance",
            Iteration: "iter-001",
            Execution: "run-42",
            Metrics: metrics,
            context: ctx);

        Assert.True(output.Stored);
        Assert.Equal(1, mockStore.CallCount);
        Assert.Equal("qa-finance", mockStore.LastScenario);
        Assert.Equal("iter-001", mockStore.LastIteration);
        Assert.Equal("run-42", mockStore.LastExecution);
        Assert.NotNull(mockStore.LastMetrics);
        Assert.Equal(0.85, mockStore.LastMetrics!.Scores["relevance"], precision: 6);
    }

    [Fact]
    public async Task WriteEvalResultHandler_NoStoreRegistered_FallsBackToLogging()
    {
        // No IEvaluationResultStore registered → handler logs and returns Stored = false
        var ctx = HandlerTestContext.Create();

        var handler = new WriteEvalResultHandler();
        var metrics = new MragMetricsDto
        {
            Scores = new Dictionary<string, double> { ["bleu"] = 0.42 }
        };

        var output = await handler.ExecuteAsync(
            Scenario: "s1",
            Iteration: "i1",
            Execution: "e1",
            Metrics: metrics,
            context: ctx);

        Assert.False(output.Stored);
    }

    // -----------------------------------------------------------------------
    // Fake store
    // -----------------------------------------------------------------------

    private sealed class FakeEvaluationResultStore : IEvaluationResultStore
    {
        public int CallCount { get; private set; }
        public string? LastScenario { get; private set; }
        public string? LastIteration { get; private set; }
        public string? LastExecution { get; private set; }
        public MragMetricsDto? LastMetrics { get; private set; }

        public Task StoreAsync(
            string scenario,
            string iteration,
            string execution,
            MragMetricsDto metrics,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastScenario = scenario;
            LastIteration = iteration;
            LastExecution = execution;
            LastMetrics = metrics;
            return Task.CompletedTask;
        }
    }
}
