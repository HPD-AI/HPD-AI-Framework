using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Middleware.V2;
using Microsoft.Extensions.AI;
using Xunit;
using static HPD.Agent.Tests.Middleware.V2.MiddlewareTestHelpers;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Tests for AgentMiddlewarePipeline execution order and lifecycle behavior.
/// Ported from IterationFilterPipelineTests.cs with updates for new architecture.
/// </summary>
public class AgentMiddlewarePipelineTests
{
    [Fact]
    public async Task Middlewares_ExecuteInOrder_BeforeAndAfterIteration()
    {
        // Arrange
        var executionLog = new List<string>();
        var middleware1 = new TestMiddleware("Middleware1", executionLog);
        var middleware2 = new TestMiddleware("Middleware2", executionLog);
        var middleware3 = new TestMiddleware("Middleware3", executionLog);

        var pipeline = new AgentMiddlewarePipeline(new[] { middleware1, middleware2, middleware3 });
        var context = CreateContext();

        // Act - Before phase
        await pipeline.ExecuteBeforeIterationAsync(context, CancellationToken.None);

        // Simulate LLM call
        executionLog.Add("LLM");

        // Act - After phase - Create AfterIterationContext for this hook
        var afterContext = MiddlewareTestHelpers.CreateAfterIterationContext();
        await pipeline.ExecuteAfterIterationAsync(afterContext, CancellationToken.None);

        // Assert - Before executes in order, After executes in REVERSE order
        Assert.Equal(new[]
        {
            "Middleware1-before",
            "Middleware2-before",
            "Middleware3-before",
            "LLM",
            "Middleware3-after",  // Reversed!
            "Middleware2-after",
            "Middleware1-after"
        }, executionLog);
    }

    [Fact]
    public async Task BeforeIterationMiddlewares_CanModifyContext()
    {
        // Arrange
        var context = CreateContext();
        // Options set in constructor

        var middleware1 = new InstructionAppendingMiddleware(" + M1");
        var middleware2 = new InstructionAppendingMiddleware(" + M2");

        var pipeline = new AgentMiddlewarePipeline(new[] { middleware1, middleware2 });

        // Act
        await pipeline.ExecuteBeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal("Base + M1 + M2", context.Options.Instructions);
    }

    [Fact]
    public async Task BeforeIterationMiddleware_CanSetSkipLLMCall()
    {
        // Arrange
        var context = CreateContext();
        var middleware = new SkipLLMCallMiddleware();
        var pipeline = new AgentMiddlewarePipeline(new[] { middleware });

        // Act
        await pipeline.ExecuteBeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.True(context.SkipLLMCall);
        Assert.NotNull(context.OverrideResponse); // V2: Middleware provides override response
    }

    [Fact]
    public async Task AfterIterationMiddlewares_CanInspectResponse()
    {
        // Arrange
        var context = CreateContext();
        var modifications = new List<string>();

        var middleware = new ContextModificationTrackingMiddleware(modifications);
        var pipeline = new AgentMiddlewarePipeline(new[] { middleware });

        // Act - Before phase
        await pipeline.ExecuteBeforeIterationAsync(context, CancellationToken.None);

        // Simulate LLM call
        modifications.Add("LLM-executed");

        // Act - After phase - Create AfterIterationContext
        var afterContext = MiddlewareTestHelpers.CreateAfterIterationContext();
        await pipeline.ExecuteAfterIterationAsync(afterContext, CancellationToken.None);

        // Assert
        Assert.Equal(new[]
        {
            "pre-invoke-messages-modified",
            "LLM-executed",
            "post-invoke-response-checked"
        }, modifications);
    }

    [Fact]
    public async Task AfterIterationMiddleware_DetectsFinalIteration()
    {
        // Arrange
        var context = CreateContext();
        var signalSet = false;

        var middleware = new FinalIterationDetectingMiddleware(() => signalSet = true);
        var pipeline = new AgentMiddlewarePipeline(new[] { middleware });

        // Simulate LLM response with NO tool calls (final iteration)


        var afterContext = MiddlewareTestHelpers.CreateAfterIterationContext(


            iteration: 0,


            toolResults: new List<FunctionResultContent>()); // Empty = final



        // Act


        await pipeline.ExecuteAfterIterationAsync(afterContext, CancellationToken.None);

        // Assert


        // V2: Check if final iteration by toolResults count
        Assert.Empty(afterContext.ToolResults);
        Assert.True(signalSet, "Middleware should have detected final iteration");
    }

