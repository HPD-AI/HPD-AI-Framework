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
        var applicationArtifact = await store.WriteBytesAsync(
            ContentScope.Create("application-artifacts"),
            "authoritative"u8.ToArray(),
            "application/octet-stream");
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
        await agent.DeleteSessionAsync("offload-session");
        Assert.Empty(await store.QueryAsync(ContentScope.Create("offload-session")));
        Assert.Single(await store.QueryAsync(ContentScope.Create("application-artifacts")));
        Assert.NotNull(await store.StatAsync(applicationArtifact.Address));
    }

    [Fact]
    public async Task FinalizedCapture_IsPublishedAsCompletePartAndRetryIsIdempotent()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("CaptureProbe", "call-capture");
        client.EnqueueTextResponse("first");
        client.EnqueueToolCall("CaptureProbe", "call-capture");
        client.EnqueueTextResponse("second");
        var store = new InMemoryContentStore();
        var options = new JsonSerializerOptions(
            HPD.Agent.Serialization.HpdAgentJsonUtilities.CreateDefaultOptions());
        options.TypeInfoResolverChain.Insert(0, ToolResultLifecycleJsonContext.Default);
        options.MakeReadOnly();
        var tool = HPDAIFunctionFactory.Create(
            (_, context, _) =>
            {
                var output = context.Outputs.CreatePart("stdout", "text/plain");
                output.WriteAsync(System.Text.Encoding.UTF8.GetBytes("HEAD\ncomplete tail\n"))
                    .AsTask().GetAwaiter().GetResult();
                return Task.FromResult<object?>(new CaptureResult("finished", output));
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = "CaptureProbe",
                ResultType = typeof(CaptureResult),
                SerializerOptions = options,
                ContentProjectionPlan = new(
                    typeof(CaptureResult),
                    [new("/output", "output", ContentSensitivity.Internal, StreamingCapture: true)])
            });

        await using var agent = await new AgentBuilder(new AgentConfig
            {
                Name = "capture-test",
                ToolOutputs = new ToolOutputConfig { MaxInlineResultBytes = 64 * 1024 }
            })
            .WithEventComposition(HPD.Agent.Serialization.CoreAgentEventComposition.Instance)
            .WithChatClient(new IdentifiedClient(client))
            .WithContentStore(store)
            .WithNativeFunction(tool)
            .BuildAsync();

        await agent.CreateSessionAsync("capture-session");
        for (var index = 0; index < 2; index++)
        {
            await agent.RunAsync(new UserMessagesInputEvent
            {
                SessionId = "capture-session",
                ThreadId = "main",
                Messages = [new(ChatRole.User, "run capture")]
            });
        }

        var stored = Assert.Single(await store.QueryAsync(ContentScope.Create("capture-session")));
        await using var opened = await store.OpenReadAsync(stored.Address);
        using var document = await JsonDocument.ParseAsync(opened!.Content);
        Assert.Equal("HEAD\ncomplete tail\n",
            document.RootElement.GetProperty("parts").GetProperty("stdout").GetString());
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

public sealed record CaptureResult(string Summary, ContentCapturePart Output);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(OffloadResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CaptureResult))]
internal partial class ToolResultLifecycleJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
