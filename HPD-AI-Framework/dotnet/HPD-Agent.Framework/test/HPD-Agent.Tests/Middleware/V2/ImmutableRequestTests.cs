using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;


/// <summary>
/// Tests for immutable request/response objects - LangChain pattern.
/// </summary>
public class ImmutableRequestTests
{
    [Fact]
    public void AgentModelTurnRequest_Override_PreservesOriginal()
    {
        // Arrange
        var originalMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "Original question")
        };

        var originalRequest = new AgentModelTurnRequest
        {
            Transport = AgentModelTransport.Chat,
            ChatModel = new TestChatClient(),
            Messages = originalMessages,
            Options = new ChatOptions { Temperature = 0.5f },
            State = CreateTestState(),
            Iteration = 0
        };

        // Act
        var modifiedRequest = originalRequest.Override(
            messages: originalRequest.Messages.Append(
                new ChatMessage(ChatRole.System, "Added context")).ToList());

        // Assert - original preserved!
        Assert.Single(originalRequest.Messages);
        Assert.Equal(2, modifiedRequest.Messages.Count);
        Assert.Equal("Original question", originalRequest.Messages[0].Text);
    }

    [Fact]
    public void AgentModelTurnRequest_Override_MultipleProperties()
    {
        // Arrange
        var request = new AgentModelTurnRequest
        {
            Transport = AgentModelTransport.Chat,
            ChatModel = new TestChatClient(),
            Messages = new List<ChatMessage>(),
            Options = new ChatOptions { Temperature = 0.5f },
            State = CreateTestState(),
            Iteration = 0
        };

        // Act
        var newMessages = new List<ChatMessage> { new(ChatRole.User, "New") };
        var newOptions = new ChatOptions { Temperature = 0.9f };

        var modified = request.Override(
            messages: newMessages,
            options: newOptions);

        // Assert
        Assert.Same(newMessages, modified.Messages);
        Assert.Equal(0.9f, modified.Options.Temperature);
        Assert.Same(request.ChatModel, modified.ChatModel); // Unchanged
        Assert.Equal(0, modified.Iteration); // Unchanged
    }

    [Fact]
    public void FunctionRequest_Override_PreservesOriginal()
    {
        // Arrange
        var function = AIFunctionFactory.Create(() => "test", "TestFunc");
        var originalArgs = new Dictionary<string, object?>
        {
            ["arg1"] = "value1",
            ["arg2"] = "value2"
        };

        var originalRequest = new FunctionRequest
        {
            Function = function,
            CallId = "call123",
            Arguments = originalArgs,
            State = CreateTestState(),
            HarnessName = "TestHarness",
            SkillName = null
        };

        // Act - sanitize PII
        var sanitized = originalRequest.Override(
            arguments: new Dictionary<string, object?> { ["arg1"] = "***REDACTED***" });

        // Assert - original preserved!
        Assert.Equal("value1", originalRequest.Arguments["arg1"]);
        Assert.Equal("***REDACTED***", sanitized.Arguments["arg1"]);
        Assert.False(sanitized.Arguments.ContainsKey("arg2")); // Removed
    }

    [Fact]
    public void ModelResponse_Properties_Immutable()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.Assistant, "Response");
        var toolCalls = new List<FunctionCallContent>
        {
            new("call1", "func1")
        };

        var response = new ModelResponse
        {
            Message = message,
            ToolCalls = toolCalls,
            Error = null
        };

        // Assert
        Assert.True(response.IsSuccess);
        Assert.False(response.IsFailure);
        Assert.True(response.HasToolCalls);
        Assert.False(response.IsFinalResponse);
    }

    [Fact]
    public void ModelResponse_WithError_HelperProperties()
    {
        // Arrange
        var response = new ModelResponse
        {
            Message = new ChatMessage(ChatRole.Assistant, ""),
            ToolCalls = Array.Empty<FunctionCallContent>(),
            Error = new InvalidOperationException("Test error")
        };

        // Assert
        Assert.False(response.IsSuccess);
        Assert.True(response.IsFailure);
        Assert.False(response.HasToolCalls);
        Assert.False(response.IsFinalResponse);
    }

    [Fact]
    public void FunctionRequest_Helpers()
    {
        // Arrange
        var function = AIFunctionFactory.Create(() => "test", "MyFunc");

        var request = new FunctionRequest
        {
            Function = function,
            CallId = "call1",
            Arguments = new Dictionary<string, object?>(),
            State = CreateTestState(),
            HarnessName = "MyHarness",
            SkillName = "MySkill"
        };

        // Assert
        Assert.Equal("MyFunc", request.FunctionName);
        Assert.True(request.IsHarnessFunction);
        Assert.True(request.IsSkillFunction);
    }

    [Fact]
    public void FunctionRequest_NoHarnessOrSkill()
    {
        // Arrange
        var function = AIFunctionFactory.Create(() => "test", "StandaloneFunc");

        var request = new FunctionRequest
        {
            Function = function,
            CallId = "call1",
            Arguments = new Dictionary<string, object?>(),
            State = CreateTestState(),
            HarnessName = null,
            SkillName = null
        };

        // Assert
        Assert.False(request.IsHarnessFunction);
        Assert.False(request.IsSkillFunction);
    }

    // Helpers

    private static AgentLoopState CreateTestState()
    {
        return AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "run123",
            "conv123",
            "TestAgent");
    }

    private class TestChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("test");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "Test response")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;

        public void Dispose() => GC.SuppressFinalize(this);
    }
}
