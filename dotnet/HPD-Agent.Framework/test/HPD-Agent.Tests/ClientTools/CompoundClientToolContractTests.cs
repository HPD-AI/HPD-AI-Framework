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
        Assert.Equal("penpot.write.updateNodes", operation.Policy.PermissionScope);
        Assert.True(operation.Policy.RequiresPermission);
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
    public void PermissionResolver_UsesCompoundActionScope()
    {
        var definition = CreateDefinition();
        var function = HPDAIFunctionFactory.Create(
            static (_, _, _) => Task.FromResult<object?>(null),
            new HPDAIFunctionFactoryOptions
            {
                Name = "penpot_design_penpot",
                Description = "Penpot design operations.",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["ClientToolDefinition"] = definition
                }
            });
        var resolver = new ClientToolOperationPermissionScopeResolver();

        var resolved = resolver.TryResolveScope(
            function,
            new Dictionary<string, object?> { ["action"] = "updateNodes" },
            out var scope);

        Assert.True(resolved);
        Assert.Equal("penpot.write.updateNodes", scope);
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
                        RequiresPermission = true,
                        PermissionScope = "penpot.write.updateNodes",
                        MutatesState = true,
                        RequiresFreshContext = true
                    }
                }
            }
        };
}
