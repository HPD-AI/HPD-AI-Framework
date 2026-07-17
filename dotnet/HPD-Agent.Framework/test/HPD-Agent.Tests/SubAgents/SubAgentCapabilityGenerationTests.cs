using FluentAssertions;
using HPD.Agent.SourceGenerator.Capabilities;
using Xunit;

namespace HPD.Agent.Tests.SubAgents;

public class SubAgentCapabilityGenerationTests
{
    private static ToolHarnessInfo MakeToolHarness(string name = "MyToolHarness") => new()
    {
        ClassName = name,
        Namespace = "Test.Namespace"
    };

    private static SubAgentCapability MakeCapability(string name = "ResearchAgent") => new()
    {
        Name = name,
        SubAgentName = name,
        MethodName = $"Create{name}",
        Description = "A test sub-agent",
        ParentToolHarnessName = "MyToolHarness",
        IsStatic = true,
        RequiresPermission = true
    };

    [Fact]
    public void GeneratedCode_DelegatesToSubAgentRuntimeInvokeAsync()
    {
        var code = MakeCapability().GenerateRegistrationCode(MakeToolHarness());

        code.Should().Contain("SubAgentRuntime.InvokeAsync");
        code.Should().Contain("SubAgentRuntime.SubAgentInvocationRequest");
        code.Should().Contain("Definition = subAgentDef");
        code.Should().Contain("Input = input");
        code.Should().Contain("TaskName = taskName");
        code.Should().Contain("ParentContext = functionContext");
        code.Should().Contain("RequestedMode = requestedMode");
        code.Should().Contain("return result.ToToolResult()");
    }

    [Fact]
    public void GeneratedCode_UsesInputArgument()
    {
        var code = MakeCapability().GenerateRegistrationCode(MakeToolHarness());

        code.Should().Contain("TryGetProperty(\"input\"");
        code.Should().Contain("TryGetProperty(\"taskName\"");
        code.Should().Contain("SubAgentInputArgs");
        code.Should().NotContain("TryGetProperty(\"query\"");
        code.Should().NotContain("SubAgentQueryArgs");
    }

    [Fact]
    public void GeneratedCode_DoesNotContainRuntimeOrchestration()
    {
        var code = MakeCapability().GenerateRegistrationCode(MakeToolHarness());

        code.Should().NotContain("GetParentAgentStore");
        code.Should().NotContain("GetParentChatClient");
        code.Should().NotContain("GetParentSessionStore");
        code.Should().NotContain("GetParentEventCoordinator");
        code.Should().NotContain("BuildAsync");
        code.Should().NotContain("ResolveInvocationRouteAsync");
        code.Should().NotContain("ResolveRouteAsync");
        code.Should().NotContain("MarkCompleted");
        code.Should().NotContain("MarkFailed");
        code.Should().NotContain("new Microsoft.Extensions.AI.ChatMessage");
        code.Should().NotContain("RegisterBackgroundTask");
        code.Should().NotContain("BackgroundTaskDescriptor");
    }

    [Fact]
    public void GeneratedCode_DoesNotContainOldSessionModeRouting()
    {
        var code = MakeCapability().GenerateRegistrationCode(MakeToolHarness());

        code.Should().NotContain("SubAgentSessionMode");
        code.Should().NotContain("SessionMode");
        code.Should().NotContain("case ");
        code.Should().NotContain("CreateSessionAsync(sessionId");
    }

    [Fact]
    public void AdditionalProperties_AdvertiseThreadNativeExecution()
    {
        var props = MakeCapability().GetAdditionalProperties();

        props["IsSubAgent"].Should().Be(true);
        props["ExecutionModel"].Should().Be("ThreadNative");
        props.Should().NotContainKey("SessionMode");
    }
}
