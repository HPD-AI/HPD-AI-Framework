using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Providers.OnnxRuntime;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Tests;

public sealed class OnnxRuntimeStructuredToolCallingBehaviorTests
{
    [Fact]
    public void BuildToolJson_UsesFunctionNameDescriptionAndSchema()
    {
        var tool = CreateAddTool();

        var json = StructuredToolCallingOnnxRuntimeChatClient.BuildToolJson([tool]);

        using var document = JsonDocument.Parse(json);
        var function = document.RootElement[0].GetProperty("function");
        function.GetProperty("name").GetString().Should().Be("Add");
        function.GetProperty("description").GetString().Should().Be("Adds two numbers.");
        function.GetProperty("parameters").GetProperty("properties").EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void TryParseToolCallEnvelope_ConvertsJsonIntoFunctionCallContent()
    {
        var parsed = StructuredToolCallingOnnxRuntimeChatClient.TryParseToolCallEnvelope(
            """{"tool_call":{"name":"Add","arguments":{"a":2,"b":3}}}""",
            new HashSet<string>(StringComparer.Ordinal) { "Add" },
            out var toolCall);

        parsed.Should().BeTrue();
        toolCall.Should().NotBeNull();
        toolCall!.CallId.Should().StartWith("onnx_call_");
        toolCall.Name.Should().Be("Add");
        toolCall.Arguments.Should().ContainKey("a").WhoseValue.Should().Be(2);
        toolCall.Arguments.Should().ContainKey("b").WhoseValue.Should().Be(3);
    }

    [Fact]
    public void TryParseToolCallEnvelope_RejectsUnknownToolName()
    {
        var parsed = StructuredToolCallingOnnxRuntimeChatClient.TryParseToolCallEnvelope(
            """{"tool_call":{"name":"DeleteEverything","arguments":{}}}""",
            new HashSet<string>(StringComparer.Ordinal) { "Add" },
            out var toolCall);

        parsed.Should().BeFalse();
        toolCall.Should().BeNull();
    }

    [Fact]
    public void TryParseToolCallEnvelope_IgnoresTrailingModelJunk()
    {
        var parsed = StructuredToolCallingOnnxRuntimeChatClient.TryParseToolCallEnvelope(
            """{"tool_call":{"name":"Add","arguments":{"a":2,"b":3}}}}}}""",
            new HashSet<string>(StringComparer.Ordinal) { "Add" },
            out var toolCall);

        parsed.Should().BeTrue();
        toolCall.Should().NotBeNull();
        toolCall!.Name.Should().Be("Add");
    }

    [Fact]
    public void TryCreateStructuredToolOptions_AddsJsonResponseFormatAndToolInstructions()
    {
        var options = new ChatOptions
        {
            Instructions = "Be concise.",
            Tools = [CreateAddTool()],
            Temperature = 0.2f
        };

        var created = StructuredToolCallingOnnxRuntimeChatClient.TryCreateStructuredToolOptions(
            options,
            out var structuredOptions,
            out var allowedToolNames);

        created.Should().BeTrue();
        structuredOptions.Should().NotBeSameAs(options);
        structuredOptions.Temperature.Should().Be(0.2f);
        structuredOptions.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>();
        structuredOptions.AllowMultipleToolCalls.Should().BeFalse();
        structuredOptions.Instructions.Should().Contain("Be concise.");
        structuredOptions.Instructions.Should().Contain("\"name\":\"Add\"");
        allowedToolNames.Should().BeEquivalentTo(["Add"]);
        options.ResponseFormat.Should().BeNull();
    }

    [Fact]
    public void TryCreateStructuredToolOptions_WithToolModeNone_DoesNotShapeOptions()
    {
        var options = new ChatOptions
        {
            ToolMode = ChatToolMode.None,
            Tools = [CreateAddTool()]
        };

        var created = StructuredToolCallingOnnxRuntimeChatClient.TryCreateStructuredToolOptions(
            options,
            out var structuredOptions,
            out var allowedToolNames);

        created.Should().BeFalse();
        structuredOptions.ToolMode.Should().Be(ChatToolMode.None);
        structuredOptions.ResponseFormat.Should().BeNull();
        allowedToolNames.Should().BeEmpty();
    }

    [Fact]
    public async Task GetResponseAsync_WithStructuredToolEnvelope_ReturnsFunctionCallContent()
    {
        var innerClient = new CapturingChatClient(
            new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                """{"tool_call":{"name":"Add","arguments":{"a":2,"b":3}}}""")));
        using var client = new StructuredToolCallingOnnxRuntimeChatClient(innerClient);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Add 2 and 3.")],
            new ChatOptions { Tools = [CreateAddTool()] });

        var toolCall = response.Messages.Single().Contents.OfType<FunctionCallContent>().Single();
        toolCall.Name.Should().Be("Add");
        toolCall.Arguments.Should().ContainKey("a").WhoseValue.Should().Be(2);
        toolCall.Arguments.Should().ContainKey("b").WhoseValue.Should().Be(3);
        innerClient.LastOptions.Should().NotBeNull();
        innerClient.LastOptions!.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>();
        innerClient.LastOptions.Instructions.Should().Contain("\"name\":\"Add\"");
    }

    [Fact]
    public async Task GetResponseAsync_WithoutFunctionTools_DelegatesUnchanged()
    {
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "plain text"));
        var innerClient = new CapturingChatClient(expectedResponse);
        using var client = new StructuredToolCallingOnnxRuntimeChatClient(innerClient);
        var options = new ChatOptions { Temperature = 0.1f };

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options);

        response.Should().BeSameAs(expectedResponse);
        innerClient.LastOptions.Should().BeSameAs(options);
    }

    private static AIFunction CreateAddTool()
    {
        return AIFunctionFactory.Create(
            (int a, int b) => a + b,
            new AIFunctionFactoryOptions
            {
                Name = "Add",
                Description = "Adds two numbers."
            });
    }

    private sealed class CapturingChatClient(ChatResponse response) : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(ChatClientMetadata)
                ? new ChatClientMetadata("test")
                : null;
    }
}
