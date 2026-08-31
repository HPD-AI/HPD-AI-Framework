using System.Text.Json;
using HPD.Agent.ClientTools;
using HPD.Agent.Permissions;

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
        Assert.Equal("penpot.write.updateNodes", operation.Policy.Permission!.Scope);
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

        Assert.Equal("penpot.write.updateNodes", operation!.Policy.Permission!.Scope);
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
    public void InvocationSchema_AddsModeOnlyToEligibleClosedBranch()
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

        var transformed = AgentInvocationModes.CreateSchema(
            schema,
            AgentInvocationModePolicy.ModelChoice,
            "action",
            new HashSet<string>(StringComparer.Ordinal) { "export" });
        var branches = transformed.GetProperty("oneOf");

        Assert.False(branches[0].GetProperty("properties").TryGetProperty(
            "invocationMode",
            out _));
        Assert.True(branches[1].GetProperty("properties").TryGetProperty(
            "invocationMode",
            out var invocationMode));
        Assert.Equal("background", invocationMode.GetProperty("enum")[1].GetString());
        Assert.False(branches[1].GetProperty("additionalProperties").GetBoolean());
        Assert.False(transformed.TryGetProperty("properties", out _));
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
                            Scope = "penpot.write.updateNodes",
                            Source = PermissionDeclarationSource.ActionOverride
                        },
                        MutatesState = true,
                        RequiresFreshContext = true
                    }
                }
            }
        };
}
