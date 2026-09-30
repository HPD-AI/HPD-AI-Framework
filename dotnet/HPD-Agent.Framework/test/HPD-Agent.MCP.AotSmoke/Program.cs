using System.Text.Json;
using HPD.Agent;
using HPD.Agent.MCP;
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

Console.WriteLine("HPD-Agent.MCP AOT smoke passed.");
