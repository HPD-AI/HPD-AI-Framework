using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.MCP;
using HPD.Agent.Permissions;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Events;
using HPD.Serialization;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Extensions.Tasks;

const string manifestJson = """
{"servers":[{"name":"modern","transport":"http","endpoint":"https://example.test/mcp","enableResources":true,"enablePrompts":true}]}
""";
var manifest = JsonSerializer.Deserialize(
    manifestJson, McpJsonSerializerContext.Default.McpManifest)
    ?? throw new InvalidOperationException("Manifest did not deserialize.");
manifest.Validate();
var roundTrip = JsonSerializer.Serialize(manifest, McpJsonSerializerContext.Default.McpManifest);
if (!roundTrip.Contains("modern", StringComparison.Ordinal))
    throw new InvalidOperationException("Manifest round trip lost the server.");

var projection = new McpResourceListResult { Server = "modern" };
_ = JsonSerializer.SerializeToElement(
    projection, McpJsonSerializerContext.Default.McpResourceListResult);

var recovery = new McpTaskRecoveryReference("modern", "task-1");
var recoveryJson = JsonSerializer.Serialize(
    recovery, McpTaskRecoveryJsonContext.Default.McpTaskRecoveryReference);
var restoredRecovery = JsonSerializer.Deserialize(
    recoveryJson, McpTaskRecoveryJsonContext.Default.McpTaskRecoveryReference);
if (restoredRecovery != recovery)
    throw new InvalidOperationException("Task recovery reference did not round trip.");
if (McpTaskProvider.MapStatus(McpTaskStatus.Completed) != AgentOperationProviderStatus.Completed)
    throw new InvalidOperationException("Task status projection failed.");

var smokeDirectory = Path.Combine(Path.GetTempPath(), $"hpd-mcp-aot-{Guid.NewGuid():N}");
var configDirectory = Path.Combine(smokeDirectory, "config");
Directory.CreateDirectory(configDirectory);
var agentConfigPath = Path.Combine(smokeDirectory, "agent.yaml");
File.WriteAllText(
    agentConfigPath,
    """
    name: McpAotSmoke
    mcp:
      servers:
        - name: inline
          transport: http
          endpoint: https://inline.test/mcp
          allowedTools:
            - inline_echo
          permission:
            policy: hpd.smoke.inline-policy
      manifests:
        - path: ./config/mcp.json
          sourceName: external
    """);
File.WriteAllText(
    Path.Combine(configDirectory, "mcp.json"),
    """
    {
      "servers": [
        {
          "name": "external",
          "transport": "http",
          "endpoint": "https://external.test/mcp",
          "allowedTools": ["external_echo"],
          "permission": {
            "policy": "hpd.smoke.confirm-policy",
            "interaction": "hpd.smoke.confirmation"
          }
        }
      ]
    }
    """);

try
{
    var agentConfig = HpdAgentConfigSerializer.ReadFile(agentConfigPath)
        ?? throw new InvalidOperationException("Agent MCP configuration did not deserialize.");
    if (agentConfig.Mcp?.Servers.Count != 1 || agentConfig.Mcp.Manifests.Count != 1)
        throw new InvalidOperationException("Agent MCP configuration lost declarations.");

    var handler = new SmokeMcpHandler();
    using var inlineHttp = new HttpClient(handler, disposeHandler: false);
    using var externalHttp = new HttpClient(handler, disposeHandler: false);
    var client = new SmokeMcpChatClient();
    await using (var agent = await new AgentBuilder(agentConfig)
        .WithEventComposition(GeneratedAgentEventComposition.Composition)
        .WithChatClient(client)
        .WithPermissions()
        .WithMcpOptions(options => options.HttpClientFactory = server => server.Name == "inline"
            ? inlineHttp
            : externalHttp)
        .BuildAsync())
    {
        using var interaction = agent.EventCoordinator.Subscribe<SmokePermissionRequestEvent>(request =>
        {
            handler.RecordInteraction();
            agent.EventCoordinator.Respond(new SmokePermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                "allow_once"));
            return ValueTask.CompletedTask;
        });
        var result = await agent.RunAsync("Invoke both MCP declarations.");
        if (result.Text != "Both MCP tools completed." ||
            !handler.InvokedTools.Contains("inline_echo", StringComparer.Ordinal) ||
            !handler.InvokedTools.Contains("external_echo", StringComparer.Ordinal) ||
            handler.ToolsListRequests != 2 ||
            handler.InteractionCount != 1)
            throw new InvalidOperationException(
                $"Native AOT MCP execution failed. Text='{result.Text}', " +
                $"tools/list={handler.ToolsListRequests}, " +
                $"invoked=[{string.Join(", ", handler.InvokedTools)}], " +
                $"interactions={handler.InteractionCount}, " +
                $"turn-tools={result.ToolCalls.Count}, " +
                $"tool-results=[{string.Join(" | ", client.ToolResults)}].");
    }
}
finally
{
    Directory.Delete(smokeDirectory, recursive: true);
}

