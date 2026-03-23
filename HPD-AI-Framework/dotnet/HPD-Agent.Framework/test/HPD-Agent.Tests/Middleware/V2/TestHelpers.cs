using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using SessionModel = global::HPD.Agent.Session;
using BranchModel = global::HPD.Agent.Branch;

namespace HPD.Agent.Tests.Middleware.V2;

/// <summary>
/// Test helpers for creating V2 middleware contexts.
/// </summary>
public static class MiddlewareTestHelpers
{
    private static AgentRunConfig CreateDefaultRunConfig()
    {
        // Empty run options - all properties are optional
        return new AgentRunConfig();
    }


    /// <summary>
    /// Creates a test AgentContext with default values.
    /// </summary>
    public static AgentContext CreateAgentContext(
        string agentName = "TestAgent",
        string? conversationId = "test-conv",
        AgentLoopState? state = null,
        HPD.Events.IEventCoordinator? eventCoordinator = null,
        SessionModel? session = null,
        BranchModel? branch = null,
        CancellationToken cancellationToken = default)
    {
        state ??= AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            conversationId ?? "test-conv",
            agentName);

        eventCoordinator ??= new HPD.Events.Core.EventCoordinator();
        var sessionId = conversationId ?? "test-session";
        session ??= new SessionModel(sessionId);
        branch ??= new BranchModel(sessionId);

