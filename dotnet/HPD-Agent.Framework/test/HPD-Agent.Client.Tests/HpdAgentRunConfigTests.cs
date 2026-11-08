using System.Text.Json;

namespace HPD.Agent.Client.Tests;

public sealed class HpdAgentRunConfigTests
{
    [Fact]
    public void SerializesStableFieldsNestedChatAndComplexPolicies()
    {
        var config = new HpdAgentRunConfig
        {
            ProviderKey = "anthropic",
            ModelId = "claude-sonnet",
            AdditionalSystemInstructions = "Be concise.",
            Chat = new HpdAgentRunChatConfig
            {
                Temperature = 0.7,
                MaxOutputTokens = 4096,
                TopP = 0.9
            },
            PermissionOverrides = new Dictionary<string, bool>
            {
                ["read_file"] = true,
                ["write_file"] = false
            },
            CoalesceDeltas = true,
            SkipTools = false,
            RunTimeout = "PT5M",
            Security = Json("""{"approval":"AutoApprove"}"""),
            Sandbox = Json("""{"filesystem":[{"access":"Read","path":"/workspace"}]}"""),
            Compaction = Json("""{"automatic":{"continuation":1}}"""),
            Goals = Json("""{"toolAccess":"ReadOnly"}"""),
            StructuredOutput = Json("""{"mode":"native"}""")
        };

        var json = config.ToJsonElement();

        Assert.Equal("anthropic", json.GetProperty("providerKey").GetString());
        Assert.Equal("claude-sonnet", json.GetProperty("modelId").GetString());
        Assert.Equal(0.7, json.GetProperty("chat").GetProperty("temperature").GetDouble());
        Assert.True(json.GetProperty("permissionOverrides").GetProperty("read_file").GetBoolean());
        Assert.False(json.GetProperty("permissionOverrides").GetProperty("write_file").GetBoolean());
        Assert.True(json.GetProperty("coalesceDeltas").GetBoolean());
        Assert.Equal("PT5M", json.GetProperty("runTimeout").GetString());
        Assert.Equal(
            "/workspace",
            json.GetProperty("sandbox").GetProperty("filesystem")[0].GetProperty("path").GetString());
        Assert.False(json.TryGetProperty("provider_key", out _));
        Assert.False(json.TryGetProperty("triggerCompaction", out _));
    }

    [Fact]
    public void OmitsUnsetFieldsAndSerializesEmptyConfigAsObject()
    {
        var empty = new HpdAgentRunConfig().ToJsonElement();
        var modelOnly = new HpdAgentRunConfig { ModelId = "model-one" }.ToJsonElement();

        Assert.Equal(JsonValueKind.Object, empty.ValueKind);
        Assert.Empty(empty.EnumerateObject());
        var property = Assert.Single(modelOnly.EnumerateObject());
        Assert.Equal("modelId", property.Name);
        Assert.Equal("model-one", property.Value.GetString());
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
