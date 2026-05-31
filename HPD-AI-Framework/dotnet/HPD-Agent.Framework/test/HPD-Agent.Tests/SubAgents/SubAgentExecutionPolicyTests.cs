using FluentAssertions;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.SubAgents;

public class SubAgentExecutionPolicyTests
{
    private static AgentConfig MinimalConfig() => new()
    {
        Name = "SubAgentUnderTest",
        SystemInstructions = "Test sub-agent.",
        Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" } }
    };

    [Fact]
    public void FromConfig_DefaultsToParentSessionForkedBranch()
    {
        var subAgent = SubAgent.FromConfig("Test", "desc", MinimalConfig());

        subAgent.SourceKind.Should().Be(SubAgentSourceKind.InlineConfig);
        subAgent.AgentConfig.Should().NotBeNull();
        subAgent.AgentId.Should().BeNull();
        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.ParentSession);
        subAgent.ExecutionPolicy.BranchPolicy.Should().Be(SubAgentBranchPolicy.ForkFromParentBranch);
        subAgent.ExecutionPolicy.BranchCompaction.Should().Be(SubAgentBranchCompaction.Inherit);
    }

    [Fact]
    public void FromAgentId_UsesStoredAgentSource()
    {
        var subAgent = SubAgent.FromAgentId("Reviewer", "Reviews code.", "code-reviewer");

        subAgent.SourceKind.Should().Be(SubAgentSourceKind.StoredAgent);
        subAgent.AgentId.Should().Be("code-reviewer");
        subAgent.AgentConfig.Should().BeNull();
        subAgent.ExecutionPolicy.Should().Be(SubAgentExecutionPolicy.Default);
    }

    [Fact]
    public void ParentBranchPolicy_IsExplicitOldWriteTarget()
    {
        var subAgent = SubAgent.FromConfig(
            "CoAuthor",
            "Writes directly in the caller branch.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentBranch());

        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.ParentSession);
        subAgent.ExecutionPolicy.BranchPolicy.Should().Be(SubAgentBranchPolicy.ParentBranch);
        subAgent.ExecutionPolicy.SharedSessionId.Should().BeNull();
        subAgent.ExecutionPolicy.ExistingBranchId.Should().BeNull();
        subAgent.ExecutionPolicy.BranchCompaction.Should().Be(SubAgentBranchCompaction.Inherit);
    }

    [Theory]
    [InlineData(SubAgentBranchCompaction.Enabled)]
    [InlineData(SubAgentBranchCompaction.Disabled)]
    public void ParentSessionForkedBranch_CanSetBranchCompaction(SubAgentBranchCompaction branchCompaction)
    {
        var policy = SubAgentExecutionPolicies.ParentSessionForkedBranch(branchCompaction);

        policy.SessionPolicy.Should().Be(SubAgentSessionPolicy.ParentSession);
        policy.BranchPolicy.Should().Be(SubAgentBranchPolicy.ForkFromParentBranch);
        policy.BranchCompaction.Should().Be(branchCompaction);
    }

    [Fact]
    public void SharedSessionFreshBranch_UsesFreshBranchPerCall()
    {
        var subAgent = SubAgent.FromConfig(
            "Architect",
            "Shared specialist.",
            MinimalConfig(),
            SubAgentExecutionPolicies.SharedSessionFreshBranch("architect-memory"));

        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.SharedSession);
        subAgent.ExecutionPolicy.BranchPolicy.Should().Be(SubAgentBranchPolicy.FreshBranch);
        subAgent.ExecutionPolicy.SharedSessionId.Should().Be("architect-memory");
        subAgent.ExecutionPolicy.ExistingBranchId.Should().BeNull();
    }

    [Fact]
    public void SharedSessionExistingBranch_UsesExistingBranch()
    {
        var subAgent = SubAgent.FromConfig(
            "Architect",
            "Shared specialist.",
            MinimalConfig(),
            SubAgentExecutionPolicies.SharedSessionExistingBranch("architect-memory", "main"));

        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.SharedSession);
        subAgent.ExecutionPolicy.BranchPolicy.Should().Be(SubAgentBranchPolicy.ExistingBranch);
        subAgent.ExecutionPolicy.SharedSessionId.Should().Be("architect-memory");
        subAgent.ExecutionPolicy.ExistingBranchId.Should().Be("main");
    }

    [Fact]
    public void SharedSessionPolicy_RequiresSharedSessionId()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.SharedSession,
            SubAgentBranchPolicy.FreshBranch);

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SharedSessionId*");
    }

    [Fact]
    public void ExistingBranchPolicy_RequiresExistingBranchId()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.ParentSession,
            SubAgentBranchPolicy.ExistingBranch);

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ExistingBranchId*");
    }

    [Fact]
    public void ForkFromParentBranch_RequiresParentSession()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.NewSession,
            SubAgentBranchPolicy.ForkFromParentBranch);

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ForkFromParentBranch*ParentSession*");
    }

    [Fact]
    public void ParentBranch_RequiresParentSession()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.SharedSession,
            SubAgentBranchPolicy.ParentBranch,
            SharedSessionId: "shared");

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ParentBranch*ParentSession*");
    }

    [Theory]
    [InlineData(SubAgentBranchPolicy.FreshBranch)]
    [InlineData(SubAgentBranchPolicy.ExistingBranch)]
    [InlineData(SubAgentBranchPolicy.ParentBranch)]
    public void BranchCompaction_RequiresForkFromParentBranch(SubAgentBranchPolicy branchPolicy)
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.ParentSession,
            branchPolicy,
            ExistingBranchId: branchPolicy == SubAgentBranchPolicy.ExistingBranch ? "existing" : null,
            BranchCompaction: SubAgentBranchCompaction.Enabled);

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*BranchCompaction*ForkFromParentBranch*");
    }
}
