using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.AotFixture;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

if (!ValidationMetadataRoundTrips())
    return 2;
if (!SubAgentHarnessOverridesRoundTrip())
    return 3;

var harnessClient = new HarnessClient();
var runConfig = new AgentRunConfig();
runConfig.Harnesses.For<DerivedExecutionHarness>().Override(
    new ExternalExecutionSettingsOverrides { Value = 42 });

await using (var agent = await new AgentBuilder(new AgentConfig
    {
        Name = "cross-assembly-toolharness-aot",
        MaxAgenticIterations = 5
    })
    .WithChatClient(harnessClient)
    .WithEventComposition(CoreAgentEventComposition.Instance)
    .WithAPIConfiguration(new ConfigurationBuilder().Build())
    .WithToolHarness<DerivedExecutionHarness>(
        options => options.Configure(new ExternalExecutionSettings { Value = 41 }))
    .WithToolHarness<ExternalEagerHarness>()
    .BuildAsync())
{
    await agent.CreateSessionAsync("aot-session");
    await agent.RunAsync(new UserMessagesInputEvent
    {
        SessionId = "aot-session",
        ThreadId = "main",
        RunConfig = runConfig,
        Messages = [new(ChatRole.User, "run-cross-assembly-harness")]
    });
}

var success = ExternalExecutionMiddleware.CreatedCount == 1 &&
    ExternalExecutionMiddleware.ActivatedCount == 1 &&
    ExternalExecutionMiddleware.DisposedCount == 1 &&
    ExternalEagerMiddleware.ActivatedCount == 1 &&
    ExternalEagerMiddleware.TurnedCount == 1 &&
    ExternalEagerMiddleware.DeactivatedCount == 1 &&
    ExternalExecutionHarness.LastConfiguredValue == 42 &&
    harnessClient.ContentReadText == "EVIDENCE-AOT";
if (!success)
{
    Console.Error.WriteLine(
        $"created={ExternalExecutionMiddleware.CreatedCount} activated={ExternalExecutionMiddleware.ActivatedCount} " +
        $"disposed={ExternalExecutionMiddleware.DisposedCount} eager={ExternalEagerMiddleware.ActivatedCount}/" +
        $"{ExternalEagerMiddleware.TurnedCount}/{ExternalEagerMiddleware.DeactivatedCount} " +
        $"configured={ExternalExecutionHarness.LastConfiguredValue} read={harnessClient.ContentReadText ?? "<null>"}");
}
return success ? 0 : 1;

static bool ValidationMetadataRoundTrips()
{
    using var attempted = JsonDocument.Parse("\"status\"");
    var response = new ValidationErrorResponse
    {
        Errors =
        [
            new ValidationError
            {
                Property = "request.action",
                ErrorCode = "unknown_union_discriminator",
                ErrorMessage = "Unsupported discriminator value.",
                AttemptedValue = attempted.RootElement.Clone(),
                AllowedValues = ["launch", "listSessions"]
            }
        ]
    };
    var json = JsonSerializer.SerializeToElement(response, HPDJsonContext.Default.ValidationErrorResponse);
    var error = json.GetProperty("errors")[0];
    return error.GetProperty("attempted_value").GetString() == "status" &&
        error.GetProperty("allowed_values")[0].GetString() == "launch" &&
        error.GetProperty("allowed_values")[1].GetString() == "listSessions";
}

static bool SubAgentHarnessOverridesRoundTrip()
{
    var child = new SubAgentRunConfig();
    child.Harnesses.For<DerivedExecutionHarness>().Override(
        new ExternalExecutionSettingsOverrides { Value = 43 });
    var descendantDefaults = new SubAgentRunDefaults();
    descendantDefaults.Harnesses.For<DerivedExecutionHarness>().Override(
        new ExternalExecutionSettingsOverrides { Value = 44 });
    var policy = SubAgentExecutionPolicy.Create(
        child,
        new AgentClientsConfig { Chat = new ChatClientConfig { ModelName = "child" } },
        new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource>
        {
            [ProviderClientFamily.Chat] = SubAgentClientSelectionSource.InputSubAgentRun
        },
        new AgentSecurityRunConfig(),
        new NoSubAgentClientPropagation(),
        descendantDefaults,
        harnessOverrideLayers: [child.Harnesses]);
    var restored = JsonSerializer.Deserialize(
        JsonSerializer.Serialize(policy, AgentEventJsonContext.Default.SubAgentExecutionPolicy),
        AgentEventJsonContext.Default.SubAgentExecutionPolicy)!;
    return restored.InitialRunConfig!.Harnesses.Count == 1 &&
        restored.HarnessOverrideLayers is { Count: 1 } &&
        restored.DescendantDefaults?.Harnesses.Count == 1;
}

