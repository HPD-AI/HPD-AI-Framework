using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace HPD.Agent.Tests.Tools;

public sealed class ToolResultLifecycleTests : AgentTestBase
{
    [Fact]
    public async Task AgentExecution_PreservesRawDeclaredResultThroughAfterFunction()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("RawProbe", "call-1");
        client.EnqueueTextResponse("done");
        var capture = new ResultCaptureMiddleware();
        var tool = HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>(new WeatherResult("Chicago", 72)),
            new HPDAIFunctionFactoryOptions
            {
                Name = "RawProbe",
                ResultType = typeof(WeatherResult),
                SerializerOptions = ResultMarshallingJsonContext.Default.Options
            });

        await using var agent = CreateAgentWithMiddlewares(
            DefaultConfig(),
            client,
            [capture],
            tools: [tool]);

        await agent.RunAsync("run the probe");

        var raw = Assert.IsType<WeatherResult>(capture.OriginalResult);
        Assert.Same(raw, capture.Result);
        Assert.Equal("Chicago", raw.City);
        Assert.Equal(72, raw.Temperature);
    }

    [Fact]
    public async Task MarkedNativeProperty_IsStoredAndReplacedBeforeModelDelivery()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("OffloadProbe", "call-offload");
        client.EnqueueTextResponse("done");
        var store = new InMemoryContentStore();
        var tool = HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>(new OffloadResult(
                "summary",
                new string('x', 80_000))),
            new HPDAIFunctionFactoryOptions
            {
                Name = "OffloadProbe",
                ResultType = typeof(OffloadResult),
                SerializerOptions = ToolResultLifecycleJsonContext.Default.Options,
                ContentProjectionPlan = new(
                    typeof(OffloadResult),
                    [new("/evidence", "evidence", ContentSensitivity.Internal)])
            });

        await using var agent = await new AgentBuilder(new AgentConfig
            {
                Name = "offload-test",
                ToolOutputs = new ToolOutputConfig { MaxInlineResultBytes = 64 * 1024 }
            })
            .WithEventComposition(HPD.Agent.Serialization.CoreAgentEventComposition.Instance)
            .WithChatClient(new IdentifiedClient(client))
            .WithContentStore(store)
            .WithNativeFunction(tool)
            .BuildAsync();

        await agent.CreateSessionAsync("offload-session");
        await agent.RunAsync(new UserMessagesInputEvent
        {
            SessionId = "offload-session",
            ThreadId = "main",
            Messages = [new(ChatRole.User, "run offload")]
        });

        var result = client.CapturedRequests[1]
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(static content => content.CallId == "call-offload")
            .Result;
        var json = Assert.IsType<JsonElement>(result);
        var reference = json.GetProperty("evidence");
        Assert.Equal((int)ContentDisposition.ModelRetrievable, reference.GetProperty("disposition").GetInt32());
        Assert.False(reference.TryGetProperty("body", out _));
        var stored = await store.QueryAsync(ContentScope.Create("offload-session"));
        Assert.Single(stored);
    }

    private sealed class ResultCaptureMiddleware : IAgentMiddleware
    {
        internal object? OriginalResult { get; private set; }
        internal object? Result { get; private set; }

        public Task AfterFunctionAsync(
            AfterFunctionContext context,
            CancellationToken cancellationToken)
        {
            OriginalResult = context.OriginalResult;
            Result = context.Result;
            return Task.CompletedTask;
        }
    }

    private sealed class IdentifiedClient(FakeChatClient inner) : DelegatingChatClient(inner)
    {
        public override object? GetService(Type type, object? key = null)
            => type == typeof(HPD.Agent.Providers.ProviderClientExecutionIdentity)
                ? HPD.Agent.Providers.ProviderClientExecutionIdentity.CreateSafe(
                    "test", "test", HPD.Agent.Providers.ProviderClientFamily.Chat,
                    "fake", "test/chat", "test/final")
                : base.GetService(type, key);
    }
}

public sealed record OffloadResult(string Summary, string Evidence);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(OffloadResult))]
internal partial class ToolResultLifecycleJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
