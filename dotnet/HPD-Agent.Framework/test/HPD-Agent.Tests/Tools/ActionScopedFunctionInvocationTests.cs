using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Tools;

public sealed class ActionScopedFunctionInvocationTests
{
    [Fact]
    public void ResolveAction_UsesSelectedPolicyAndSanitizesNestedControl()
    {
        using var document = JsonDocument.Parse("""
            {"request":{"action":"run","invocationMode":"background","value":3}}
            """);
        var arguments = new AIFunctionArguments
        {
            ["request"] = document.RootElement.GetProperty("request").Clone()
        };
        arguments.SetJson(document.RootElement.Clone());

        var resolved = AgentInvocationModes.ResolveAction(
            arguments, Contract(), out var sanitized);

        Assert.Equal("run", resolved.Action);
        Assert.Equal(AgentInvocationMode.Background, resolved.RequestedMode);
        Assert.Equal(AgentInvocationMode.Background, resolved.Mode);
        Assert.Equal(AgentInvocationModePolicy.ModelChoice, resolved.Policy);
        Assert.Equal(AgentInvocationModeHandling.ToolBody, resolved.Handling);
        Assert.False(sanitized.GetJson().GetProperty("request").TryGetProperty("invocationMode", out _));
        Assert.Equal(3, sanitized.GetJson().GetProperty("request").GetProperty("value").GetInt32());
    }

    [Fact]
    public void CreateActionSchema_OffersChoiceOnlyOnModelChoiceBranch()
    {
        using var document = JsonDocument.Parse("""
            {
              "type":"object",
              "properties":{
                "request":{"oneOf":[
                  {"type":"object","properties":{"action":{"const":"read"}}},
                  {"type":"object","properties":{"action":{"const":"run"}}}
                ]}
              }
            }
            """);

        var schema = AgentInvocationModes.CreateActionSchema(document.RootElement, Contract());
        var branches = schema.GetProperty("properties").GetProperty("request").GetProperty("oneOf");

        Assert.False(branches[0].GetProperty("properties").TryGetProperty("invocationMode", out _));
        Assert.True(branches[1].GetProperty("properties").TryGetProperty("invocationMode", out _));
    }

    [Fact]
    public void ResolveAction_RejectsUnknownActionWithoutFallback()
    {
        using var document = JsonDocument.Parse("""{"request":{"action":"delete"}}""");
        var arguments = new AIFunctionArguments { ["request"] = document.RootElement.GetProperty("request").Clone() };
        arguments.SetJson(document.RootElement.Clone());

        var error = Assert.Throws<InvalidOperationException>(() =>
            AgentInvocationModes.ResolveAction(arguments, Contract(), out _));

        Assert.Contains("Unknown function action", error.Message, StringComparison.Ordinal);
    }

    private static AIFunctionOperationContract Contract() => new()
    {
        ActionArgumentName = "request",
        Discriminator = "action",
        Actions = new Dictionary<string, AIFunctionActionPolicy>(StringComparer.Ordinal)
        {
            ["read"] = new()
            {
                InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
                InvocationModeHandling = AgentInvocationModeHandling.Runtime
            },
            ["run"] = new()
            {
                InvocationModePolicy = AgentInvocationModePolicy.ModelChoice,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody
            }
        }
    };
}
