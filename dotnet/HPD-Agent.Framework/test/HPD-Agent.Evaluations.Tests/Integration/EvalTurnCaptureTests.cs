using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Tests.Integration;

public sealed class EvalTurnCaptureTests
{
    [Fact]
    public async Task TerminalBeforePrepare_CompletesOnceWithTerminalFacts()
    {
        var (before, after) = CreateContexts("turn-1", "trace-1");
        var capture = new EvalTurnCapture();
        capture.Begin(before);
        var terminal = Finished("turn-1", "trace-1", TimeSpan.FromSeconds(2));

        await capture.HandleAsync(terminal);
        TurnEvaluationContext? completed = null;
        var completions = 0;
        capture.Prepare(after, context => { completed = context; completions++; });
        await capture.HandleAsync(terminal);

        Assert.Equal(1, completions);
        Assert.NotNull(completed);
        Assert.Equal(TimeSpan.FromSeconds(2), completed.Duration);
        Assert.NotSame(terminal.Usage, completed.MessageTurnUsage);
    }

    [Fact]
    public async Task ConcurrentCaptures_AreCompletedByExactTurnIdentity()
    {
        var first = CreateContexts("turn-1", "trace-shared");
        var second = CreateContexts("turn-2", "trace-shared");
        var capture = new EvalTurnCapture();
        capture.Begin(first.Before);
        capture.Begin(second.Before);
        TurnEvaluationContext? firstResult = null;
        TurnEvaluationContext? secondResult = null;
        capture.Prepare(first.After, value => firstResult = value);
        capture.Prepare(second.After, value => secondResult = value);

        await capture.HandleAsync(Finished("turn-2", "trace-shared", TimeSpan.FromSeconds(2)));
        Assert.Null(firstResult);
        Assert.NotNull(secondResult);
        await capture.HandleAsync(Finished("turn-1", "trace-shared", TimeSpan.FromSeconds(1)));
        Assert.NotNull(firstResult);
        Assert.Equal(TimeSpan.FromSeconds(1), firstResult!.Duration);
    }

    [Fact]
    public void EndInputScope_ClearsAmbientEvaluationDataOnSuccessPath()
    {
        var contexts = CreateContexts("turn-1", "trace-1");
        var capture = new EvalTurnCapture();
        capture.Begin(contexts.Before);
        EvalContext.SetAttribute("before", true);

        capture.EndInputScope();
        EvalContext.SetAttribute("after", true);

        // A fresh activation proves the previous AsyncLocal value is no longer selected.
        var fresh = EvalContext.Activate();
        Assert.DoesNotContain("before", fresh.Attributes.Keys);
        Assert.DoesNotContain("after", fresh.Attributes.Keys);
        EvalContext.Deactivate();
    }

    [Fact]
    public async Task ErrorTerminal_FailsAndRemovesExactCapture()
    {
        var contexts = CreateContexts("turn-1", "trace-1");
        var capture = new EvalTurnCapture();
        capture.Begin(contexts.Before);
        Exception? failure = null;
        capture.Prepare(contexts.After, _ => throw new Xunit.Sdk.XunitException("must not complete"), error => failure = error);

        await capture.HandleAsync(new MessageTurnErrorEvent("turn-1", "failed", MessageTurnUsageSummary.Empty)
        {
            TraceId = "trace-1"
        });

        Assert.NotNull(failure);
    }

    [Fact]
    public async Task Prepare_DeepSnapshotsMutableEvaluatorInputs()
    {
        var contexts = CreateContexts("turn-1", "trace-1");
        var bytes = new byte[] { 1, 2, 3 };
        contexts.After.FinalResponse.Messages[0].Contents.Add(new DataContent(bytes, "application/octet-stream"));
        var responseUsage = new UsageDetails { InputTokenCount = 7 };
        contexts.After.FinalResponse.Usage = responseUsage;
        var nested = new Dictionary<string, object?> { ["value"] = new List<object?> { "original" } };

        var capture = new EvalTurnCapture();
        capture.Begin(contexts.Before);
        EvalContext.SetAttribute("nested", nested);
        TurnEvaluationContext? completed = null;
        capture.Prepare(contexts.After, value => completed = value);

        bytes[0] = 99;
        responseUsage.InputTokenCount = 99;
        ((List<object?>)nested["value"]!).Add("mutated");
        await capture.HandleAsync(Finished("turn-1", "trace-1", TimeSpan.FromSeconds(1)));

        Assert.NotNull(completed);
        Assert.Equal(7, completed.FinalResponse.Usage!.InputTokenCount);
        Assert.Equal(new byte[] { 1, 2, 3 }, completed.FinalResponse.Messages[0].Contents.OfType<DataContent>().Single().Data.ToArray());
        var capturedNested = Assert.IsType<Dictionary<string, object?>>(completed.Attributes["nested"]);
        Assert.Single(Assert.IsType<object?[]>(capturedNested["value"]));
    }

    private static (BeforeMessageTurnContext Before, AfterMessageTurnContext After) CreateContexts(
        string turnId,
        string traceId)
    {
        var state = AgentLoopState.InitialSafe([], turnId, "conversation", "Agent");
        var agentContext = new AgentContext(
            "Agent",
            "conversation",
            state,
            new EventCoordinator(),
            new Session("session"),
            new Thread("session", "Agent"),
            CancellationToken.None,
            traceId: traceId,
            config: new AgentConfig { Name = "Agent" });
        var runConfig = new AgentRunConfig();
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "hello") { MessageId = "user-1" },
            new(ChatRole.Assistant, "world") { MessageId = "assistant-1" }
        };
        return (
            agentContext.AsBeforeMessageTurn([history[0]], history, runConfig),
            agentContext.AsAfterMessageTurn(new ChatResponse([history[1]]), history, runConfig));
    }

    private static MessageTurnFinishedEvent Finished(string turnId, string traceId, TimeSpan duration)
        => new(turnId, "conversation", "agent-id", "Agent", duration, MessageTurnUsageSummary.Empty)
        {
            TraceId = traceId
        };
}