    [Fact]
    public async Task AfterIterationMiddleware_DoesNotSignalWhenToolCallsExist()
    {
        // Arrange
        var context = CreateContext();
        var signalSet = false;

        var middleware = new FinalIterationDetectingMiddleware(() => signalSet = true);
        var pipeline = new AgentMiddlewarePipeline(new[] { middleware });

        // Simulate LLM response WITH tool calls (not final)


        var afterContext = MiddlewareTestHelpers.CreateAfterIterationContext(


            iteration: 0,


            toolResults: new List<FunctionResultContent> { new FunctionResultContent("call1", "result") });



        // Act


        await pipeline.ExecuteAfterIterationAsync(afterContext, CancellationToken.None);

        // Assert


        // V2: Check if NOT final iteration by toolResults count
        Assert.NotEmpty(afterContext.ToolResults);
        Assert.False(signalSet, "Middleware should NOT signal on non-final iteration");
    }

    [Fact]
    public async Task Middleware_HandlesNullOptions_Gracefully()
    {
        // Arrange
        // V2: Options is always provided in BeforeIterationContext (never null)
        // Test that middleware handles existing options correctly

        var context = CreateContext();
        var middleware = new InstructionAppendingMiddleware(" + Modified");
        var pipeline = new AgentMiddlewarePipeline(new[] { middleware });

        // Act
        await pipeline.ExecuteBeforeIterationAsync(context, CancellationToken.None);

        // Assert - V2 always has Options
        Assert.NotNull(context.Options);
    }

    [Fact]
    public async Task Pipeline_PropagatesMiddlewareExceptions()
    {
        // Arrange
        var executionLog = new List<string>();
        var middleware1 = new TestMiddleware("M1", executionLog);
        var throwingMiddleware = new ThrowingMiddleware();
        var middleware3 = new TestMiddleware("M3", executionLog);

        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[] { middleware1, throwingMiddleware, middleware3 });
        var context = CreateContext();

