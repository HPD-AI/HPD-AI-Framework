using HPD.Agent.Middleware;

namespace HPD.Agent.Tests;

public sealed class AgentResourcePublicApiTests
{
    [Fact]
    public void FunctionContextExposesAgentResourceLookup()
    {
        var method = typeof(FunctionExecutionContext).GetMethod(
            nameof(FunctionExecutionContext.GetAgentResource));

        Assert.NotNull(method);
        Assert.True(method.IsPublic);
        Assert.True(method.IsGenericMethodDefinition);
    }
}
