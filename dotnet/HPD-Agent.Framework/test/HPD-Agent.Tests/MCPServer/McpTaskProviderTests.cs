using FluentAssertions;
using HPD.Agent.MCP;
using HPD.Agent.MCP.Tasks;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpTaskProviderTests
{
    [Fact]
    public async Task RemoteTask_CreatesObservesUpdatesCancelsAndProjectsUnifiedOperation()
    {
        var handler = new TaskProtocolHandler();
        using var http = new HttpClient(handler);
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "tasks",
                Endpoint = new Uri("https://tasks.test/mcp")
            }, http, null, ownsHttpClient: false),
            new McpClientOptions());
        var sink = new TestEventSink();
        await using var registry = new AgentOperationRegistry(sink);
        var provider = new McpTaskProvider(registry, null);

        var started = await provider.StartAsync(
            client,
            "tasks",
            new CallToolRequestParams { Name = "long-job" },
            new AgentExecutionAddress("agent", "session", "thread"),
            null,
            "execution-1",
            null,
            default);

        Assert.NotNull(started.Receipt);
        Assert.Equal("task-1", started.Receipt!.ProviderOperationId);
        Assert.NotEqual(started.Receipt.OperationId, started.Receipt.ProviderOperationId);
        handler.Status = "input_required";
        await WaitUntilAsync(() => registry.Snapshot().Single().ProviderStatus ==
            AgentOperationProviderStatus.InputRequired);

        using var input = JsonDocument.Parse("true");
        await registry.SupplyInputAsync(started.Receipt.OperationId, new AgentOperationInput
        {
            Responses = new Dictionary<string, JsonElement>
            {
                ["approval"] = input.RootElement.Clone()
            }
        }, default);
        Assert.Contains("tasks/update", handler.Methods);

        await registry.RequestCancellationAsync(started.Receipt.OperationId, default);
        Assert.Contains("tasks/cancel", handler.Methods);
        handler.Status = "cancelled";
        await WaitUntilAsync(() => registry.Snapshot().Single().ProviderStatus ==
            AgentOperationProviderStatus.Cancelled);

        var snapshot = registry.Snapshot().Single();
        Assert.Equal(AgentOperationObservationStatus.Attached, snapshot.ObservationStatus);
        Assert.Null(snapshot.Recovery);
        Assert.Contains(sink.Events, static evt => evt is AgentOperationRegisteredEvent);
        Assert.Contains(sink.Events, static evt => evt is AgentOperationTransitionedEvent);
    }

    [Fact]
    public async Task RemoteTask_RecoversObservationThroughOwningMcpRevision()
    {
        var handler = new TaskProtocolHandler();
        var protector = new PassthroughProtector();
        var options = new McpOptions
        {
            HttpClientFactory = _ => new HttpClient(handler)
        };
        options.Invocation.RecoveryReferenceProtector = protector;
        options.AddTasksExtension();
        var factory = new McpCapabilitySourceFactory(
            CapabilitySourceId.Create("mcp.tasks:recovery"),
            null,
            """{"servers":[{"name":"tasks","transport":"http","endpoint":"https://tasks.test/mcp"}]}""",
            options,
            null,
            null);
        await using var source = await factory.CreateAsync(null, default);
        var loaded = await source.LoadAsync(new CapabilityLoadContext(1, null), default);
        await using var catalog = new AgentCapabilityCatalog(1, [loaded.Owner]);
        await using var registry = new AgentOperationRegistry(new TestEventSink());
        var now = DateTimeOffset.UtcNow;
        var recoveryJson = JsonSerializer.Serialize(
            new McpTaskRecoveryReference("tasks", "task-1"),
            McpTaskRecoveryJsonContext.Default.McpTaskRecoveryReference);
        var detached = new AgentOperationSnapshot
        {
            OperationId = "recovered-op",
            ProviderOperationId = "task-1",
            SourceKind = AgentOperationSourceKind.McpTask,
            Name = "long-job",
            Address = new AgentExecutionAddress("agent", "session", "thread"),
            ProviderStatus = AgentOperationProviderStatus.Running,
            ObservationStatus = AgentOperationObservationStatus.Detached,
            Control = new AgentOperationControl("task-1", AgentOperationKind.Provider,
                AgentOperationCapabilities.Cancel | AgentOperationCapabilities.Update |
                AgentOperationCapabilities.Reconcile),
            Notification = new AgentOperationNotificationPolicy(),
            RegisteredAt = now,
            UpdatedAt = now,
            Recovery = new AgentOperationRecoveryReference("mcp-task-v1", recoveryJson),
            Version = 1
        };
        await registry.RehydrateAsync([new AgentOperationRegisteredEvent { Operation = detached }]);
        Assert.True(registry.TryGet(detached.OperationId, out var operation));

        await catalog.ReconcileAsync([operation!], default);

        Assert.Equal(AgentOperationObservationStatus.Attached, operation!.Snapshot.ObservationStatus);
        Assert.Contains("tasks/get", handler.Methods);
    }

    [Fact]
    public void AddTasksExtension_IsTheOnlyTasksActivationBoundary()
    {
        var options = new McpOptions();

        options.AddTasksExtension().Should().BeSameAs(options);

        options.Invocation.EnableRemoteTasks.Should().BeTrue();
        options.Invocation.RemoteTaskAdapter.Should().NotBeNull();
    }

    [Theory]
    [InlineData(McpTaskStatus.Working, AgentOperationProviderStatus.Running)]
    [InlineData(McpTaskStatus.InputRequired, AgentOperationProviderStatus.InputRequired)]
    [InlineData(McpTaskStatus.Completed, AgentOperationProviderStatus.Completed)]
    [InlineData(McpTaskStatus.Failed, AgentOperationProviderStatus.Failed)]
    [InlineData(McpTaskStatus.Cancelled, AgentOperationProviderStatus.Cancelled)]
    public void MapStatus_PreservesProviderState(
        McpTaskStatus remote,
        AgentOperationProviderStatus expected)
    {
        McpTaskProvider.MapStatus(remote).Should().Be(expected);
    }

    [Fact]
    public void RecoveryReference_RoundTripsWithSourceGeneratedMetadata()
    {
        var expected = new McpTaskRecoveryReference("search", "remote-42");

        var json = System.Text.Json.JsonSerializer.Serialize(
            expected,
            McpTaskRecoveryJsonContext.Default.McpTaskRecoveryReference);
        var actual = System.Text.Json.JsonSerializer.Deserialize(
            json,
            McpTaskRecoveryJsonContext.Default.McpTaskRecoveryReference);

        actual.Should().Be(expected);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class TestEventSink : IAgentOperationEventSink
    {
        internal List<AgentEvent> Events { get; } = [];

        public ValueTask AppendAsync(AgentEvent operationEvent, CancellationToken cancellationToken)
        {
            Events.Add(operationEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TaskProtocolHandler : HttpMessageHandler
    {
        internal ConcurrentQueue<string> Methods { get; } = [];
        internal volatile string Status = "working";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Methods.Enqueue(method);
            var result = method switch
            {
                "server/discover" => """{"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{},"extensions":{"io.modelcontextprotocol/tasks":{}}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"tasks","version":"1"}},"ttlMs":0,"cacheScope":"private"}""",
                "tools/list" => """{"resultType":"complete","tools":[],"ttlMs":0,"cacheScope":"private"}""",
                "tools/call" => TaskResult("working", created: true),
                "tasks/get" => TaskResult(Status, created: false),
                "tasks/update" => "{}",
                "tasks/cancel" => "{}",
                _ => throw new InvalidOperationException($"Unexpected MCP method '{method}'.")
            };
            var id = root.GetProperty("id").GetRawText();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{result}}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }

        private static string TaskResult(string status, bool created)
        {
            var input = status == "input_required"
                ? ",\"inputRequests\":{\"approval\":{\"method\":\"elicitation/create\",\"params\":{\"message\":\"approve\",\"requestedSchema\":{\"type\":\"object\",\"properties\":{}}}}}"
                : string.Empty;
            return $"{{\"resultType\":\"task\",\"taskId\":\"task-1\",\"status\":\"{status}\",\"createdAt\":\"2026-08-26T12:00:00Z\",\"lastUpdatedAt\":\"2026-08-26T12:00:01Z\",\"pollIntervalMs\":1{input}}}";
        }
    }

    private sealed class PassthroughProtector : IMcpRecoveryReferenceProtector
    {
        public ValueTask<string> ProtectAsync(string reference, CancellationToken cancellationToken) =>
            ValueTask.FromResult(reference);
        public ValueTask<string> UnprotectAsync(string protectedReference, CancellationToken cancellationToken) =>
            ValueTask.FromResult(protectedReference);
    }
}
