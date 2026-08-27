using System.Text.Json;
using HPD.Agent.MCP;

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
Console.WriteLine("HPD-Agent.MCP AOT smoke passed.");
