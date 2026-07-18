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
        await store.AppendThreadEventAsync(
            "session-1",
            "main",
            new TextDeltaEvent("first", "message-1"));
        await store.AppendThreadEventAsync(
            "session-1",
            "main",
            new TextDeltaEvent("second", "message-1"));
        var committed = await store.CollectThreadEventsAsync("session-1", "main");
        var firstSequence = committed!
            .OfType<TextDeltaEvent>()
            .Single(evt => evt.Text == "first")
            .ThreadSequenceNumber;
        var secondSequence = committed
            .OfType<TextDeltaEvent>()
            .Single(evt => evt.Text == "second")
            .ThreadSequenceNumber;
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?after=1:{firstSequence}");
        context.Response.Body = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var streamTask = SseEventHandler.StreamEventsAsync(
            context,
            store,
            new ThreadKey("session-1", "main"),
            timeout.Token);
        while (context.Response.Body.Length < 20)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        await timeout.CancelAsync();
        await streamTask;

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        body.Should().Contain($"id: 1:{secondSequence}\n");
        body.Should().Contain("second");
        body.Should().NotContain($"id: 1:{firstSequence}\n");
        body.Should().NotContain("first");
    }
}
