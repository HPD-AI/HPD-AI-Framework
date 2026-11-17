using System.Text.Json;

namespace HPD.Agent.Tests;

public sealed class CapabilityInspectionTests
{
    [Fact]
    public async Task Published_capability_epoch_exposes_action_schemas_and_resolved_policies()
    {
        await using Agent agent = await new AgentBuilder(new AgentConfig
        {
            Name = "capability-inspection"
        }).BuildAsync();

        AgentCapabilityInspectionSnapshot snapshot = await agent.InspectCapabilitiesAsync();
        AgentCapabilityInspection content = Assert.Single(
            snapshot.Functions,
            function => function.Name == "Content");

        Assert.Equal(HPDCapabilityKind.Function, content.Kind);
        Assert.Equal("request", content.ActionArgumentName);
        Assert.Equal("action", content.ActionDiscriminator);
        Assert.Equal(JsonValueKind.Object, content.InputSchema.ValueKind);
        Assert.Contains(content.Actions, action => action.Name == "list");
        AgentFunctionActionInspection list = Assert.Single(
            content.Actions,
            action => action.Name == "list");
        Assert.Equal(JsonValueKind.Object, list.InputSchema.ValueKind);
        Assert.Equal("list", list.InputSchema.GetProperty("properties")
            .GetProperty("action").GetProperty("const").GetString());
        Assert.Equal(AgentInvocationModePolicy.SynchronousOnly, list.InvocationModePolicy);
        Assert.Equal(AgentInvocationModeHandling.Runtime, list.InvocationModeHandling);
        Assert.False(string.IsNullOrWhiteSpace(list.Permission.Authority));
    }
}
