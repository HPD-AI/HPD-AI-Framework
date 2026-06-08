using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Providers.OnnxRuntime;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Providers;

public sealed class OnnxRuntimeStructuredToolCallingTests
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
        var allowedTools = new HashSet<string>(StringComparer.Ordinal) { "Add" };

        var parsed = StructuredToolCallingOnnxRuntimeChatClient.TryParseToolCallEnvelope(
            """{"tool_call":{"name":"Add","arguments":{"a":2,"b":3}}}""",
            allowedTools,
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
        toolCall.Arguments.Should().ContainKey("a").WhoseValue.Should().Be(2);
        toolCall.Arguments.Should().ContainKey("b").WhoseValue.Should().Be(3);
    }

    [Fact]
    public void TryCreateStructuredToolOptions_AddsJsonResponseFormatAndToolInstructions()
    {
        var tool = CreateAddTool();
        var options = new ChatOptions
        {
            Instructions = "Be concise.",
            Tools = [tool],
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

    [Fact]
    public async Task GetResponseAsync_WithToolModeNone_DelegatesUnchanged()
    {
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "plain text"));
        var innerClient = new CapturingChatClient(expectedResponse);
        using var client = new StructuredToolCallingOnnxRuntimeChatClient(innerClient);
        var options = new ChatOptions
        {
            ToolMode = ChatToolMode.None,
            Tools = [CreateAddTool()]
        };

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options);

        response.Should().BeSameAs(expectedResponse);
        innerClient.LastOptions.Should().BeSameAs(options);
        innerClient.LastOptions!.ResponseFormat.Should().BeNull();
    }

    [Fact]
    public void OnnxRuntimeJsonContext_SerializesStructuredToolCallingFlag()
    {
        var config = new OnnxRuntimeProviderConfig
        {
            EnableStructuredToolCalling = true,
            ModelPath = "/models/local"
        };

        var json = JsonSerializer.Serialize(config, OnnxRuntimeJsonContext.Default.OnnxRuntimeProviderConfig);

        json.Should().Contain("\"enableStructuredToolCalling\":true");
    }

    [SkippableFact]
    public async Task LiveModel_WithStructuredToolCalling_ReturnsFunctionCallContent()
    {
        Skip.IfNot(
            string.Equals(global::System.Environment.GetEnvironmentVariable("ONNX_TOOL_CALL_SMOKE"), "1", StringComparison.Ordinal),
            "Set ONNX_TOOL_CALL_SMOKE=1 to run the local ONNX structured tool-call smoke.");

        var modelPath = global::System.Environment.GetEnvironmentVariable("ONNX_MODEL_PATH");
        Skip.If(
            string.IsNullOrWhiteSpace(modelPath),
            "Set ONNX_MODEL_PATH to a real ONNX Runtime GenAI model directory.");
        Skip.IfNot(
            Directory.Exists(modelPath),
            $"ONNX_MODEL_PATH does not point to an existing directory: {modelPath}");

        var provider = new OnnxRuntimeProvider();
        var config = new ClientProviderConfig
        {
            ProviderKey = "onnx-runtime",
            ModelName = global::System.Environment.GetEnvironmentVariable("ONNX_MODEL_NAME") ?? "local-onnx-runtime"
        };
        config.SetProviderConfig(new OnnxRuntimeProviderConfig
        {
            ModelPath = modelPath,
            EnableStructuredToolCalling = true,
            MaxLength = ReadPositiveInt("ONNX_TOOL_CALL_MAX_LENGTH") ?? 96,
            Temperature = 0
        });

        using var client = provider.CreateChatClient(config);
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(ReadPositiveInt("ONNX_TOOL_CALL_TIMEOUT_SECONDS") ?? 180));

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(
                    ChatRole.User,
                    global::System.Environment.GetEnvironmentVariable("ONNX_TOOL_CALL_PROMPT") ??
                    "Use the Add tool to add 2 and 3. Return only the tool call.")
            ],
            new ChatOptions
            {
                AllowMultipleToolCalls = false,
                MaxOutputTokens = ReadPositiveInt("ONNX_TOOL_CALL_OUTPUT_TOKENS") ?? 64,
                Temperature = 0,
                ToolMode = ChatToolMode.RequireSpecific("Add"),
                Tools = [CreateAddTool()]
            },
            cts.Token);

        var toolCall = response.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .SingleOrDefault();

        toolCall.Should().NotBeNull("raw ONNX response text was: {0}", response.Text);
        toolCall!.Name.Should().Be("Add");
        toolCall.Arguments.Should().ContainKey("a").WhoseValue.Should().Be(2);
        toolCall.Arguments.Should().ContainKey("b").WhoseValue.Should().Be(3);
    }

    [SkippableFact]
    public async Task LiveAgent_WithOnnxStructuredToolCalling_ExecutesTool()
    {
        Skip.IfNot(
            string.Equals(global::System.Environment.GetEnvironmentVariable("ONNX_TOOL_CALL_SMOKE"), "1", StringComparison.Ordinal),
            "Set ONNX_TOOL_CALL_SMOKE=1 to run the local ONNX structured tool-call smoke.");

        var modelPath = global::System.Environment.GetEnvironmentVariable("ONNX_MODEL_PATH");
        Skip.If(
            string.IsNullOrWhiteSpace(modelPath),
            "Set ONNX_MODEL_PATH to a real ONNX Runtime GenAI model directory.");
        Skip.IfNot(
            Directory.Exists(modelPath),
            $"ONNX_MODEL_PATH does not point to an existing directory: {modelPath}");

        var agent = await new AgentBuilder()
            .WithName("onnx-tool-smoke")
            .WithInstructions("Use tools when the user asks for arithmetic.")
            .WithOnnxRuntime(
                modelPath,
                options =>
                {
                    options.EnableStructuredToolCalling = true;
                    options.MaxLength = ReadPositiveInt("ONNX_TOOL_CALL_MAX_LENGTH") ?? 128;
                    options.Temperature = 0;
                })
            .WithNativeFunction(CreateAddTool())
            .WithMaxFunctionCallTurns(1)
            .WithOptionsConfiguration(options =>
            {
                options.AllowMultipleToolCalls = false;
                options.MaxOutputTokens = ReadPositiveInt("ONNX_TOOL_CALL_OUTPUT_TOKENS") ?? 96;
                options.Temperature = 0;
                options.ToolMode = ChatToolMode.RequireSpecific("Add");
            })
            .BuildAsync(CancellationToken.None);

        var starts = new List<ToolCallStartEvent>();
        var results = new List<ToolCallResultEvent>();
        var resultSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var startSubscription = agent.Subscribe<ToolCallStartEvent>(evt =>
        {
            starts.Add(evt);
        });
        using var resultSubscription = agent.Subscribe<ToolCallResultEvent>(evt =>
        {
            results.Add(evt);
            resultSeen.TrySetResult();
        });
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(ReadPositiveInt("ONNX_TOOL_CALL_TIMEOUT_SECONDS") ?? 180));

        var runTask = agent.RunAsync(
            global::System.Environment.GetEnvironmentVariable("ONNX_TOOL_CALL_PROMPT") ??
            "Use the Add tool to add 2 and 3. Return only the tool call.",
            cancellationToken: cts.Token);

        var completed = await Task.WhenAny(
            runTask,
            resultSeen.Task,
            Task.Delay(TimeSpan.FromSeconds(ReadPositiveInt("ONNX_TOOL_CALL_EVENT_TIMEOUT_SECONDS") ?? 60), cts.Token));

        if (completed == resultSeen.Task)
        {
            await cts.CancelAsync();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        else if (completed == runTask)
        {
            await runTask;
        }
        else
        {
            await cts.CancelAsync();
            throw new TimeoutException("The ONNX live agent smoke did not emit a tool result before the event timeout.");
        }

        starts.Should().ContainSingle(evt => evt.Name == "Add");
        results.Should().ContainSingle(evt => evt.Name == "Add");
        results.Single().Result.Text.Should().Contain("5");
    }

    [SkippableFact]
    public async Task LiveAgent_WithOnnxStructuredToolCalling_ExecutesToolsAcrossMultipleUserTurns()
    {
        Skip.IfNot(
            string.Equals(global::System.Environment.GetEnvironmentVariable("ONNX_TOOL_CALL_SMOKE"), "1", StringComparison.Ordinal),
            "Set ONNX_TOOL_CALL_SMOKE=1 to run the local ONNX structured tool-call smoke.");

        var modelPath = global::System.Environment.GetEnvironmentVariable("ONNX_MODEL_PATH");
        Skip.If(
            string.IsNullOrWhiteSpace(modelPath),
            "Set ONNX_MODEL_PATH to a real ONNX Runtime GenAI model directory.");
        Skip.IfNot(
            Directory.Exists(modelPath),
            $"ONNX_MODEL_PATH does not point to an existing directory: {modelPath}");

        var agent = await CreateLiveOnnxToolAgentAsync(modelPath);

        var first = await RunUntilAddResultAsync(agent, "Use the Add tool to add 2 and 3. Return only the tool call.");
        var second = await RunUntilAddResultAsync(agent, "Use the Add tool to add 4 and 5. Return only the tool call.");

        first.Result.Text.Should().Contain("5");
        second.Result.Text.Should().Contain("9");
    }

    private static AIFunction CreateAddTool()
    {
        return HPDAIFunctionFactory.Create(
            (args, _, _) => Task.FromResult<object?>(ReadNumber(args, "a") + ReadNumber(args, "b")),
            new HPDAIFunctionFactoryOptions
            {
                Name = "Add",
                Description = "Adds two numbers.",
                SchemaProvider = () =>
                {
                    using var document = JsonDocument.Parse("""
                        {
                          "type": "object",
                          "properties": {
                            "a": { "type": "number" },
                            "b": { "type": "number" }
                          },
                          "required": ["a", "b"]
                        }
                        """);
                    return document.RootElement.Clone();
                }
            });
    }

    private static Task<Agent> CreateLiveOnnxToolAgentAsync(string modelPath)
        => new AgentBuilder()
            .WithName("onnx-tool-smoke")
            .WithInstructions("Use tools when the user asks for arithmetic.")
            .WithOnnxRuntime(
                modelPath,
                options =>
                {
                    options.EnableStructuredToolCalling = true;
                    options.MaxLength = ReadPositiveInt("ONNX_TOOL_CALL_MAX_LENGTH") ?? 128;
                    options.Temperature = 0;
                })
            .WithNativeFunction(CreateAddTool())
            .WithMaxFunctionCallTurns(1)
            .WithOptionsConfiguration(options =>
            {
                options.AllowMultipleToolCalls = false;
                options.MaxOutputTokens = ReadPositiveInt("ONNX_TOOL_CALL_OUTPUT_TOKENS") ?? 96;
                options.Temperature = 0;
                options.ToolMode = ChatToolMode.RequireSpecific("Add");
            })
            .BuildAsync(CancellationToken.None);

    private static async Task<ToolCallResultEvent> RunUntilAddResultAsync(Agent agent, string prompt)
    {
        var results = new List<ToolCallResultEvent>();
        var resultSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var resultSubscription = agent.Subscribe<ToolCallResultEvent>(evt =>
        {
            if (evt.Name == "Add")
            {
                results.Add(evt);
                resultSeen.TrySetResult();
            }
        });
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(ReadPositiveInt("ONNX_TOOL_CALL_TIMEOUT_SECONDS") ?? 120));

        var runTask = agent.RunAsync(prompt, cancellationToken: cts.Token);
        var completed = await Task.WhenAny(
            runTask,
            resultSeen.Task,
            Task.Delay(TimeSpan.FromSeconds(ReadPositiveInt("ONNX_TOOL_CALL_EVENT_TIMEOUT_SECONDS") ?? 60), cts.Token));

        if (completed == resultSeen.Task)
        {
            await cts.CancelAsync();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        else if (completed == runTask)
        {
            await runTask;
        }
        else
        {
            await cts.CancelAsync();
            throw new TimeoutException("The ONNX live agent smoke did not emit a tool result before the event timeout.");
        }

        return results.Should().ContainSingle(evt => evt.Name == "Add").Subject;
    }

    private static int ReadNumber(AIFunctionArguments args, string name)
    {
        var json = args.GetJson();
        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty(name, out var jsonValue) &&
            jsonValue.ValueKind == JsonValueKind.Number &&
            jsonValue.TryGetInt32(out var jsonInt))
        {
            return jsonInt;
        }

        return args.TryGetValue(name, out var value) switch
        {
            true when value is int intValue => intValue,
            true when value is long longValue => checked((int)longValue),
            true when value is double doubleValue => checked((int)doubleValue),
            true when value is decimal decimalValue => checked((int)decimalValue),
            true when value is JsonElement { ValueKind: JsonValueKind.Number } element &&
                      element.TryGetInt32(out var elementInt) => elementInt,
            _ => throw new InvalidOperationException($"Missing numeric argument '{name}'.")
        };
    }

    private sealed class CapturingChatClient : IChatClient
    {
        private readonly ChatResponse _response;

        public CapturingChatClient(ChatResponse response)
        {
            _response = response;
        }

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
            return Task.FromResult(_response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            foreach (var update in _response.ToChatResponseUpdates())
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

    private static int? ReadPositiveInt(string variableName)
        => int.TryParse(global::System.Environment.GetEnvironmentVariable(variableName), out var value) && value > 0
            ? value
            : null;
}