        return new AgentContext(
            agentName,
            conversationId,
            state,
            eventCoordinator,
            session,
            branch,
            cancellationToken);
    }

    /// <summary>
    /// Creates a BeforeIterationContext for testing.
    /// </summary>
    public static BeforeIterationContext CreateBeforeIterationContext(
        int iteration = 0,
        List<ChatMessage>? messages = null,
        ChatOptions? options = null,
        AgentLoopState? state = null,
        string agentName = "TestAgent")
    {
        var context = CreateAgentContext(agentName: agentName, state: state);
        return context.AsBeforeIteration(
            iteration,
            messages ?? new List<ChatMessage>(),
            options ?? new ChatOptions(),
            runConfig: CreateDefaultRunConfig());
    }

    /// <summary>
    /// Creates an AfterIterationContext for testing.
    /// </summary>
    public static AfterIterationContext CreateAfterIterationContext(
        int iteration = 0,
        List<FunctionResultContent>? toolResults = null,
        AgentLoopState? state = null,
        string agentName = "TestAgent")
    {
        var context = CreateAgentContext(agentName: agentName, state: state);
        return context.AsAfterIteration(iteration, toolResults ?? new List<FunctionResultContent>(), CreateDefaultRunConfig());
    }

    /// <summary>
    /// Creates a BeforeFunctionContext for testing.
    /// </summary>
    public static BeforeFunctionContext CreateBeforeFunctionContext(
        AIFunction? function = null,
        string callId = "test-call",
        IReadOnlyDictionary<string, object?>? arguments = null,
        string? ToolkitName = null,
        string? skillName = null,
        AgentLoopState? state = null,
        string agentName = "TestAgent")
    {
        var context = CreateAgentContext(agentName: agentName, state: state);
        function ??= AIFunctionFactory.Create(() => "test", "TestFunction");
        arguments ??= new Dictionary<string, object?>();

        return context.AsBeforeFunction(function, callId, arguments, CreateDefaultRunConfig(), ToolkitName, skillName);
    }

    /// <summary>
    /// Creates an AfterFunctionContext for testing.
    /// </summary>
    public static AfterFunctionContext CreateAfterFunctionContext(
        AIFunction? function = null,
        string callId = "test-call",
        object? result = null,
        Exception? exception = null,
        AgentLoopState? state = null,
        string agentName = "TestAgent")
    {
        var context = CreateAgentContext(agentName: agentName, state: state);
        function ??= AIFunctionFactory.Create(() => "test", "TestFunction");

        return context.AsAfterFunction(function, callId, result, exception, CreateDefaultRunConfig());
    }

    /// <summary>
    /// Creates an ErrorContext for testing.
    /// </summary>
    public static ErrorContext CreateErrorContext(
        Exception? error = null,
        ErrorSource source = ErrorSource.ToolCall,
        int iteration = 0,
        AgentLoopState? state = null,
        string agentName = "TestAgent")
    {
        var context = CreateAgentContext(agentName: agentName, state: state);
        error ??= new InvalidOperationException("Test error");

        return context.AsError(error, source, iteration);
    }

    /// <summary>
    /// Creates a BeforeMessageTurnContext for testing.
    /// </summary>
    public static BeforeMessageTurnContext CreateBeforeMessageTurnContext(
        ChatMessage? userMessage = null,
        List<ChatMessage>? conversationHistory = null,
        AgentRunConfig? runConfig = null,
        AgentLoopState? state = null,
        string agentName = "TestAgent")
    {
        var context = CreateAgentContext(agentName: agentName, state: state);
        userMessage ??= new ChatMessage(ChatRole.User, "Test message");
        conversationHistory ??= new List<ChatMessage>();
        runConfig ??= new AgentRunConfig();

        return context.AsBeforeMessageTurn(userMessage, conversationHistory, runConfig);
    }

    /// <summary>
    /// Creates an AfterMessageTurnContext for testing.
    /// </summary>
    public static AfterMessageTurnContext CreateAfterMessageTurnContext(
        ChatResponse? finalResponse = null,
        List<ChatMessage>? turnHistory = null,
        AgentLoopState? state = null,
        string agentName = "TestAgent")
    {
        var context = CreateAgentContext(agentName: agentName, state: state);
        finalResponse ??= new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response"));
        turnHistory ??= new List<ChatMessage>();

        return context.AsAfterMessageTurn(finalResponse, turnHistory, CreateDefaultRunConfig());
    }

    /// <summary>
    /// Creates a BeforeToolExecutionContext for testing.
    /// </summary>
    public static BeforeToolExecutionContext CreateBeforeToolExecutionContext(
        ChatMessage? response = null,
        List<FunctionCallContent>? toolCalls = null,
        AgentLoopState? state = null,
        string agentName = "TestAgent")
    {
        var context = CreateAgentContext(agentName: agentName, state: state);
        response ??= new ChatMessage(ChatRole.Assistant, "Test response");
        toolCalls ??= new List<FunctionCallContent>();

        return context.AsBeforeToolExecution(response, toolCalls, CreateDefaultRunConfig());
    }

    /// <summary>
    /// Creates a BeforeParallelBatchContext for testing.
    /// </summary>
    public static BeforeParallelBatchContext CreateBeforeParallelBatchContext(
        List<ParallelFunctionInfo>? functions = null,
        AgentLoopState? state = null,
        string agentName = "TestAgent")
    {
        var context = CreateAgentContext(agentName: agentName, state: state);
        functions ??= new List<ParallelFunctionInfo>();

        return context.AsBeforeParallelBatch(functions, CreateDefaultRunConfig());
    }

    /// <summary>
    /// Creates a ModelRequest for testing WrapModelCallStreamingAsync.
    /// </summary>
    public static ModelRequest CreateModelRequest(
        IChatClient? model = null,
        List<ChatMessage>? messages = null,
        ChatOptions? options = null,
        AgentLoopState? state = null,
        int iteration = 0)
    {
        state ??= AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            "test-conv",
            "TestAgent");

        return new ModelRequest
        {
            Model = model ?? new TestChatClient(),
            Messages = messages ?? new List<ChatMessage>(),
            Options = options ?? new ChatOptions(),
            State = state,
            Iteration = iteration
        };
    }

    /// <summary>
    /// Creates a FunctionRequest for testing WrapFunctionCallAsync.
    /// </summary>
    public static FunctionRequest CreateFunctionRequest(
        AIFunction? function = null,
        string callId = "test-call",
        IReadOnlyDictionary<string, object?>? arguments = null,
        AgentLoopState? state = null)
    {
        state ??= AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            "test-conv",
            "TestAgent");

        function ??= AIFunctionFactory.Create(() => "test", "TestFunction");
        arguments ??= new Dictionary<string, object?>();

        return new FunctionRequest
        {
            Function = function,
            CallId = callId,
            Arguments = arguments,
            State = state
        };
    }

    /// <summary>
    /// Simple test chat client for testing.
    /// </summary>
    public class TestChatClient : IChatClient
    {
        /// <inheritdoc />
        public ChatClientMetadata Metadata => new("test-client");

        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "Test response")));
        }

        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        /// <inheritdoc />
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;

        /// <inheritdoc />
        public void Dispose() => GC.SuppressFinalize(this);
    }
}
