using System.Collections.Immutable;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

public sealed class ContainerHistoryRewriteTests
{
    [Theory]
    [InlineData(RecoveryType.HiddenItem)]
    [InlineData(RecoveryType.QualifiedName)]
    [InlineData(RecoveryType.ContainerWithArguments)]
    public async Task RewritePreservesMessageEnvelopeAndUnrelatedParallelContents(RecoveryType recovery)
    {
        var call = new FunctionCallContent("recover", "Hidden", new Dictionary<string, object?> { ["value"] = 3 });
        var other = new FunctionCallContent("other", "Visible");
        var result = new FunctionResultContent("recover", "recovery feedback");
        var otherResult = new FunctionResultContent("other", "real result");
        var created = DateTimeOffset.UtcNow;
        var assistant = new ChatMessage(ChatRole.Assistant, [new TextContent("before"), call, other])
        { MessageId = "assistant-id", CreatedAt = created, AuthorName = "worker", AdditionalProperties = new() { ["custom"] = "value" }, RawRepresentation = new object() };
        var tool = new ChatMessage(ChatRole.Tool, [result, otherResult])
        { MessageId = "tool-id", CreatedAt = created, AdditionalProperties = new() { ["custom"] = "result" }, RawRepresentation = new object() };
        var history = new List<ChatMessage> { assistant, tool };
        var state = AgentLoopState.InitialSafe([], "run", "session", "test");
        var context = new AgentContext("test", "session", state, new HPD.Events.Core.EventCoordinator(),
            new Session("session"), new HPD.Agent.Thread("session", "main"), default);
        var after = context.AsAfterMessageTurn(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")), history,
            new AgentRunConfig { Collapsing = new() { RecoveryHistoryMode = ContainerRecoveryHistoryMode.Rewrite } });
        after.UpdateMiddlewareState<ContainerMiddlewareState>(_ => new ContainerMiddlewareState()
            .WithRecoveredFunction("recover", new(recovery, "Harness", "Hidden"))
            .WithContainerInstructions("Harness", new("expanded", null)));
        await new ContainerMiddleware([], ImmutableHashSet<string>.Empty).AfterMessageTurnAsync(after, default);
        Assert.Equal(new[] { "assistant-id", "tool-id" }, history.Select(m => m.MessageId));
        Assert.All(history, m => { Assert.Equal(created, m.CreatedAt); Assert.Null(m.RawRepresentation); });
        Assert.Equal("worker", history[0].AuthorName);
        Assert.Equal("value", history[0].AdditionalProperties!["custom"]);
        Assert.Equal("result", history[1].AdditionalProperties!["custom"]);
        Assert.NotSame(assistant.AdditionalProperties, history[0].AdditionalProperties);
        Assert.Equal("Harness", history[0].Contents.OfType<FunctionCallContent>().First().Name);
        Assert.Equal("recover", history[0].Contents.OfType<FunctionCallContent>().First().CallId);
        Assert.Equal("expanded", history[1].Contents.OfType<FunctionResultContent>().First().Result);
        Assert.Same(other, history[0].Contents.Last());
        Assert.Same(otherResult, history[1].Contents.Last());
        Assert.Equal("Hidden", call.Name); // Original event payload remains unchanged.
    }

    [Theory]
    [InlineData("Hidden", false, false, false)]
    [InlineData("Harness.Hidden", false, false, false)]
    [InlineData("Harness", true, false, false)]
    [InlineData("Hidden", false, true, false)]
    [InlineData("Harness.Hidden", false, true, false)]
    [InlineData("Harness", true, true, false)]
    [InlineData("Hidden", false, true, true)]
    [InlineData("Harness.Hidden", false, true, true)]
    [InlineData("Harness", true, true, true)]
    public async Task RecoveredTurnFinalizesAndReplaysRewrittenPairs(string name, bool arguments, bool parallel, bool preserve)
    {
        var client = new FakeChatClient();
        var args = arguments ? new Dictionary<string, object?> { ["value"] = 3 } : new();
        if (parallel) client.EnqueueToolCalls((name, "recover", args), ("Visible", "visible", new()));
        else client.EnqueueToolCall(name, "recover", args);
        client.EnqueueTextResponse("done");
        var member = CollapsedToolHarnessTestHelper.CreateToolHarnessMemberFunction("Hidden", "member", (_, _) => Task.FromResult<object?>("real result"), "Harness");
        var container = CollapsedToolHarnessTestHelper.CreateContainerFunction("Harness", "container", [member]);
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "rewrite-test" })
            .WithEventComposition(HPD.Agent.Serialization.CoreAgentEventComposition.Instance)
            .WithChatClient(new IdentifiedClient(client)).WithNativeFunction(container).WithNativeFunction(member)
            .WithNativeFunction(HPDAIFunctionFactory.Create((_, _, _) => Task.FromResult<object?>("visible result"),
                new HPDAIFunctionFactoryOptions { Name = "Visible", Description = "Visible function" })).BuildAsync();
        await agent.CreateSessionAsync("rewrite-session");
        await agent.RunAsync(new UserMessagesInputEvent { Messages = [new(ChatRole.User, "work")], SessionId = "rewrite-session", ThreadId = "main",
            RunConfig = new() { Collapsing = new() { RecoveryHistoryMode = preserve ? ContainerRecoveryHistoryMode.Preserve : ContainerRecoveryHistoryMode.Rewrite } } });
        var store = agent.Config.SessionStore!;
        var events = (await store.CollectThreadEventsAsync(new("rewrite-session", "main")))!;
        Assert.Contains(events, e => e is MessageTurnFinishedEvent);
        Assert.DoesNotContain(events, e => e is MessageTurnErrorEvent);
        Assert.Equal(preserve ? 0 : 2, events.OfType<ThreadMessageReplacedEvent>().Count());
        var thread = await store.ProjectThreadAsync("rewrite-session", "main", ThreadProjectionPurpose.ThreadHistory);
        var rewritten = thread!.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single(c => c.CallId == "recover");
        if (!preserve)
        {
            Assert.Equal("Harness", rewritten.Name);
            Assert.True(rewritten.Arguments is null || rewritten.Arguments.Count == 0);
        }
        var resultEvents = events.OfType<ToolCallResultEvent>().ToArray();
        Assert.All(resultEvents, e => Assert.False(string.IsNullOrWhiteSpace(e.MessageId)));
        Assert.Single(resultEvents.Select(e => e.MessageId).Distinct());
        if (parallel)
        {
            var visible = thread.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single(c => c.CallId == "visible");
            Assert.Equal("Visible", visible.Name);
            Assert.Equal("visible result", thread.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single(c => c.CallId == "visible").Result?.ToString());
        }
        var output = thread.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single(c => c.CallId == "recover");
        if (!preserve) Assert.Contains("expanded", output.Result?.ToString());
    }

    private sealed class IdentifiedClient(FakeChatClient inner) : DelegatingChatClient(inner)
    {
        public override object? GetService(Type type, object? key = null)
            => type == typeof(HPD.Agent.Providers.ProviderClientExecutionIdentity)
                ? HPD.Agent.Providers.ProviderClientExecutionIdentity.CreateSafe("test", "test", HPD.Agent.Providers.ProviderClientFamily.Chat, "fake", "test/chat", "test/final")
                : base.GetService(type, key);
    }
}
