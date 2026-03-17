using Xunit;
using FluentAssertions;
using HPD.Agent.SourceGenerator.Capabilities;

namespace HPD.Agent.Tests.SubAgents;

/// <summary>
/// T16–T19: Unit tests that assert on the string output of SubAgentCapability.GenerateRegistrationCode().
/// These tests verify that the generated code contains the correct patterns for each Session mode.
///
/// Access requires [assembly: InternalsVisibleTo("HPD-Agent.Tests")] in the source generator project.
/// </summary>
public class SubAgentCapabilityGenerationTests
{
    private static ToolkitInfo MakeToolkit(string name = "MyToolkit") => new()
    {
        ClassName = name,
        Namespace = "Test.Namespace"
    };

    private static SubAgentCapability MakeCapability(string SessionMode, string name = "ResearchAgent") => new()
    {
        Name = name,
        SubAgentName = name,
        MethodName = $"Create{name}",
        Description = "A test sub-agent",
        ParentToolkitName = "MyToolkit",
        SessionMode = SessionMode,
        IsStatic = true,
        RequiresPermission = true
    };

    // ─────────────────────────────────────────────────────────────────────────
    // T16 — SharedSession generated code contains LoadSessionAsync guard
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void T16_SharedSession_GeneratedCode_ContainsLoadSessionAsyncGuard()
    {
        var capability = MakeCapability("SharedSession");
        var toolkit = MakeToolkit();

        var code = capability.GenerateRegistrationCode(toolkit);

        // The guard must load the existing session before deciding to create
        code.Should().Contain("LoadSessionAsync");
        // And only create when it doesn't already exist
        code.Should().Contain("existingSession == null");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T17 — PerSession generated code attaches parent store BEFORE BuildAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void T17_PerSession_GeneratedCode_AttachesParentStoreBeforeBuildAsync()
    {
        var capability = MakeCapability("PerSession");
        var toolkit = MakeToolkit();

        var code = capability.GenerateRegistrationCode(toolkit);

        // Store attachment must appear before BuildAsync in the generated output
        var storeAttachIndex = code.IndexOf("WithSessionStore(parentStore)", StringComparison.Ordinal);
        var buildAsyncIndex = code.IndexOf("BuildAsync()", StringComparison.Ordinal);

        storeAttachIndex.Should().BeGreaterThan(-1, "WithSessionStore(parentStore) must appear in PerSession code");
        buildAsyncIndex.Should().BeGreaterThan(-1, "BuildAsync() must appear in the code");
        storeAttachIndex.Should().BeLessThan(buildAsyncIndex,
            "parent store must be attached before BuildAsync() is called");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T18 — PerSession generated code does NOT call CreateSessionAsync
    //        for the happy path (parent session already exists)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void T18_PerSession_GeneratedCode_DoesNotCallCreateSessionAsync_InPerSessionCase()
    {
        var capability = MakeCapability("PerSession");
        var toolkit = MakeToolkit();

        var code = capability.GenerateRegistrationCode(toolkit);

        // Locate the PerSession case block
        var perSessionCaseIndex = code.IndexOf("case SubAgentSessionMode.PerSession:", StringComparison.Ordinal);
        var statelessCaseIndex = code.IndexOf("case SubAgentSessionMode.Stateless:", StringComparison.Ordinal);

        perSessionCaseIndex.Should().BeGreaterThan(-1);

        // Extract just the PerSession case body (up to the Stateless case)
        var perSessionBlock = statelessCaseIndex > perSessionCaseIndex
            ? code[perSessionCaseIndex..statelessCaseIndex]
            : code[perSessionCaseIndex..];

        // The PerSession block itself must NOT directly call CreateSessionAsync
        // (it uses goto for the fallback path, which is in the Stateless block)
        perSessionBlock.Should().NotContain("await agent.CreateSessionAsync",
            "PerSession inherits an existing session — it must NOT create a new one in the PerSession case block");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T19 — Stateless generated code always calls CreateSessionAsync with new GUID
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void T19_Stateless_GeneratedCode_AlwaysCallsCreateSessionAsyncWithNewGuid()
    {
        var capability = MakeCapability("Stateless");
        var toolkit = MakeToolkit();

        var code = capability.GenerateRegistrationCode(toolkit);

        // Locate the Stateless case block
        var statelessCaseIndex = code.IndexOf("case SubAgentSessionMode.Stateless:", StringComparison.Ordinal);
        statelessCaseIndex.Should().BeGreaterThan(-1);

        var statelessBlock = code[statelessCaseIndex..];

        // Must create a brand-new session ID via Guid.NewGuid()
        statelessBlock.Should().Contain("Guid.NewGuid()");
        // Must call CreateSessionAsync unconditionally (no if-guard)
        statelessBlock.Should().Contain("await agent.CreateSessionAsync(sessionId");
        // Must NOT have the existingSession guard (that's only for SharedSession)
        statelessBlock.Should().NotContain("LoadSessionAsync");
    }
}
