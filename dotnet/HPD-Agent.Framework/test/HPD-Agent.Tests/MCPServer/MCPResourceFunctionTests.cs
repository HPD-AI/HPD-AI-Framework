using System.Text.Json;
using FluentAssertions;
using HPD.Agent.MCP;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.Tests.MCPServer;

public sealed class MCPResourceFunctionTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "hpd-mcp-resource-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _serverScriptPath;

    public MCPResourceFunctionTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        _serverScriptPath = Path.Combine(_tempDirectory, "resource_server.py");
        File.WriteAllText(_serverScriptPath, ResourceServerScript);
    }

    [Fact]
    public async Task EnableResourcesFalse_DoesNotEmitResourceFunctions()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var config = CreateServerConfig(enableResources: false);

        var functions = await manager.LoadToolsForToolHarnessAsync(config);

        functions.Should().BeEmpty();
    }

    [Fact]
    public async Task EnableResourcesTrue_EmitsFlatResourceFunctions()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var config = CreateServerConfig(enableResources: true);

        var functions = await manager.LoadToolsForToolHarnessAsync(config);

        functions.Select(function => function.Name).Should().BeEquivalentTo(
            "mcp_fixture_server_list_resources",
            "mcp_fixture_server_list_resource_templates",
            "mcp_fixture_server_read_resource");

        foreach (var function in functions)
        {
            function.AdditionalProperties.Should().ContainKey("ParentToolHarness").WhoseValue.Should().Be("ParentHarness");
            function.AdditionalProperties.Should().ContainKey("ParentContainer").WhoseValue.Should().Be("ParentHarness");
            function.AdditionalProperties.Should().ContainKey("MCPResourceOperation");
        }
    }

    [Fact]
    public async Task EnableResourcesTrue_CollapsedModePutsResourceFunctionsBehindMcpContainer()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var manifest = new MCPManifest
        {
            Servers =
            [
                CreateStandaloneCollapsedServerConfig()
            ]
        };
        var manifestContent = JsonSerializer.Serialize(manifest, MCPJsonSerializerContext.Default.MCPManifest);

        var functions = await manager.LoadToolsFromManifestContentAsync(manifestContent);

        functions.Should().ContainSingle(function => function.Name == "MCP_fixture-server");
        foreach (var function in functions.Where(function => function.Name != "MCP_fixture-server"))
        {
            function.AdditionalProperties.Should().ContainKey("ParentToolHarness").WhoseValue.Should().Be("MCP_fixture-server");
        }
        functions.Single(function => function.Name == "MCP_fixture-server")
            .AdditionalProperties["ReferencedFunctions"].Should().BeOfType<string[]>()
            .Which.Should().Contain("mcp_fixture_server_read_resource");
    }

    [Fact]
    public async Task EnableResourcesTrue_NestedToolHarnessModePutsMcpContainerUnderParent()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var config = CreateServerConfig(enableResources: true);
        config.CollapseWithinToolHarness = true;

        var functions = await manager.LoadToolsForToolHarnessAsync(config);

        var container = functions.Single(function => function.Name == "MCP_fixture-server");
        container.AdditionalProperties["ParentContainer"].Should().Be("ParentHarness");

        foreach (var function in functions.Where(function => function.Name != "MCP_fixture-server"))
        {
            function.AdditionalProperties.Should().ContainKey("ParentToolHarness").WhoseValue.Should().Be("MCP_fixture-server");
        }
    }

    [Fact]
    public async Task ResourceFunctions_InvokeListTemplateReadAndBlobPaths()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var config = CreateServerConfig(enableResources: true);
        var functions = await manager.LoadToolsForToolHarnessAsync(config);

        var listResources = functions.Single(function => function.Name == "mcp_fixture_server_list_resources");
        var resourceList = await InvokeJsonAsync(listResources, "{}");
        resourceList.GetProperty("resources")[0].GetProperty("uri").GetString().Should().Be("fixture://hello");

        var listTemplates = functions.Single(function => function.Name == "mcp_fixture_server_list_resource_templates");
        var templateList = await InvokeJsonAsync(listTemplates, "{}");
        templateList.GetProperty("resourceTemplates")[0].GetProperty("uriTemplate").GetString().Should().Be("fixture://items/{id}");

        var readResource = functions.Single(function => function.Name == "mcp_fixture_server_read_resource");
        var textRead = await InvokeJsonAsync(readResource, """{"uri":"fixture://hello","maxChars":5}""");
        var textContent = textRead.GetProperty("contents")[0];
        textContent.GetProperty("contentType").GetString().Should().Be("text");
        textContent.GetProperty("text").GetString().Should().Be("hello");
        textContent.GetProperty("truncated").GetBoolean().Should().BeTrue();
        textRead.GetProperty("truncated").GetBoolean().Should().BeTrue();

        var blobRead = await InvokeJsonAsync(readResource, """{"uri":"fixture://blob"}""");
        var blobContent = blobRead.GetProperty("contents")[0];
        blobContent.GetProperty("contentType").GetString().Should().Be("blob");
        blobContent.GetProperty("byteLength").GetInt32().Should().Be(4);
        blobContent.TryGetProperty("text", out var blobText).Should().BeTrue();
        blobText.ValueKind.Should().Be(JsonValueKind.Null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private MCPServerConfig CreateServerConfig(bool enableResources)
    {
        return new MCPServerConfig
        {
            Name = "fixture-server",
            Transport = "stdio",
            Command = "python3",
            Arguments = [_serverScriptPath],
            ProtocolVersion = "2025-11-25",
            ConnectionTimeoutMs = 10_000,
            InitializationTimeoutMs = 10_000,
            ShutdownTimeoutMs = 1_000,
            EnableResources = enableResources,
            MaxResourceContentLength = 20,
            ParentToolHarness = "ParentHarness"
        };
    }

    private MCPServerConfig CreateStandaloneCollapsedServerConfig()
    {
        var config = CreateServerConfig(enableResources: true);
        config.EnableCollapsing = true;
        config.ParentToolHarness = null;
        config.CollapseWithinToolHarness = false;
        return config;
    }

    private static async Task<JsonElement> InvokeJsonAsync(AIFunction function, string json)
    {
        var args = new AIFunctionArguments();
        using var document = JsonDocument.Parse(json);
        args.SetJson(document.RootElement.Clone());

        var hpdFunction = function.Should().BeOfType<HPDAIFunctionFactory.HPDAIFunction>().Subject;
        var result = await hpdFunction.InvokeAsync(args, CreateContext(function), CancellationToken.None);
        result.Should().BeOfType<JsonElement>();
        return (JsonElement)result!;
    }

    private static FunctionExecutionContext CreateContext(AIFunction function)
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new global::HPD.Agent.Session("session-1");
        var thread = new global::HPD.Agent.Thread("session-1") { Id = "thread-1" };
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            new EventCoordinator(),
            session,
            thread,
            CancellationToken.None);
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            toolharnessName: null,
            skillName: null);

        return new FunctionExecutionContext(
            beforeContext,
            new FunctionRequest
            {
                Function = function,
                CallId = "call-1",
                Arguments = new Dictionary<string, object?>(),
                State = state,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = agentContext.EventCoordinator
            });
    }

    private const string ResourceServerScript = """
import base64
import json
import sys

CAPABILITIES = {
    "tools": {},
    "resources": {}
}

SERVER_INFO = {
    "name": "fixture-resource-server",
    "version": "1.0.0"
}

RESOURCES = [
    {
        "name": "hello",
        "title": "Hello",
        "uri": "fixture://hello",
        "description": "A text fixture resource.",
        "mimeType": "text/plain",
        "size": 22
    },
    {
        "name": "blob",
        "title": "Blob",
        "uri": "fixture://blob",
        "description": "A binary fixture resource.",
        "mimeType": "application/octet-stream",
        "size": 4
    }
]

TEMPLATES = [
    {
        "name": "item",
        "title": "Item",
        "uriTemplate": "fixture://items/{id}",
        "description": "A templated fixture resource.",
        "mimeType": "text/plain"
    }
]

def send_response(message_id, result):
    sys.stdout.write(json.dumps({
        "jsonrpc": "2.0",
        "id": message_id,
        "result": result
    }, separators=(",", ":")) + "\n")
    sys.stdout.flush()

def send_error(message_id, code, message):
    sys.stdout.write(json.dumps({
        "jsonrpc": "2.0",
        "id": message_id,
        "error": {
            "code": code,
            "message": message
        }
    }, separators=(",", ":")) + "\n")
    sys.stdout.flush()

for line in sys.stdin:
    if not line.strip():
        continue

    request = json.loads(line)
    method = request.get("method")
    message_id = request.get("id")

    if message_id is None:
        continue

    if method == "initialize":
        send_response(message_id, {
            "protocolVersion": request.get("params", {}).get("protocolVersion", "2025-11-25"),
            "capabilities": CAPABILITIES,
            "serverInfo": SERVER_INFO
        })
    elif method == "tools/list":
        send_response(message_id, {
            "tools": []
        })
    elif method == "resources/list":
        send_response(message_id, {
            "resources": RESOURCES
        })
    elif method == "resources/templates/list":
        send_response(message_id, {
            "resourceTemplates": TEMPLATES
        })
    elif method == "resources/read":
        uri = request.get("params", {}).get("uri")
        if uri == "fixture://blob":
            send_response(message_id, {
                "contents": [
                    {
                        "uri": uri,
                        "mimeType": "application/octet-stream",
                        "blob": base64.b64encode(bytes([1, 2, 3, 4])).decode("ascii")
                    }
                ]
            })
        else:
            send_response(message_id, {
                "contents": [
                    {
                        "uri": uri or "fixture://hello",
                        "mimeType": "text/plain",
                        "text": "hello resource content"
                    }
                ]
            })
    else:
        send_error(message_id, -32601, "Method not found")
""";
}
