using System.Text.Json;
using HPD.Agent.ClientTools;
using HPD.Agent.Permissions;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.ClientTools;

public sealed class CompoundClientToolContractTests
{
    [Fact]
    public void ResolveOperation_MergesDefaultAndActionPolicy()
    {
        var definition = CreateDefinition();

        var operation = definition.ResolveOperation(
            new Dictionary<string, object?> { ["action"] = "updateNodes" });

        Assert.NotNull(operation);
        Assert.Equal("updateNodes", operation.Action);
        Assert.Equal("penpot.write.updateNodes", operation.Policy.Permission!.Authority);
        Assert.True(operation.Policy.Permission.RequiresPermission);
        Assert.True(operation.Policy.RequiresFreshContext);
        Assert.Equal(AgentInvocationModePolicy.SynchronousOnly, operation.Policy.InvocationModePolicy);
    }

    [Fact]
    public void Validate_RejectsSchemaAndPolicyActionMismatch()
    {
        var definition = CreateDefinition() with
        {
            OperationContract = new ClientToolOperationContract
            {
                Discriminator = "action",
                Actions = new Dictionary<string, ClientToolPolicy>
                {
                    ["deleteNodes"] = new()
                }
            }
        };

        var exception = Assert.Throws<ArgumentException>(definition.Validate);

        Assert.Contains("exactly match", exception.Message);
    }

    [Fact]
    public void ResolveOperation_UsesCompoundActionPermissionDeclaration()
    {
        var definition = CreateDefinition();
        var operation = definition.ResolveOperation(
            new Dictionary<string, object?> { ["action"] = "updateNodes" });

        Assert.Equal("penpot.write.updateNodes", operation!.Policy.Permission!.Authority);
    }

    [Fact]
    public void ResolveOperation_RejectsUnknownAction()
    {
        var definition = CreateDefinition();

        var exception = Assert.Throws<ArgumentException>(() =>
            definition.ResolveOperation(
                new Dictionary<string, object?> { ["action"] = "unknown" }));

        Assert.Contains("Unknown compound tool action", exception.Message);
    }

    [Fact]
    public void InvocationSchema_SeparatesModeFromClosedBranches()
    {
        var schema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "oneOf": [
                {
                  "type": "object",
                  "properties": { "action": { "const": "inspect" } },
                  "required": ["action"],
                  "additionalProperties": false
                },
                {
                  "type": "object",
                  "properties": { "action": { "const": "export" } },
                  "required": ["action"],
                  "additionalProperties": false
                }
              ]
            }
            """).RootElement.Clone();

        var transformed = FunctionInvocationEnvelope.CreateSchema(schema);
        var properties = transformed.GetProperty("properties");
        var branches = properties.GetProperty("arguments").GetProperty("oneOf");

        Assert.False(branches[0].GetProperty("properties").TryGetProperty("invocationMode", out _));
        Assert.False(branches[1].GetProperty("properties").TryGetProperty("invocationMode", out _));
        Assert.Equal("background", properties.GetProperty("invocation").GetProperty("properties")
            .GetProperty("mode").GetProperty("enum")[1].GetString());
        Assert.False(branches[1].GetProperty("additionalProperties").GetBoolean());
    }

    [Theory]
    [InlineData("export", "background", false)]
    [InlineData("inspect", "background", true)]
    [InlineData("inspect", null, false)]
    public async Task ClientToolPreparation_UsesSelectedActionPolicyAndPreservesDomainMode(
        string action, string? requestedMode, bool rejected)
    {
        using var schema = JsonDocument.Parse("""
            {"type":"object","oneOf":[
              {"type":"object","properties":{"action":{"const":"inspect"},"invocationMode":{"type":"string"}},"required":["action","invocationMode"],"additionalProperties":false},
              {"type":"object","properties":{"action":{"const":"export"},"invocationMode":{"type":"string"}},"required":["action","invocationMode"],"additionalProperties":false}
            ]}
            """);
        var definition = new ClientToolDefinition
        {
            Name = "canvas", Description = "Client canvas", ParametersSchema = schema.RootElement.Clone(),
            OperationContract = new ClientToolOperationContract
            {
                Discriminator = "action",
                Actions = new Dictionary<string, ClientToolPolicy>
                {
                    ["inspect"] = new() { InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly },
                    ["export"] = new() { InvocationModePolicy = AgentInvocationModePolicy.ModelChoice }
                }
            }
        };
        definition.Validate();
        var function = ClientToolMiddleware.ConvertToolToAIFunction(definition, "canvas", null);
        var payload = requestedMode is null
            ? "{\"arguments\":{\"action\":\"" + action + "\",\"invocationMode\":\"domain\"}}"
            : "{\"arguments\":{\"action\":\"" + action + "\",\"invocationMode\":\"domain\"},\"invocation\":{\"mode\":\"" + requestedMode + "\"}}";
        using var json = JsonDocument.Parse(payload);
        var arguments = new AIFunctionArguments();
        arguments.SetJson(json.RootElement.Clone());
        var state = AgentLoopState.InitialSafe([], "run", "conversation", "AgentA");
        var context = new global::HPD.Agent.Middleware.AgentContext("AgentA", "conversation", state,
            new global::HPD.Events.Core.EventCoordinator(), null, null, CancellationToken.None);
        var core = new FunctionExecutionCore(new global::HPD.Agent.Middleware.AgentMiddlewarePipeline([]));

        var preparation = await core.PrepareFunctionAsync(new FunctionCallContent("call", function.Name, arguments),
            new ChatOptions { Tools = [function] }, new AgentRunConfig(), context, null, CancellationToken.None, admit: false);

        if (rejected)
        {
            Assert.NotNull(preparation.ImmediateOutcome);
            return;
        }
        Assert.Null(preparation.ImmediateOutcome);
        Assert.Equal(action, preparation.ResolvedInvocation!.Action);
        Assert.Equal(requestedMode == "background" ? AgentInvocationMode.Background : AgentInvocationMode.Synchronous,
            preparation.ResolvedInvocation.Mode);
        Assert.Equal("domain", ((JsonElement)preparation.Arguments["invocationMode"]!).GetString());
        arguments["invocationMode"] = "changed after preparation";
        Assert.Equal("domain", ((JsonElement)preparation.Arguments["invocationMode"]!).GetString());
        var frozen = Assert.IsAssignableFrom<IDictionary<string, object?>>(preparation.Arguments);
        Assert.Throws<NotSupportedException>(() => frozen["invocationMode"] = "changed");
    }

    private static ClientToolDefinition CreateDefinition() =>
        new()
        {
            Name = "penpot",
            Description = "Penpot design operations.",
            ParametersSchema = JsonDocument.Parse(
                """
                {
                  "type": "object",
                  "oneOf": [
                    {
                      "type": "object",
                      "properties": {
                        "action": { "const": "updateNodes" }
                      },
                      "required": ["action"],
                      "additionalProperties": false
                    }
                  ]
                }
                """).RootElement.Clone(),
            DefaultPolicy = new ClientToolPolicy
            {
                InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly
            },
            OperationContract = new ClientToolOperationContract
            {
                Discriminator = "action",
                Actions = new Dictionary<string, ClientToolPolicy>
                {
                    ["updateNodes"] = new()
                    {
                        Permission = new AIFunctionPermissionDeclaration
                        {
                            RequiresPermission = true,
                            Authority = "penpot.write.updateNodes",
                            Source = PermissionDeclarationSource.ActionOverride
                        },
                        MutatesState = true,
                        RequiresFreshContext = true
                    }
                }
            }
        };
}
