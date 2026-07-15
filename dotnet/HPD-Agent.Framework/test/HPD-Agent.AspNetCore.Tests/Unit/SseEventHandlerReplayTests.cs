using System.Text;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using Microsoft.AspNetCore.Http;

namespace HPD.Agent.AspNetCore.Tests.Unit;

public sealed class SseEventHandlerReplayTests
{
    [Fact]
    public async Task StreamEventsAsync_ReplaysOnlyCommittedEventsAfterTheCursor()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentAsync(store);
        await agent.CreateSessionAsync("session-1");
        await store.AppendThreadEventAsync(
            "session-1",
            "main",
            new TextDeltaEvent("first", "message-1"));
        await store.AppendThreadEventAsync(
            "session-1",
            "main",
            new TextDeltaEvent("second", "message-1"));
        var committed = await store.LoadThreadDocumentAsync("session-1", "main");
        var firstSequence = committed!.Events
            .OfType<TextDeltaEvent>()
            .Single(evt => evt.Text == "first")
            .SequenceNumber;
        var secondSequence = committed.Events
            .OfType<TextDeltaEvent>()
            .Single(evt => evt.Text == "second")
            .SequenceNumber;
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?after={firstSequence}");
        context.Response.Body = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var streamTask = SseEventHandler.StreamEventsAsync(
            context,
            agent,
            "session-1",
            "main",
            timeout.Token);
        while (context.Response.Body.Length < 20)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        await timeout.CancelAsync();
        await streamTask;

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        body.Should().Contain($"id: {secondSequence}\n");
        body.Should().Contain("second");
        body.Should().NotContain($"id: {firstSequence}\n");
        body.Should().NotContain("first");
    }

    private static Task<Agent> CreateAgentAsync(ISessionStore store)
    {
        var config = new AgentConfig
        {
            Name = "sse-replay-test",
            Clients = new AgentClientConfig
            {
                Chat = new ClientProviderConfig
                {
                    ProviderKey = "test",
                    ModelName = "test-model"
                }
            }
        };

        return new AgentBuilder(config, new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync();
    }
}
