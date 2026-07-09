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
    public void FromConfig_DefaultsToParentSessionForkedThread()
    {
        var subAgent = SubAgent.FromConfig("Test", "desc", MinimalConfig());

        subAgent.SourceKind.Should().Be(SubAgentSourceKind.InlineConfig);
        subAgent.AgentConfig.Should().NotBeNull();
        subAgent.AgentId.Should().BeNull();
        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.ParentSession);
        subAgent.ExecutionPolicy.ThreadPolicy.Should().Be(SubAgentThreadPolicy.ForkFromParentThread);
        subAgent.ExecutionPolicy.ThreadCompaction.Should().BeNull();
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
    public void ParentThreadPolicy_IsExplicitOldWriteTarget()
    {
        var subAgent = SubAgent.FromConfig(
            "CoAuthor",
            "Writes directly in the caller thread.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentThread());

        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.ParentSession);
        subAgent.ExecutionPolicy.ThreadPolicy.Should().Be(SubAgentThreadPolicy.ParentThread);
        subAgent.ExecutionPolicy.SharedSessionId.Should().BeNull();
        subAgent.ExecutionPolicy.ExistingThreadId.Should().BeNull();
        subAgent.ExecutionPolicy.ThreadCompaction.Should().BeNull();
    }

    [Theory]
    [InlineData(ThreadForkCompactionMode.Enabled)]
    [InlineData(ThreadForkCompactionMode.Disabled)]
    public void ParentSessionForkedThread_CanSetThreadCompaction(ThreadForkCompactionMode mode)
    {
        var compaction = new ThreadForkCompactionOptions
        {
            Mode = mode,
            PreferCache = false,
            Strategy = new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 3 }
        };

        var policy = SubAgentExecutionPolicies.ParentSessionForkedThread(compaction);

        policy.SessionPolicy.Should().Be(SubAgentSessionPolicy.ParentSession);
        policy.ThreadPolicy.Should().Be(SubAgentThreadPolicy.ForkFromParentThread);
        policy.ThreadCompaction.Should().BeSameAs(compaction);
    }

    [Fact]
    public void SharedSessionFreshThread_UsesFreshThreadPerCall()
    {
        var subAgent = SubAgent.FromConfig(
            "Architect",
            "Shared specialist.",
            MinimalConfig(),
            SubAgentExecutionPolicies.SharedSessionFreshThread("architect-memory"));

        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.SharedSession);
        subAgent.ExecutionPolicy.ThreadPolicy.Should().Be(SubAgentThreadPolicy.FreshThread);
        subAgent.ExecutionPolicy.SharedSessionId.Should().Be("architect-memory");
        subAgent.ExecutionPolicy.ExistingThreadId.Should().BeNull();
    }

    [Fact]
    public void SharedSessionExistingThread_UsesExistingThread()
    {
        var subAgent = SubAgent.FromConfig(
            "Architect",
            "Shared specialist.",
            MinimalConfig(),
            SubAgentExecutionPolicies.SharedSessionExistingThread("architect-memory", "main"));

        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.SharedSession);
        subAgent.ExecutionPolicy.ThreadPolicy.Should().Be(SubAgentThreadPolicy.ExistingThread);
        subAgent.ExecutionPolicy.SharedSessionId.Should().Be("architect-memory");
        subAgent.ExecutionPolicy.ExistingThreadId.Should().Be("main");
    }

    [Fact]
    public void SharedSessionPolicy_RequiresSharedSessionId()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.SharedSession,
            SubAgentThreadPolicy.FreshThread);

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SharedSessionId*");
    }

    [Fact]
    public void ExistingThreadPolicy_RequiresExistingThreadId()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.ParentSession,
            SubAgentThreadPolicy.ExistingThread);

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ExistingThreadId*");
    }

    [Fact]
    public void ForkFromParentThread_RequiresParentSession()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.NewSession,
            SubAgentThreadPolicy.ForkFromParentThread);

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ForkFromParentThread*ParentSession*");
    }

    [Fact]
    public void ParentThread_RequiresParentSession()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.SharedSession,
            SubAgentThreadPolicy.ParentThread,
            SharedSessionId: "shared");

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ParentThread*ParentSession*");
    }

    [Theory]
    [InlineData(SubAgentThreadPolicy.FreshThread)]
    [InlineData(SubAgentThreadPolicy.ExistingThread)]
    [InlineData(SubAgentThreadPolicy.ParentThread)]
    public void ThreadCompaction_RequiresForkFromParentThread(SubAgentThreadPolicy threadPolicy)
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.ParentSession,
            threadPolicy,
            ExistingThreadId: threadPolicy == SubAgentThreadPolicy.ExistingThread ? "existing" : null,
            ThreadCompaction: ThreadForkCompactionOptions.Enabled);

        var act = () => SubAgent.FromConfig("Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ThreadCompaction*ForkFromParentThread*");
    }
}
