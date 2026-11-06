using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

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
}
