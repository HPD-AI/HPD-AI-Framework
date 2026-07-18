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
        var subAgent = SubAgent.FromConfig("test", "Test", "desc", MinimalConfig());

        subAgent.Configuration.Should().BeOfType<SuppliedAgentConfiguration>();
        subAgent.AgentId.Should().Be("test");
        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.ParentSession);
        subAgent.ExecutionPolicy.ThreadPolicy.Should().Be(SubAgentThreadPolicy.ForkFromParentThread);
        subAgent.ExecutionPolicy.ThreadCompaction.Should().BeNull();
    }

    [Fact]
    public void FromAgentId_UsesStoredAgentSource()
    {
        var subAgent = SubAgent.FromAgentId("code-reviewer", "Reviewer", "Reviews code.");

        subAgent.Configuration.Should().BeOfType<StoredAgentConfiguration>();
        subAgent.AgentId.Should().Be("code-reviewer");
        subAgent.ExecutionPolicy.Should().Be(SubAgentExecutionPolicy.Default);
    }

    [Fact]
    public void FromParent_UsesParentConfigurationSource()
    {
        var subAgent = SubAgent.FromParent(
            "co-author",
            "CoAuthor",
            "Writes directly in the caller thread.",
            SubAgentExecutionPolicies.ParentSessionFreshThread());

        subAgent.ExecutionPolicy.SessionPolicy.Should().Be(SubAgentSessionPolicy.ParentSession);
        subAgent.Configuration.Should().BeOfType<ParentAgentConfiguration>();
        subAgent.ExecutionPolicy.ThreadPolicy.Should().Be(SubAgentThreadPolicy.FreshThread);
        subAgent.ExecutionPolicy.SharedSessionId.Should().BeNull();
        subAgent.ExecutionPolicy.ExistingThreadId.Should().BeNull();
        subAgent.ExecutionPolicy.ThreadCompaction.Should().BeNull();
    }

    [Fact]
    public void ParentSessionForkedThread_CanSetThreadCompaction()
    {
        var compaction = new CompactionSpecification
        {
            Point = new CompactAtCurrentHead(),
            Preservation = new PreservePreviousTurns(3),
            Strategy = new RemovalCompaction(),
            CommitMode = CompactionCommitMode.Hard
        };

        var forkCompaction = new ApplyThreadForkCompaction(compaction);
        var policy = SubAgentExecutionPolicies.ParentSessionForkedThread(forkCompaction);

        policy.SessionPolicy.Should().Be(SubAgentSessionPolicy.ParentSession);
        policy.ThreadPolicy.Should().Be(SubAgentThreadPolicy.ForkFromParentThread);
        policy.ThreadCompaction.Should().BeSameAs(forkCompaction);
    }

    [Fact]
    public void SharedSessionFreshThread_UsesFreshThreadPerCall()
    {
        var subAgent = SubAgent.FromConfig(
            "architect",
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
            "architect",
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

        var act = () => SubAgent.FromConfig("bad", "Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SharedSessionId*");
    }

    [Fact]
    public void ExistingThreadPolicy_RequiresExistingThreadId()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.ParentSession,
            SubAgentThreadPolicy.ExistingThread);

        var act = () => SubAgent.FromConfig("bad", "Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ExistingThreadId*");
    }

    [Fact]
    public void ForkFromParentThread_RequiresParentSession()
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.NewSession,
            SubAgentThreadPolicy.ForkFromParentThread);

        var act = () => SubAgent.FromConfig("bad", "Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ForkFromParentThread*ParentSession*");
    }

    [Theory]
    [InlineData(SubAgentThreadPolicy.FreshThread)]
    [InlineData(SubAgentThreadPolicy.ExistingThread)]
    public void ThreadCompaction_RequiresForkFromParentThread(SubAgentThreadPolicy threadPolicy)
    {
        var policy = new SubAgentExecutionPolicy(
            SubAgentSessionPolicy.ParentSession,
            threadPolicy,
            ExistingThreadId: threadPolicy == SubAgentThreadPolicy.ExistingThread ? "existing" : null,
            ThreadCompaction: new ApplyThreadForkCompaction(new CompactionSpecification
            {
                Point = new CompactAtCurrentHead(),
                Strategy = new RemovalCompaction(),
                CommitMode = CompactionCommitMode.Hard
            }));

        var act = () => SubAgent.FromConfig("bad", "Bad", "desc", MinimalConfig(), policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ThreadCompaction*ForkFromParentThread*");
    }
}
