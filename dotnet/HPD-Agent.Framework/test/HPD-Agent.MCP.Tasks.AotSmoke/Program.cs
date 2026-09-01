using System.Text.Json;
using HPD.Agent;
using HPD.Agent.MCP;
using ModelContextProtocol.Extensions.Tasks;

var reference = new McpTaskRecoveryReference("modern", "task-1");
var json = JsonSerializer.Serialize(
    reference, McpTaskRecoveryJsonContext.Default.McpTaskRecoveryReference);
var restored = JsonSerializer.Deserialize(
    json, McpTaskRecoveryJsonContext.Default.McpTaskRecoveryReference);
if (restored != reference)
    throw new InvalidOperationException("Task recovery reference did not round trip.");
if (McpTaskProvider.MapStatus(McpTaskStatus.Completed) != AgentOperationProviderStatus.Completed)
    throw new InvalidOperationException("Task status projection failed.");
Console.WriteLine("HPD-Agent.MCP.Tasks AOT smoke passed.");
