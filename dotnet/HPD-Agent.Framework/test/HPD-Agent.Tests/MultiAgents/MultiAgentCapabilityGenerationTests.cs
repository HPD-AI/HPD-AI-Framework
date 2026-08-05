using FluentAssertions;
using HPD.Agent.SourceGenerator.Capabilities;
using Xunit;

namespace HPD.Agent.Tests.MultiAgents;

public class MultiAgentCapabilityGenerationTests
{
    private static ToolHarnessInfo MakeToolHarness(string name = "MyToolHarness") => new()
    {
        ClassName = name,
        Namespace = "Test.Namespace"
    };

    private static MultiAgentCapability MakeCapability(string name = "ResearchWorkflow") => new()
    {
        Name = name,
        MethodName = $"Create{name}",
        Description = "A test workflow",
        ParentToolHarnessName = "MyToolHarness",
        IsStatic = true,
        RequiresPermission = true,
        StreamEvents = true
    };

    [Fact]
    public void GeneratedCode_DelegatesToMultiAgentRuntimeInvokeAsync()
    {
        var code = MakeCapability().GenerateRegistrationCode(MakeToolHarness());

        code.Should().Contain("MultiAgentRuntime.InvokeAsync");
        code.Should().Contain("MultiAgentRuntime.MultiAgentInvocationRequest");
        code.Should().Contain("Workflow = workflow");
        code.Should().Contain("Name = \"ResearchWorkflow\"");
        code.Should().Contain("Input = input");
        code.Should().Contain("ParentContext = functionContext");
        code.Should().Contain("StreamEvents = true");
        code.Should().Contain("InvocationModePolicy = global::HPD.Agent.AgentInvocationModePolicy.SynchronousOnly");
        code.Should().Contain("RequestedMode = requestedMode");
        code.Should().Contain("return result.ToToolResult()");
    }

    [Fact]
    public void GeneratedCode_DoesNotContainRuntimeOrchestration()
    {
        var code = MakeCapability().GenerateRegistrationCode(MakeToolHarness());

        code.Should().NotContain("GetParentEventCoordinator");
        code.Should().NotContain("GetParentAgentMetadata");
        code.Should().NotContain("GetParentChatClient");
        code.Should().NotContain("ExecuteStreamingAsync");
        code.Should().NotContain("workflow.RunAsync");
        code.Should().NotContain("Analyze(s => s.CurrentMessages)");
        code.Should().NotContain("TextDeltaEvent");
        code.Should().NotContain("RegisterBackgroundTask");
        code.Should().NotContain("BackgroundTaskDescriptor");
    }

    [Fact]
    public void GeneratedCode_PreservesStreamEventsFlag()
    {
        var capability = MakeCapability();
        capability.StreamEvents = false;

        var code = capability.GenerateRegistrationCode(MakeToolHarness());

        code.Should().Contain("StreamEvents = false");
    }

    [Fact]
    public void GeneratedCode_PreservesInvocationModePolicy()
    {
        var capability = MakeCapability();
        capability.InvocationModePolicy = "ModelChoice";

        var code = capability.GenerateRegistrationCode(MakeToolHarness());

        code.Should().Contain("InvocationModePolicy = global::HPD.Agent.AgentInvocationModePolicy.ModelChoice");
        code.Should().Contain("\\\"invocationMode\\\"");
    }
}