        // Act & Assert - Should throw and stop executing remaining middlewares
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ExecuteBeforeIterationAsync(context, CancellationToken.None));

        // M1 should have executed before the exception
        Assert.Contains("M1-before", executionLog);

        // M3 should NOT execute because exception stopped the pipeline
        Assert.DoesNotContain("M3-before", executionLog);
    }

    [Fact]
    public async Task BeforeToolExecution_ExecutesInOrder()
    {
        // Arrange
        var executionLog = new List<string>();
        var middleware1 = new TestMiddleware("M1", executionLog);
        var middleware2 = new TestMiddleware("M2", executionLog);

        var pipeline = new AgentMiddlewarePipeline(new[] { middleware1, middleware2 });
        var toolCalls = new[] { new FunctionCallContent("call_1", "func1") };

        var context = CreateBeforeToolExecutionContext(toolCalls: toolCalls.ToList());

        // Act
        await pipeline.ExecuteBeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(new[] { "M1-beforeTool", "M2-beforeTool" }, executionLog);
    }

    private static BeforeIterationContext CreateContext()
    {
        // V2: Use TestHelpers for consistent context creation
        return CreateBeforeIterationContext(
            iteration: 0,
            messages: new List<ChatMessage>(),
            options: new ChatOptions { Instructions = "Base" });
    }

    //     
    // TEST HELPER MIDDLEWARES
    //     

    public class TestMiddleware : IAgentMiddleware
    {
        private readonly string _name;
        private readonly List<string> _log;

        public TestMiddleware(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public Task BeforeIterationAsync(
            BeforeIterationContext context,
            CancellationToken cancellationToken)
        {
            _log.Add($"{_name}-before");
            return Task.CompletedTask;
        }

        public Task BeforeToolExecutionAsync(
            BeforeToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            _log.Add($"{_name}-beforeTool");
            return Task.CompletedTask;
        }

        public Task AfterIterationAsync(
            AfterIterationContext context,
            CancellationToken cancellationToken)
        {
            _log.Add($"{_name}-after");
            return Task.CompletedTask;
        }

        // V2: BeforeSequentialFunctionAsync renamed to BeforeFunctionAsync
        public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public class InstructionAppendingMiddleware : IAgentMiddleware
    {
        private readonly string _textToAppend;

        public InstructionAppendingMiddleware(string textToAppend)
        {
            _textToAppend = textToAppend;
        }

        public Task BeforeIterationAsync(
            BeforeIterationContext context,
            CancellationToken cancellationToken)
        {
            // V2: No NULL check needed - Options always available on BeforeIterationContext
            context.Options.Instructions += _textToAppend;
            return Task.CompletedTask;
        }

        public Task BeforeToolExecutionAsync(BeforeToolExecutionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterIterationAsync(AfterIterationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public class SkipLLMCallMiddleware : IAgentMiddleware
    {
        public Task BeforeIterationAsync(
            BeforeIterationContext context,
            CancellationToken cancellationToken)
        {
            // V2: Skip LLM call and provide cached response
            context.SkipLLMCall = true;
            context.OverrideResponse = new ChatMessage(ChatRole.Assistant, "Cached response");
            return Task.CompletedTask;
        }

        public Task BeforeToolExecutionAsync(BeforeToolExecutionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterIterationAsync(AfterIterationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public class ContextModificationTrackingMiddleware : IAgentMiddleware
    {
        private readonly List<string> _modifications;

        public ContextModificationTrackingMiddleware(List<string> modifications)
        {
            _modifications = modifications;
        }

        public Task BeforeIterationAsync(
            BeforeIterationContext context,
            CancellationToken cancellationToken)
        {
            // Pre-invoke: Modify context
            context.Messages?.Add(new ChatMessage(ChatRole.User, "Modified message"));
            _modifications.Add("pre-invoke-messages-modified");
            return Task.CompletedTask;
        }

        public Task BeforeToolExecutionAsync(BeforeToolExecutionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterIterationAsync(


            AfterIterationContext context,


            CancellationToken cancellationToken)


        {


            // Post-invoke: V2 context has tool results, not response


            if (context.ToolResults != null)
            {
                _modifications.Add("post-invoke-response-checked");
            }
            return Task.CompletedTask;
        }

        public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public class FinalIterationDetectingMiddleware : IAgentMiddleware
    {
        private readonly Action _onFinalIteration;

        public FinalIterationDetectingMiddleware(Action onFinalIteration)
        {
            _onFinalIteration = onFinalIteration;
        }

        public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeToolExecutionAsync(BeforeToolExecutionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterIterationAsync(


            AfterIterationContext context,


            CancellationToken cancellationToken)


        {


            // V2: Check if this is final iteration (no more tool calls)


            if (context.ToolResults?.Count == 0)
            {
                _onFinalIteration();
            }
            return Task.CompletedTask;
        }

        public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public class ThrowingMiddleware : IAgentMiddleware
    {
        public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Test exception");
        }

        public Task BeforeToolExecutionAsync(BeforeToolExecutionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterIterationAsync(AfterIterationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private static AgentContext CreateAgentContext(AgentLoopState? state = null)
    {
        var agentState = state ?? AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        return new AgentContext(
            "TestAgent",
            "test-conversation",
            agentState,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Branch("test-session"),
            CancellationToken.None);
    }

    private static BeforeToolExecutionContext CreateBeforeToolExecutionContext(
        ChatMessage? response = null,
        List<FunctionCallContent>? toolCalls = null,
        AgentLoopState? state = null)
    {
        var agentContext = CreateAgentContext(state);
        response ??= new ChatMessage(ChatRole.Assistant, []);
        toolCalls ??= new List<FunctionCallContent>();
        return agentContext.AsBeforeToolExecution(response, toolCalls, new AgentRunConfig());
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContext(
        AgentLoopState? state = null,
        List<ChatMessage>? turnHistory = null)
    {
        var agentContext = CreateAgentContext(state);
        var finalResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response"));
        turnHistory ??= new List<ChatMessage>();
        return agentContext.AsAfterMessageTurn(finalResponse, turnHistory, new AgentRunConfig());
    }

    //
    // STREAMING REGRESSION TESTS
    //

    [Fact]
    public async Task ExecuteModelCallStreamingAsync_WithNullStreamingImplementation_PassesThroughStream()
    {
        // Regression test for: Middleware that returns null for WrapModelCallStreamingAsync
        // should pass through the stream unchanged (not buffer it)

        // Arrange
        var middleware = new NoOpMiddleware(); // Returns null for streaming
        var pipeline = new AgentMiddlewarePipeline(new[] { middleware });

        var mockChatClient = new MiddlewareTestHelpers.TestChatClient();
        var agentState = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        var request = new ModelRequest
        {
            Model = mockChatClient,
            Messages = new List<ChatMessage>(),
            Options = new ChatOptions(),
            State = agentState,
            Iteration = 0
        };

        var expectedUpdates = new[]
        {
            new ChatResponseUpdate { Contents = new List<AIContent> { new TextContent("Hello") } },
            new ChatResponseUpdate { Contents = new List<AIContent> { new TextContent(" world") } },
            new ChatResponseUpdate { Contents = new List<AIContent> { new TextContent("!") } }
        };

        async IAsyncEnumerable<ChatResponseUpdate> StreamingHandler(ModelRequest req)
        {
            foreach (var update in expectedUpdates)
            {
                yield return update;
            }
        }

        // Act
        var actualUpdates = new List<ChatResponseUpdate>();
        await foreach (var update in pipeline.ExecuteModelCallStreamingAsync(request, StreamingHandler, CancellationToken.None))
        {
            actualUpdates.Add(update);
        }

        // Assert - Should receive ALL individual updates (not buffered into one)
        Assert.Equal(3, actualUpdates.Count);
        Assert.Equal("Hello", ((TextContent)actualUpdates[0].Contents[0]).Text);
        Assert.Equal(" world", ((TextContent)actualUpdates[1].Contents[0]).Text);
        Assert.Equal("!", ((TextContent)actualUpdates[2].Contents[0]).Text);
    }

    [Fact]
    public async Task ExecuteModelCallStreamingAsync_WithMultipleNullMiddlewares_PassesThroughStream()
    {
        // Regression test: Multiple middlewares returning null should all pass through

        // Arrange
        var middleware1 = new NoOpMiddleware();
        var middleware2 = new NoOpMiddleware();
        var middleware3 = new NoOpMiddleware();
        var pipeline = new AgentMiddlewarePipeline(new[] { middleware1, middleware2, middleware3 });

        var mockChatClient = new MiddlewareTestHelpers.TestChatClient();
        var agentState = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        var request = new ModelRequest
        {
            Model = mockChatClient,
            Messages = new List<ChatMessage>(),
            Options = new ChatOptions(),
            State = agentState,
            Iteration = 0
        };

        async IAsyncEnumerable<ChatResponseUpdate> StreamingHandler(ModelRequest req)
        {
            yield return new ChatResponseUpdate { Contents = new List<AIContent> { new TextContent("A") } };
            yield return new ChatResponseUpdate { Contents = new List<AIContent> { new TextContent("B") } };
        }

        // Act
        var actualUpdates = new List<ChatResponseUpdate>();
        await foreach (var update in pipeline.ExecuteModelCallStreamingAsync(request, StreamingHandler, CancellationToken.None))
        {
            actualUpdates.Add(update);
        }

        // Assert - Should receive individual updates (not buffered)
        Assert.Equal(2, actualUpdates.Count);
    }

    /// <summary>
    /// Middleware that returns null for streaming (default behavior).
    /// Used to test that null streaming implementation passes through correctly.
    /// </summary>
    public class NoOpMiddleware : IAgentMiddleware
    {
        // All hooks use default implementation (no-op)
        // WrapModelCallStreamingAsync returns null by default
    }

}
