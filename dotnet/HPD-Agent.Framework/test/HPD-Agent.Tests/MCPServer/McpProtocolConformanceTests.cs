using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using HPD.Agent.MCP;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;
using HPD.Events.Core;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpProtocolConformanceTests
{
    [Fact]
    public async Task Mrtr_CompletesTwoSdkOwnedInputRoundsThroughHpdResolver()
    {
        var handler = new MrtrHandler();
        var resolver = new RecordingResolver();
        using var http = new HttpClient(handler);
        await using var runtime = Runtime(http, options => options.Invocation.InputResolver = resolver);
        var function = Assert.Single(await runtime.LoadToolsFromManifestContentAsync(Manifest()));

        var result = await ((HPDAIFunctionFactory.HPDAIFunction)function).InvokeAsync(
            new AIFunctionArguments(), CreateContext(function), default);

        Assert.Equal(2, resolver.Requests.Count);
        Assert.Equal(3, handler.ToolCalls.Count);
        Assert.DoesNotContain("inputResponses", handler.ToolCalls[0], StringComparison.Ordinal);
        Assert.Contains("inputResponses", handler.ToolCalls[1], StringComparison.Ordinal);
        Assert.Contains("round-1", handler.ToolCalls[1], StringComparison.Ordinal);
        Assert.Contains("round-2", handler.ToolCalls[2], StringComparison.Ordinal);
        Assert.Contains("done", result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mrtr_SdkRoundLimitRejectsUnboundedServerInputLoop()
    {
        var handler = new MrtrHandler(neverComplete: true);
        using var http = new HttpClient(handler);
        await using var runtime = Runtime(http, options =>
            options.Invocation.InputResolver = new RecordingResolver());
        var function = Assert.Single(await runtime.LoadToolsFromManifestContentAsync(Manifest()));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            ((HPDAIFunctionFactory.HPDAIFunction)function).InvokeAsync(
                new AIFunctionArguments(), CreateContext(function), default).AsTask());

        Assert.InRange(handler.ToolCalls.Count, 2, 100);
    }

    [Fact]
    public async Task AutomaticNegotiation_UsesModernDiscoveryWithoutInitializeOrSessionHeader()
    {
        var handler = new ProtocolHandler(modern: true);
        using var http = new HttpClient(handler);
        await using var runtime = Runtime(http);

        var tools = await runtime.LoadToolsFromManifestContentAsync(Manifest());

        Assert.Empty(tools);
        Assert.Equal(["server/discover", "tools/list"], handler.Methods);
        Assert.DoesNotContain("initialize", handler.Methods);
        Assert.All(handler.SessionHeaders, Assert.Null);
        Assert.All(handler.ProtocolHeaders, value => Assert.Equal("2026-07-28", value));
        Assert.All(handler.RequestBodies, body => Assert.Contains(
            "io.modelcontextprotocol/protocolVersion", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AutomaticNegotiation_FallsBackToInitializeEraOnSameHttpTransport()
    {
        var handler = new ProtocolHandler(modern: false);
        using var http = new HttpClient(handler);
        await using var runtime = Runtime(http);

        var tools = await runtime.LoadToolsFromManifestContentAsync(Manifest());

        Assert.Empty(tools);
        Assert.Equal("server/discover", handler.Methods[0]);
        Assert.Contains("initialize", handler.Methods);
        Assert.Contains("notifications/initialized", handler.Methods);
        Assert.Equal("legacy-session", handler.SessionHeaders.Last());
    }

    [Fact]
    public async Task ExactModernVersion_RefusesLegacyFallback()
    {
        var handler = new ProtocolHandler(modern: false);
        using var http = new HttpClient(handler);
        await using var runtime = Runtime(http, options => options.Protocol.ExactVersion = "2026-07-28");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            runtime.LoadToolsFromManifestContentAsync(Manifest()));

        Assert.Equal(["server/discover"], handler.Methods);
        Assert.DoesNotContain("initialize", handler.Methods);
    }

    [Fact]
    public async Task AppsAdvertisement_IsRecordedWithoutAdvertisingClientSupportOrRendering()
    {
        var handler = new ProtocolHandler(modern: true, advertiseApps: true);
        using var http = new HttpClient(handler);
        var options = new McpOptions { HttpClientFactory = _ => http };
        var factory = new McpCapabilitySourceFactory(
            CapabilitySourceId.Create("mcp.test:apps"), null, Manifest(), options, null, null);
        await using var source = await factory.CreateAsync(null, default);

        var loaded = await source.LoadAsync(new CapabilityLoadContext(1, null), default);
        await using var owner = loaded.Owner;

        Assert.Equal("true", owner.Snapshot.Metadata[
            "mcp.server.wire.extension.apps.advertised"]);
        Assert.Equal("unavailable", owner.Snapshot.Metadata[
            "mcp.server.wire.apps.rendering"]);
        Assert.DoesNotContain("io.modelcontextprotocol/apps", handler.RequestBodies[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModernSubscriptions_UseListenAndProjectInvalidation()
    {
        var handler = new SubscriptionHandler();
        var options = new McpOptions
        {
            HttpClientFactory = _ => new HttpClient(handler)
        };
        options.Subscriptions.EnableCatalogInvalidation = true;
        await using var runtime = new McpRuntime(NullLogger.Instance, options);
        await runtime.LoadToolsFromManifestContentAsync(Manifest());
        var invalidated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await runtime.StartSubscriptionsAsync(reason => invalidated.TrySetResult(reason), default);
        var reason = await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("tools changed", reason, StringComparison.Ordinal);
        Assert.Contains("subscriptions/listen", handler.Methods);
        Assert.DoesNotContain("resources/subscribe", handler.Methods);
    }

    [Fact]
    public async Task SourceRefresh_ReusesFreshPublicCatalogPagesAcrossRevisionRuntimes()
    {
        var handler = new ProtocolHandler(modern: true, toolsTtlMilliseconds: 3_600_000);
        using var http = new HttpClient(handler);
        var options = new McpOptions { HttpClientFactory = _ => http };
        var factory = new McpCapabilitySourceFactory(
            CapabilitySourceId.Create("mcp.test:cache"), null, Manifest(), options, null, null);
        await using var source = await factory.CreateAsync(null, default);

        var first = await source.LoadAsync(new CapabilityLoadContext(1, null), default);
        await first.Owner.DisposeAsync();
        var second = await source.LoadAsync(new CapabilityLoadContext(2, null), default);
        await second.Owner.DisposeAsync();

        Assert.Equal(1, handler.Methods.Count(static method => method == "tools/list"));
        Assert.Equal(2, handler.Methods.Count(static method => method == "server/discover"));
    }

    [Fact]
    public async Task SourceRefresh_DoesNotReusePrivatePageAcrossRevisionConnections()
    {
        var handler = new ProtocolHandler(
            modern: true,
            toolsTtlMilliseconds: 3_600_000,
            publicToolsCache: false);
        using var http = new HttpClient(handler);
        var options = new McpOptions { HttpClientFactory = _ => http };
        var factory = new McpCapabilitySourceFactory(
            CapabilitySourceId.Create("mcp.test:private-cache"), null, Manifest(), options, null, null);
        await using var source = await factory.CreateAsync(null, default);

        var first = await source.LoadAsync(new CapabilityLoadContext(1, null), default);
        await first.Owner.DisposeAsync();
        var second = await source.LoadAsync(new CapabilityLoadContext(2, null), default);
        await second.Owner.DisposeAsync();

        Assert.Equal(2, handler.Methods.Count(static method => method == "tools/list"));
    }

    [Fact]
    public async Task MultiPageCatalogUsesEarliestExpiryAndMostRestrictiveScope()
    {
        var handler = new MultiPageCatalogHandler();
        using var http = new HttpClient(handler);
        var options = new McpOptions { HttpClientFactory = _ => http };
        var factory = new McpCapabilitySourceFactory(
            CapabilitySourceId.Create("mcp.test:aggregate-cache"), null, Manifest(), options, null, null);
        await using var source = await factory.CreateAsync(null, default);
        var before = DateTimeOffset.UtcNow;

        var loaded = await source.LoadAsync(new CapabilityLoadContext(1, null), default);
        await using var owner = loaded.Owner;

        Assert.Equal("private", owner.Snapshot.Metadata["mcp.catalog.cacheScope"]);
        var freshUntil = DateTimeOffset.Parse(owner.Snapshot.Metadata["mcp.catalog.freshUntil"]);
        Assert.InRange(freshUntil, before.AddSeconds(30), before.AddMinutes(2));
        Assert.IsType<System.Collections.Immutable.ImmutableArray<AIFunction>>(
            owner.Snapshot.Functions);
    }

    [Fact]
    public async Task ToolsResourcesAndPromptsRemainBoundedPermissionBearingHpdFunctions()
    {
        var handler = new CapabilityProjectionHandler();
        var options = new McpOptions { HttpClientFactory = _ => new HttpClient(handler) };
        var manifest = """{"servers":[{"name":"wire","transport":"http","endpoint":"https://wire.test/mcp","requiresPermission":true,"enableResources":true,"enablePrompts":true,"maxResourceListResults":2,"maxResourceContentLength":32,"maxPromptListResults":3,"maxPromptContentLength":48}]}""";
        var factory = new McpCapabilitySourceFactory(
            CapabilitySourceId.Create("mcp.test:projection"), null, manifest, options, null, null);
        await using var source = await factory.CreateAsync(null, default);

        var loaded = await source.LoadAsync(new CapabilityLoadContext(1, null), default);
        await using var owner = loaded.Owner;

        Assert.Contains(owner.Snapshot.Functions, function => function.Name == "echo");
        Assert.Contains(owner.Snapshot.Functions, function => function.Name.EndsWith("list_resources"));
        Assert.Contains(owner.Snapshot.Functions, function => function.Name.EndsWith("read_resource"));
        Assert.Contains(owner.Snapshot.Functions, function => function.Name.EndsWith("list_prompts"));
        Assert.Contains(owner.Snapshot.Functions, function => function.Name.EndsWith("get_prompt"));
        Assert.All(owner.Snapshot.Functions.Where(function =>
                function.Name == "echo" || function.Name.EndsWith("read_resource") ||
                function.Name.EndsWith("get_prompt")),
            function => Assert.True(
                Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function).HPDOptions.FunctionPermission?.RequiresPermission));
        Assert.All(owner.Snapshot.Descriptors.Values, descriptor =>
            Assert.Equal(HPDCapabilityKind.Mcp, descriptor.Kind));
    }

    private static McpRuntime Runtime(HttpClient http, Action<McpOptions>? configure = null)
    {
        var options = new McpOptions { HttpClientFactory = _ => http };
        configure?.Invoke(options);
        return new McpRuntime(NullLogger.Instance, options);
    }

    private static string Manifest() =>
        """{"servers":[{"name":"wire","transport":"http","endpoint":"https://wire.test/mcp"}]}""";

    private static FunctionExecutionContext CreateContext(AIFunction function)
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "agent");
        var session = new global::HPD.Agent.Session("session-1");
        var thread = new global::HPD.Agent.Thread("session-1", "agent") { Id = "thread-1" };
        var agentContext = new AgentContext(
            "agent", "conversation-1", state, new EventCoordinator(), session, thread, default);
        var before = agentContext.AsBeforeFunction(
            function, "call-1", new Dictionary<string, object?>(), new AgentRunConfig(), null, null);
        return new FunctionExecutionContext(before, new FunctionRequest
        {
            Function = function,
            CallId = "call-1",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = agentContext.EventCoordinator
        });
    }

    private sealed class RecordingResolver : IMcpInputResolver
    {
        internal List<McpInputResolutionContext> Requests { get; } = [];

        public ValueTask<McpInputResolution> ResolveAsync(
            McpInputResolutionContext context,
            CancellationToken cancellationToken)
        {
            Requests.Add(context);
            using var document = JsonDocument.Parse("""{"confirm":true}""");
            return ValueTask.FromResult(new McpInputResolution
            {
                Resolved = true,
                Value = document.RootElement.Clone()
            });
        }
    }

    private sealed class MrtrHandler(bool neverComplete = false) : HttpMessageHandler
    {
        internal List<string> ToolCalls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString();
            if (method == "server/discover")
                return ProtocolHandler.JsonResponse(root, """{"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"mrtr","version":"1"}},"ttlMs":0,"cacheScope":"private"}""");
            if (method == "tools/list")
                return ProtocolHandler.JsonResponse(root, """{"resultType":"complete","tools":[{"name":"confirm","description":"confirm twice","inputSchema":{"type":"object","properties":{}}}],"ttlMs":0,"cacheScope":"private"}""");
            if (method != "tools/call")
                throw new InvalidOperationException($"Unexpected method '{method}'.");

            ToolCalls.Add(body);
            var round = ToolCalls.Count;
            if (neverComplete || round <= 2)
            {
                var inputRequired =
                    "{\"resultType\":\"input_required\",\"inputRequests\":{\"confirm-" + round +
                    "\":{\"method\":\"elicitation/create\",\"params\":{\"message\":\"Confirm round " + round +
                    "\",\"requestedSchema\":{\"type\":\"object\",\"properties\":{\"confirm\":{\"type\":\"boolean\"}}}}}}," +
                    "\"requestState\":\"round-" + round + "\"}";
                return ProtocolHandler.JsonResponse(root, inputRequired);
            }

            return ProtocolHandler.JsonResponse(root,
                """{"resultType":"complete","content":[{"type":"text","text":"done"}]}""");
        }
    }

    private sealed class SubscriptionHandler : HttpMessageHandler
    {
        internal List<string> Methods { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Methods.Add(method);
            if (method == "server/discover")
                return ProtocolHandler.JsonResponse(root, """{"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{"listChanged":true}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"subscriptions","version":"1"}},"ttlMs":0,"cacheScope":"private"}""");
            if (method == "tools/list")
                return ProtocolHandler.JsonResponse(root, """{"resultType":"complete","tools":[],"ttlMs":0,"cacheScope":"private"}""");
            if (method == "subscriptions/listen")
            {
                var id = root.GetProperty("id").GetRawText();
                var events =
                    "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\",\"params\":{}}\n\n" +
                    $"data: {{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"resultType\":\"complete\"}}}}\n\n";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(events, Encoding.UTF8, "text/event-stream")
                };
            }
            throw new InvalidOperationException($"Unexpected method '{method}'.");
        }
    }

    private sealed class MultiPageCatalogHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString();
            if (method == "server/discover")
                return ProtocolHandler.JsonResponse(root, """{"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"pages","version":"1"}},"ttlMs":0,"cacheScope":"private"}""");
            if (method == "tools/list")
            {
                var second = root.TryGetProperty("params", out var parameters) &&
                    parameters.TryGetProperty("cursor", out _);
                return ProtocolHandler.JsonResponse(root, second
                    ? """{"resultType":"complete","tools":[],"ttlMs":60000,"cacheScope":"private"}"""
                    : """{"resultType":"complete","tools":[],"nextCursor":"page-2","ttlMs":3600000,"cacheScope":"public"}""");
            }
            throw new InvalidOperationException($"Unexpected method '{method}'.");
        }
    }

    private sealed class CapabilityProjectionHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return root.GetProperty("method").GetString() switch
            {
                "server/discover" => ProtocolHandler.JsonResponse(root, """{"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{},"resources":{},"prompts":{}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"projection","version":"1"}},"ttlMs":0,"cacheScope":"private"}"""),
                "tools/list" => ProtocolHandler.JsonResponse(root, """{"resultType":"complete","tools":[{"name":"echo","description":"echo","inputSchema":{"type":"object","properties":{"text":{"type":"string"}}}}],"ttlMs":0,"cacheScope":"private"}"""),
                _ => throw new InvalidOperationException("Unexpected projection request.")
            };
        }
    }

    private sealed class ProtocolHandler(
        bool modern,
        long toolsTtlMilliseconds = 0,
        bool publicToolsCache = true,
        bool advertiseApps = false) : HttpMessageHandler
    {
        internal List<string> Methods { get; } = [];
        internal List<string?> SessionHeaders { get; } = [];
        internal List<string?> ProtocolHeaders { get; } = [];
        internal List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Methods.Add(method);
            RequestBodies.Add(body);
            SessionHeaders.Add(Header(request, "Mcp-Session-Id"));
            ProtocolHeaders.Add(Header(request, "MCP-Protocol-Version"));

            if (method == "server/discover")
            {
                return modern
                    ? JsonResponse(root, advertiseApps
                        ? """{"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{},"extensions":{"io.modelcontextprotocol/apps":{}}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"wire","version":"1"}},"ttlMs":0,"cacheScope":"private"}"""
                        : """{"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"wire","version":"1"}},"ttlMs":0,"cacheScope":"private"}""")
                    : new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("unsupported", Encoding.UTF8, "text/plain")
                    };
            }

            if (method == "initialize")
            {
                var response = JsonResponse(root,
                    """{"protocolVersion":"2025-11-25","capabilities":{"tools":{}},"serverInfo":{"name":"legacy","version":"1"}}""");
                response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "legacy-session");
                return response;
            }

            if (method == "notifications/initialized")
                return new HttpResponseMessage(HttpStatusCode.Accepted);

            if (method == "tools/list")
                return JsonResponse(root, modern
                    ? $"{{\"resultType\":\"complete\",\"tools\":[],\"ttlMs\":{toolsTtlMilliseconds},\"cacheScope\":\"{(publicToolsCache ? "public" : "private")}\"}}"
                    : """{"tools":[]}""");

            throw new InvalidOperationException($"Unexpected MCP method '{method}'.");
        }

        internal static HttpResponseMessage JsonResponse(JsonElement request, string result)
        {
            var id = request.GetProperty("id").GetRawText();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{result}}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.SingleOrDefault() : null;
    }
}
