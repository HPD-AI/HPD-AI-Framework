using FluentAssertions;
using HPD.Agent.MCP;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;

namespace HPD.Agent.Tests.MCPServer;

public sealed class MCPTaskFunctionTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "hpd-mcp-task-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _serverScriptPath;

    public MCPTaskFunctionTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        _serverScriptPath = Path.Combine(_tempDirectory, "task_server.py");
        File.WriteAllText(_serverScriptPath, TaskServerScript);
    }

    [Fact]
    public async Task McpToolTask_CompletesThroughHpdAdapter()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var functions = await manager.LoadToolsForToolHarnessAsync(CreateServerConfig());
        var function = functions.Single(f => f.Name == "delayed_echo");

        var args = new AIFunctionArguments
        {
            ["message"] = "hello from task"
        };

        var result = await InvokeAsync(function, args);

        result.Should().BeOfType<TextContent>()
            .Which.Text.Should().Be("task completed: hello from task");
    }

    [Fact]
    public async Task McpToolTaskFailure_PropagatesThroughHpdAdapter()
    {
        using var manager = new MCPClientManager(NullLogger.Instance, new MCPOptions { FailOnServerError = true });
        var functions = await manager.LoadToolsForToolHarnessAsync(CreateServerConfig());
        var function = functions.Single(f => f.Name == "delayed_echo");

        var args = new AIFunctionArguments
        {
            ["message"] = "please fail"
        };

        var act = () => InvokeAsync(function, args);

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("*Task*failed*");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private MCPServerConfig CreateServerConfig()
    {
        return new MCPServerConfig
        {
            Name = "task-server",
            Transport = "stdio",
            Command = "python3",
            Arguments = [_serverScriptPath],
            ProtocolVersion = "2026-07-28",
            ConnectionTimeoutMs = 10_000,
            InitializationTimeoutMs = 10_000,
            ShutdownTimeoutMs = 1_000,
            ParentToolHarness = "ParentHarness"
        };
    }

    private static async Task<object?> InvokeAsync(AIFunction function, AIFunctionArguments args)
    {
        var hpdFunction = function.Should().BeOfType<HPDAIFunctionFactory.HPDAIFunction>().Subject;
        return await hpdFunction.InvokeAsync(args, CreateContext(function), CancellationToken.None);
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

    private const string TaskServerScript = """
import json
import sys
from datetime import datetime, timezone

SERVER_INFO = {
    "name": "fixture-task-server",
    "version": "1.0.0"
}

TOOLS = [
    {
        "name": "delayed_echo",
        "description": "Echoes a message through the MCP tasks extension.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "message": {
                    "type": "string"
                }
            },
            "required": ["message"]
        }
    }
]

tasks = {}

def now():
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

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

def task_base(task_id, status):
    created_at = tasks.get(task_id, {}).get("createdAt", now())
    return {
        "taskId": task_id,
        "status": status,
        "createdAt": created_at,
        "lastUpdatedAt": now(),
        "pollIntervalMs": 1
    }

for line in sys.stdin:
    if not line.strip():
        continue

    request = json.loads(line)
    method = request.get("method")
    message_id = request.get("id")

    if message_id is None:
        continue

    if method == "server/discover":
        send_response(message_id, {
            "supportedVersions": ["2026-07-28"],
            "capabilities": {
                "tools": {}
            },
            "serverInfo": SERVER_INFO,
            "ttlMs": 0,
            "cacheScope": "private"
        })
    elif method == "initialize":
        send_response(message_id, {
            "protocolVersion": "2026-07-28",
            "capabilities": {
                "tools": {}
            },
            "serverInfo": SERVER_INFO
        })
    elif method == "tools/list":
        send_response(message_id, {
            "tools": TOOLS
        })
    elif method == "tools/call":
        params = request.get("params", {})
        arguments = params.get("arguments", {})
        message = arguments.get("message", "")
        task_id = "task-" + str(len(tasks) + 1)
        created_at = now()
        tasks[task_id] = {
            "message": message,
            "createdAt": created_at,
            "polls": 0
        }
        send_response(message_id, {
            "resultType": "task",
            "taskId": task_id,
            "status": "working",
            "createdAt": created_at,
            "lastUpdatedAt": created_at,
            "pollIntervalMs": 1
        })
    elif method == "tasks/get":
        task_id = request.get("params", {}).get("taskId")
        task = tasks.get(task_id)
        if task is None:
            send_error(message_id, -32602, "Unknown task")
            continue

        task["polls"] += 1
        if "fail" in task["message"]:
            result = task_base(task_id, "failed")
            result["error"] = {
                "code": -32000,
                "message": "task failure requested"
            }
            send_response(message_id, result)
            continue

        if task["polls"] == 1:
            send_response(message_id, task_base(task_id, "working"))
            continue

        result = task_base(task_id, "completed")
        result["result"] = {
            "content": [
                {
                    "type": "text",
                    "text": "task completed: " + task["message"]
                }
            ]
        }
        send_response(message_id, result)
    elif method == "tasks/cancel":
        send_response(message_id, {})
    else:
        send_error(message_id, -32601, "Method not found")
""";
}
