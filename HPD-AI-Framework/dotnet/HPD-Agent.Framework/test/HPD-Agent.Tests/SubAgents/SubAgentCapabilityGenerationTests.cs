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
    public void GeneratedCode_ResolvesThreadNativeRoute()
    {
        var code = MakeCapability().GenerateRegistrationCode(MakeToolHarness());

        code.Should().Contain("SubAgentRuntime.ResolveRouteAsync");
        code.Should().Contain("SessionId = route.SessionId");
        code.Should().Contain("ThreadId = route.ThreadId");
        code.Should().Contain("SubAgentRuntime.MarkCompleted");
        code.Should().Contain("SubAgentRuntime.MarkFailed");
    }

    [Fact]
    public void GeneratedCode_BuildsFromInlineConfigOrStoredAgentId()
    {
        var code = MakeCapability().GenerateRegistrationCode(MakeToolHarness());

        code.Should().Contain("subAgentDef.SourceKind == SubAgentSourceKind.StoredAgent");
        code.Should().Contain("new AgentBuilder().WithAgentId(subAgentDef.AgentId)");
        code.Should().Contain("functionContext?.GetParentAgentStore()");
        code.Should().Contain("subAgentDef.SourceKind == SubAgentSourceKind.InlineConfig");
        code.Should().Contain("new AgentBuilder(subAgentDef.AgentConfig)");
    }

    [Fact]
    public void GeneratedCode_AttachesParentSessionStoreBeforeBuildAsync()
    {
        var code = MakeCapability().GenerateRegistrationCode(MakeToolHarness());

        var storeAttachIndex = code.IndexOf("WithSessionStore(parentStore)", StringComparison.Ordinal);
        var buildAsyncIndex = code.IndexOf("BuildAsync()", StringComparison.Ordinal);

        storeAttachIndex.Should().BeGreaterThan(-1);
        buildAsyncIndex.Should().BeGreaterThan(-1);
        storeAttachIndex.Should().BeLessThan(buildAsyncIndex);
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
