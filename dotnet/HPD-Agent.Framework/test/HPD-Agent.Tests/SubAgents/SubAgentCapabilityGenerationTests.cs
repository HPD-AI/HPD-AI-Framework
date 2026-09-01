using HPD.Agent.SourceGenerator.Capabilities;
using Xunit;

namespace HPD.Agent.Tests.SubAgents;

public class SubAgentCapabilityGenerationTests
{
    [Fact]
    public void SubAgentCapability_EmitsDescriptorInsteadOfIndependentFunction()
    {
        var capability = new SubAgentCapability
        {
            Name = "ResearchAgent",
            SubAgentName = "researcher",
            MethodName = "CreateResearchAgent",
            Description = "Researches a bounded topic.",
            ParentToolHarnessName = "Harness",
            IsStatic = true,
            RequiresPermission = true
        };

        Assert.False(capability.EmitsIntoCreateTools);
        Assert.Throws<InvalidOperationException>(() => capability.GenerateRegistrationCode(new ToolHarnessInfo()));
    }
}
