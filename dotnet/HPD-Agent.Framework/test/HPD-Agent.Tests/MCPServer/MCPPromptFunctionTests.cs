using System.Text.Json;
using FluentAssertions;
using HPD.Agent.MCP;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.Tests.MCPServer;

public sealed class MCPPromptFunctionTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "hpd-mcp-prompt-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _serverScriptPath;

    public MCPPromptFunctionTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        _serverScriptPath = Path.Combine(_tempDirectory, "prompt_server.py");
        File.WriteAllText(_serverScriptPath, PromptServerScript);
    }

    [Fact]
    public async Task EnablePromptsFalse_DoesNotEmitPromptFunctions()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var config = CreateServerConfig(enablePrompts: false);

        var functions = await manager.LoadToolsForToolHarnessAsync(config);

        functions.Should().BeEmpty();
    }

    [Fact]
    public async Task EnablePromptsTrue_EmitsFlatPromptFunctions()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var config = CreateServerConfig(enablePrompts: true);

        var functions = await manager.LoadToolsForToolHarnessAsync(config);

        functions.Select(function => function.Name).Should().BeEquivalentTo(
            "mcp_fixture_server_list_prompts",
            "mcp_fixture_server_get_prompt");

        foreach (var function in functions)
        {
            function.AdditionalProperties.Should().ContainKey("ParentToolHarness").WhoseValue.Should().Be("ParentHarness");
            function.AdditionalProperties.Should().ContainKey("ParentContainer").WhoseValue.Should().Be("ParentHarness");
            function.AdditionalProperties.Should().ContainKey("MCPPromptOperation");
        }
    }

    [Fact]
    public async Task EnablePromptsTrue_CollapsedModePutsPromptFunctionsBehindMcpContainer()
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
            .Which.Should().Contain("mcp_fixture_server_get_prompt");
    }

    [Fact]
    public async Task PromptFunctions_InvokeListAndGetPromptPaths()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var config = CreateServerConfig(enablePrompts: true);
        var functions = await manager.LoadToolsForToolHarnessAsync(config);

        var listPrompts = functions.Single(function => function.Name == "mcp_fixture_server_list_prompts");
        var promptList = await InvokeJsonAsync(listPrompts, "{}");
        var prompt = promptList.GetProperty("prompts")[0];
        prompt.GetProperty("name").GetString().Should().Be("debug_query");
        prompt.GetProperty("arguments")[0].GetProperty("name").GetString().Should().Be("query");
        prompt.GetProperty("arguments")[0].GetProperty("required").GetBoolean().Should().BeTrue();

        var getPrompt = functions.Single(function => function.Name == "mcp_fixture_server_get_prompt");
        var promptResult = await InvokeJsonAsync(
            getPrompt,
            """{"name":"debug_query","arguments":{"query":"select * from userss","error":"table userss does not exist"},"maxChars":15}""");

        promptResult.GetProperty("description").GetString().Should().Be("Debug SQL against the current schema.");
        promptResult.GetProperty("truncated").GetBoolean().Should().BeTrue();

        var textMessage = promptResult.GetProperty("messages")[0];
        textMessage.GetProperty("role").GetString().Should().Be("user");
        textMessage.GetProperty("content").GetProperty("contentType").GetString().Should().Be("text");
        textMessage.GetProperty("content").GetProperty("text").GetString().Should().Be("Debug query: se");
        textMessage.GetProperty("content").GetProperty("truncated").GetBoolean().Should().BeTrue();

        var imageMessage = promptResult.GetProperty("messages")[1];
        imageMessage.GetProperty("content").GetProperty("contentType").GetString().Should().Be("image");
        imageMessage.GetProperty("content").GetProperty("mimeType").GetString().Should().Be("image/png");
        imageMessage.GetProperty("content").GetProperty("byteLength").GetInt32().Should().Be(4);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private MCPServerConfig CreateServerConfig(bool enablePrompts)
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
            EnablePrompts = enablePrompts,
            MaxPromptContentLength = 20,
            ParentToolHarness = "ParentHarness"
        };
    }

    private MCPServerConfig CreateStandaloneCollapsedServerConfig()
    {
        var config = CreateServerConfig(enablePrompts: true);
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

    private const string PromptServerScript = """
import base64
import json
import sys

CAPABILITIES = {
    "tools": {},
    "prompts": {}
}

SERVER_INFO = {
    "name": "fixture-prompt-server",
    "version": "1.0.0"
}

PROMPTS = [
    {
        "name": "debug_query",
        "title": "Debug Query",
        "description": "Debug SQL against the current schema.",
        "arguments": [
            {
                "name": "query",
                "title": "Query",
                "description": "The SQL query to debug.",
                "required": True
            },
            {
                "name": "error",
                "description": "Optional database error.",
                "required": False
            }
        ]
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

def handle_request(message):
    method = message.get("method")
    message_id = message.get("id")
    params = message.get("params") or {}

    if method == "initialize":
        send_response(message_id, {
            "protocolVersion": params.get("protocolVersion", "2025-11-25"),
            "capabilities": CAPABILITIES,
            "serverInfo": SERVER_INFO
        })
        return

    if method == "tools/list":
        send_response(message_id, {
            "tools": []
        })
        return

    if method == "prompts/list":
        send_response(message_id, {
            "prompts": PROMPTS
        })
        return

    if method == "prompts/get":
        arguments = params.get("arguments") or {}
        query = arguments.get("query", "")
        error = arguments.get("error", "")
        send_response(message_id, {
            "description": "Debug SQL against the current schema.",
            "messages": [
                {
                    "role": "user",
                    "content": {
                        "type": "text",
                        "text": "Debug query: " + query + " error: " + error
                    }
                },
                {
                    "role": "assistant",
                    "content": {
                        "type": "image",
                        "data": base64.b64encode(bytes([1, 2, 3, 4])).decode("ascii"),
                        "mimeType": "image/png"
                    }
                }
            ]
        })
        return

    if method == "notifications/initialized":
        return

    send_error(message_id, -32601, "Method not found: " + str(method))

for line in sys.stdin:
    if not line.strip():
        continue
    handle_request(json.loads(line))
""";
}