Console.WriteLine("HPD-Agent.MCP AOT smoke passed.");

sealed class SmokeMcpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<string> _invokedTools = [];
    private int _toolsListRequests;
    private int _interactionCount;

    internal IReadOnlyList<string> InvokedTools
    {
        get { lock (_gate) return _invokedTools.ToArray(); }
    }

    internal int ToolsListRequests => Volatile.Read(ref _toolsListRequests);
    internal int InteractionCount => Volatile.Read(ref _interactionCount);
    internal void RecordInteraction() => Interlocked.Increment(ref _interactionCount);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var method = root.GetProperty("method").GetString();
        var serverName = request.RequestUri!.Host == "inline.test" ? "inline" : "external";
        var toolName = serverName + "_echo";

        if (method == "server/discover")
        {
            return JsonResponse(
                root,
                """
                {"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"__SERVER__","version":"1"}},"ttlMs":0,"cacheScope":"private"}
                """.Replace("__SERVER__", serverName, StringComparison.Ordinal));
        }
        if (method == "tools/list")
        {
            Interlocked.Increment(ref _toolsListRequests);
            return JsonResponse(
                root,
                """
                {"resultType":"complete","tools":[{"name":"__TOOL__","description":"__TOOL__","inputSchema":{"type":"object","properties":{}}}],"ttlMs":0,"cacheScope":"private"}
                """.Replace("__TOOL__", toolName, StringComparison.Ordinal));
        }
        if (method == "tools/call")
        {
            var parameters = root.GetProperty("params");
            if (parameters.TryGetProperty("arguments", out var arguments) &&
                arguments.ValueKind == JsonValueKind.Object &&
                arguments.EnumerateObject().Any(static property =>
                    property.Name.StartsWith("__", StringComparison.Ordinal)))
                throw new InvalidOperationException("HPD metadata leaked into external MCP arguments.");
            var actualTool = parameters.GetProperty("name").GetString()!;
            lock (_gate)
                _invokedTools.Add(actualTool);
            return JsonResponse(
                root,
                """
                {"resultType":"complete","content":[{"type":"text","text":"__TOOL__ result"}]}
                """.Replace("__TOOL__", actualTool, StringComparison.Ordinal));
        }
        throw new InvalidOperationException($"Unexpected MCP method '{method}'.");
    }

    private static HttpResponseMessage JsonResponse(JsonElement request, string result)
    {
        var id = request.GetProperty("id").GetRawText();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":__ID__,"result":__RESULT__}"""
                    .Replace("__ID__", id, StringComparison.Ordinal)
                    .Replace("__RESULT__", result, StringComparison.Ordinal),
                Encoding.UTF8,
                "application/json")
        };
    }
}

sealed class SmokeMcpChatClient : IChatClient
{
    private readonly List<string> _toolResults = [];
    private int _calls;

    internal IReadOnlyList<string> ToolResults => _toolResults;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The MCP smoke uses streaming.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        var call = ++_calls;
        _toolResults.AddRange(messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(static result => result.Result?.ToString() ?? "<null>"));
        if (call <= 2)
        {
            var names = options?.Tools?.OfType<AIFunction>()
                .Select(static function => function.Name)
                .ToHashSet(StringComparer.Ordinal) ?? [];
            if (!names.Contains("inline_echo") || !names.Contains("external_echo"))
                throw new InvalidOperationException("Declarative MCP tools were not visible to the model.");
            yield return new ChatResponseUpdate
            {
                Contents =
                [
                    new FunctionCallContent(
                        call == 1 ? "call-inline" : "call-external",
                        call == 1 ? "inline_echo" : "external_echo",
                        new Dictionary<string, object?>())
                ],
                FinishReason = ChatFinishReason.ToolCalls
            };
            yield break;
        }