internal sealed class HarnessClient : IChatClient
{
    private readonly ConcurrentDictionary<string, int> _stages = new(StringComparer.Ordinal);
    public string? ContentReadText { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var key = chatMessages
            .Where(message => message.Role == ChatRole.User)
            .SelectMany(message => message.Contents)
            .OfType<TextContent>()
            .Select(content => content.Text)
            .First();
        var stage = _stages.AddOrUpdate(key, 1, static (_, current) => current + 1);
        return Task.FromResult(stage switch
        {
            1 => ToolCall(nameof(DerivedExecutionHarness), "expand"),
            2 => ToolCall(nameof(DerivedExecutionHarness.DerivedPing), "expand-function"),
            3 => ToolCall(nameof(DerivedExecutionHarness.ConfiguredValue), "configured"),
            4 => ToolCall(nameof(DerivedExecutionHarness.DerivedPing), "ping"),
            5 => ToolCall(nameof(ExternalExecutionHarness.OffloadEvidence), "offload"),
            6 => ContentReadCall(chatMessages),
            _ => CompleteAfterContentRead(chatMessages)
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        foreach (var message in response.Messages)
        {
            yield return new ChatResponseUpdate
            {
                Role = message.Role,
                Contents = message.Contents,
                FinishReason = message.Contents.OfType<FunctionCallContent>().Any()
                    ? ChatFinishReason.ToolCalls
                    : ChatFinishReason.Stop
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ProviderClientExecutionIdentity)
            ? new ProviderClientExecutionIdentity
            {
                ProviderKey = "test",
                BackendKey = "harness-aot",
                Family = ProviderClientFamily.Chat,
                OperationAdapterKey = "aot-smoke",
                UsageSemanticsKey = "test",
                SafeConfigurationFingerprint = new string('0', 64)
            }
            : null;
    public void Dispose() { }

    private static ChatResponse ToolCall(string name, string callId) => new(
        [new ChatMessage(ChatRole.Assistant,
            [(AIContent)new FunctionCallContent(callId, name, new Dictionary<string, object?>())])]);

    private static ChatResponse ContentReadCall(IEnumerable<ChatMessage> messages)
    {
        var offloadResult = messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .Last(content => content.CallId == "offload")
            .Result;
        if (offloadResult is not JsonElement json)
            throw new InvalidOperationException("Offload result was not normalized to JSON.");
        if (!json.TryGetProperty("evidence", out var evidence))
            throw new InvalidOperationException($"Offload result did not contain evidence: {json.GetRawText()}");
        var contentId = evidence.GetProperty("contentId").GetString()
            ?? throw new InvalidOperationException("Offload result did not contain a content id.");
        var request = new JsonObject
        {
            ["request"] = new JsonObject
            {
                ["action"] = "read",
                ["contentId"] = contentId,
                ["part"] = "evidence",
                ["startLine"] = 0,
                ["endLine"] = 0
            }
        };
        var arguments = new AIFunctionArguments();
        arguments["request"] = JsonSerializer.SerializeToElement(
            request["request"],
            HPDJsonContext.Default.JsonObject);
        arguments.SetJson(JsonSerializer.SerializeToElement(
            request,
            HPDJsonContext.Default.JsonObject));
        return new ChatResponse(
            [new ChatMessage(ChatRole.Assistant,
                [(AIContent)new FunctionCallContent("content-read", "Content", arguments)])]);
    }

    private ChatResponse CompleteAfterContentRead(IEnumerable<ChatMessage> messages)
    {
        var result = messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .Last(content => content.CallId == "content-read")
            .Result;
        if (result is JsonElement json)
            ContentReadText = json.GetProperty("text").GetString();
        else if (result is string text)
            ContentReadText = text;
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]);
    }
}
