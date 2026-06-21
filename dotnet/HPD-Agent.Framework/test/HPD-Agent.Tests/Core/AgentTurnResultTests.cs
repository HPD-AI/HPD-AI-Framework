using System.Collections.Concurrent;
using System.Linq;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Core;

public sealed class AgentTurnResultTests : AgentTestBase
{
    [Fact]
    public async Task RunAsync_Returns_Text_From_TextDeltaEvents()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("Hello", " ", "world", "!");
        var agent = CreateAgent(client: fakeClient);

        var result = await agent.RunAsync("test", cancellationToken: TestCancellationToken);

        Assert.Equal("Hello world!", result.Text);
        Assert.Contains(result.Events, e => e is TextDeltaEvent);
        Assert.NotNull(result.Started);
        Assert.NotNull(result.Finished);
    }

    [Fact]
    public async Task RunAsync_Returns_All_TurnEvents()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("Turn", " ", "events");
        var agent = CreateAgent(client: fakeClient);

        var result = await agent.RunAsync("test", cancellationToken: TestCancellationToken);

        Assert.Contains(result.Events, e => e is MessageTurnStartedEvent);
        Assert.Contains(result.Events, e => e is TextMessageStartEvent);
        Assert.Contains(result.Events, e => e is TextDeltaEvent);
        Assert.Contains(result.Events, e => e is TextMessageEndEvent);
        Assert.Contains(result.Events, e => e is MessageTurnFinishedEvent);
    }

    [Fact]
    public async Task RunAsync_Returns_Completion_Metadata()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("metadata");
        var agent = CreateAgent(client: fakeClient);

        var result = await agent.RunAsync("test", cancellationToken: TestCancellationToken);

        Assert.NotNull(result.Finished);
        Assert.Equal(result.Finished!.Usage, result.Usage);
        Assert.Equal(result.Finished.Duration, result.Duration);
    }

    [Fact]
    public async Task RunAsync_Still_Emits_Events_To_Subscribers()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("Live", " ", "and", " ", "final");
        var agent = CreateAgent(client: fakeClient);
        var observed = new ConcurrentQueue<string>();

        using var subscription = agent.Subscribe<TextDeltaEvent>(evt => observed.Enqueue(evt.Text));

        var result = await agent.RunAsync("test", cancellationToken: TestCancellationToken);

        Assert.Equal("Live and final", result.Text);
        Assert.Equal("Live and final", string.Concat(observed));
    }

    [Fact]
    public async Task RunAsync_Text_Excludes_Text_From_ToolCall_Messages()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextWithToolCall(
            "{\"location\":\"Chicago\"}",
            "get_weather",
            "call_1",
            new Dictionary<string, object?> { ["city"] = "Chicago" });
        fakeClient.EnqueueTextResponse("It is sunny and 72 F in Chicago.");

        var weather = AIFunctionFactory.Create(
            (string city) => $"It is sunny and 72 F in {city}.",
            name: "get_weather");
        var agent = CreateAgent(client: fakeClient, tools: [weather]);

        var result = await agent.RunAsync("What is the weather in Chicago?", cancellationToken: TestCancellationToken);

        Assert.Equal("It is sunny and 72 F in Chicago.", result.Text);
        Assert.Contains(result.Events, e => e is TextDeltaEvent text && text.Text.Contains("location"));
        Assert.Contains(result.Events, e => e is ToolCallStartEvent);
    }

    [Fact]
    public async Task RunAsync_Returns_ToolCalls_From_ToolEvents()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextWithToolCall(
            "{\"location\":\"Chicago\"}",
            "get_weather",
            "call_1",
            new Dictionary<string, object?> { ["city"] = "Chicago" });
        fakeClient.EnqueueTextResponse("It is sunny and 72 F in Chicago.");

        var weather = AIFunctionFactory.Create(
            (string city) => $"It is sunny and 72 F in {city}.",
            name: "get_weather");
        var agent = CreateAgent(client: fakeClient, tools: [weather]);

        var result = await agent.RunAsync("What is the weather in Chicago?", cancellationToken: TestCancellationToken);

        var toolCall = Assert.Single(result.ToolCalls);
        Assert.Equal("call_1", toolCall.CallId);
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Contains("Chicago", toolCall.ArgumentsJson);
        Assert.Equal("\"It is sunny and 72 F in Chicago.\"", toolCall.Result?.Text);
        Assert.Equal("It is sunny and 72 F in Chicago.", toolCall.Text);
    }
}