        yield return new ChatResponseUpdate
        {
            Contents = [new TextContent("Both MCP tools completed.")],
            FinishReason = ChatFinishReason.Stop
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(ProviderStreamingUsageSemanticsDeclaration)
            ? new ProviderStreamingUsageSemanticsDeclaration(
                ProviderClientFamily.Chat,
                UsageUpdateSemantics.FinalOnly,
                "smoke",
                "mcp-aot-final-only")
            : serviceType == typeof(ProviderClientExecutionIdentity)
                ? new ProviderClientExecutionIdentity
                {
                    ProviderKey = "smoke",
                    BackendKey = "smoke",
                    Family = ProviderClientFamily.Chat,
                    ModelName = "smoke",
                    OperationAdapterKey = "smoke/chat",
                    UsageSemanticsKey = "smoke/final",
                    SafeConfigurationFingerprint = "smoke"
                }
                : null;

    public void Dispose()
    {
    }
}

[PermissionPresentation("hpd.smoke.permission-presentation", typeof(SmokePermissionJsonContext))]
public sealed record SmokePermissionPresentation(string Message);

[PermissionPolicyDefinition("hpd.smoke.inline-policy")]
public sealed class SmokeInlinePermissionPolicy :
    PermissionPolicy<SmokePermissionPresentation>
{
    public override ValueTask<PermissionEvaluation> EvaluateAsync(
        ValidatedPermissionInput input,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(SmokePermissionEvaluationFactory.Create(
            context,
            immediateAllow: true,
            "MCP read policy"));
}

[PermissionPolicyDefinition("hpd.smoke.confirm-policy")]
public sealed class SmokeConfirmPermissionPolicy :
    PermissionPolicy<SmokePermissionPresentation>
{
    public override ValueTask<PermissionEvaluation> EvaluateAsync(
        ValidatedPermissionInput input,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(SmokePermissionEvaluationFactory.Create(
            context,
            immediateAllow: false,
            "MCP confirmation policy"));
}

[PermissionInteractionDefinition("hpd.smoke.confirmation")]
public sealed class SmokePermissionInteraction :
    IPermissionInteractionEventContract<SmokePermissionRequestEvent, SmokePermissionResponseEvent>
{
    public async ValueTask<PermissionDecision> RequestAsync(
        PermissionInteractionContext context,
        PermissionEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var response = await context.RequestAsync<SmokePermissionRequestEvent, SmokePermissionResponseEvent>(
            new SmokePermissionRequestEvent(
                context.PermissionId,
                nameof(SmokePermissionInteraction),
                context.FunctionName,
                context.FunctionCallId),
            cancellationToken).ConfigureAwait(false);
        var selected = evaluation.Choices.Items.SingleOrDefault(choice =>
            string.Equals(choice.Id, response.ChoiceId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"MCP smoke permission response selected unknown choice '{response.ChoiceId}'.");
        return new PermissionDecision
        {
            Kind = selected.Decision,
            ChoiceId = selected.Id
        };
    }
}

[DurableEvent]
[EventType("HPD_SMOKE_PERMISSION_REQUEST")]
public sealed record SmokePermissionRequestEvent(
    string PermissionId,
    string SourceName,
    string FunctionName,
    string CallId) :
    AgentEvent,
    IAgentRequestEvent<SmokePermissionResponseEvent>
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
    public string RequestId => PermissionId;
}

[DurableEvent]
[EventType("HPD_SMOKE_PERMISSION_RESPONSE")]
public sealed record SmokePermissionResponseEvent(
    string PermissionId,
    string SourceName,
    string ChoiceId,
    string? FeedbackText = null) :
    AgentEvent,
    IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;
    public string RequestId => PermissionId;
}

[JsonSerializable(typeof(SmokePermissionPresentation))]
[JsonSerializable(typeof(SmokePermissionRequestEvent))]
[JsonSerializable(typeof(SmokePermissionResponseEvent))]
internal partial class SmokePermissionJsonContext : JsonSerializerContext;

internal static class SmokePermissionEvaluationFactory
{
    internal static PermissionEvaluation Create(
        PermissionEvaluationContext context,
        bool immediateAllow,
        string message) => new()
    {
        PolicyId = immediateAllow
            ? "hpd.smoke.inline-policy"
            : "hpd.smoke.confirm-policy",
        PolicyRevision = "1",
        Authority = context.Authority,
        Title = "MCP permission",
        Risk = PermissionRisk.Medium,
        Choices = new PermissionChoiceSet
        {
            Items =
            [
                new PermissionChoiceDescriptor
                {
                    Id = "allow_once",
                    Label = "Allow once",
                    Decision = PermissionDecisionKind.Allow
                },
                new PermissionChoiceDescriptor
                {
                    Id = "deny_once",
                    Label = "Deny",
                    Decision = PermissionDecisionKind.Deny
                }
            ]
        },
        Presentation = new SmokePermissionPresentation(message),
        ImmediateDecision = immediateAllow
            ? new PermissionDecision
            {
                Kind = PermissionDecisionKind.Allow,
                ChoiceId = "allow_once"
            }
            : null
    };
}
