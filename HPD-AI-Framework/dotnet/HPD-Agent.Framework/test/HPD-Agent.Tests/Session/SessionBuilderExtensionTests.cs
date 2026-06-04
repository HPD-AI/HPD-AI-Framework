using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

public class SessionBuilderExtensionTests : AgentTestBase
{
    [Fact]
    public void WithSessionRepository_SetsRepositoryAndAutoSaveByDefault()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var builder = new AgentBuilder();

        var result = builder.WithSessionRepository(repository);

        Assert.Same(builder, result);
        Assert.Same(repository, builder.Config.SessionRepository);
        Assert.True(builder.Config.SessionRepositoryOptions?.PersistAfterTurn);
    }

    [Fact]
    public void WithSessionRepository_WithPersistAfterTurnFalse_SetsManualSave()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var builder = new AgentBuilder();

        builder.WithSessionRepository(repository, persistAfterTurn: false);

        Assert.Same(repository, builder.Config.SessionRepository);
        Assert.False(builder.Config.SessionRepositoryOptions?.PersistAfterTurn);
    }

    [Fact]
    public void WithSessionRepository_WithConfigureAction_AllowsFullConfiguration()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var builder = new AgentBuilder();

        builder.WithSessionRepository(repository, options =>
        {
            options.PersistAfterTurn = true;
        });

        Assert.Same(repository, builder.Config.SessionRepository);
        Assert.True(builder.Config.SessionRepositoryOptions?.PersistAfterTurn);
    }

    [Fact]
    public void SessionRepositoryOptions_DefaultValues_AreCorrect()
    {
        var options = new SessionRepositoryOptions();

        Assert.False(options.PersistAfterTurn);
    }

    [Fact]
    public void WithSessionRepository_NullBuilder_Throws()
    {
        AgentBuilder builder = null!;
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());

        Assert.Throws<ArgumentNullException>(() => builder.WithSessionRepository(repository));
    }

    [Fact]
    public void WithSessionRepository_NullRepository_Throws()
    {
        var builder = new AgentBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithSessionRepository(null!));
    }

    [Fact]
    public void WithSessionRepository_NullConfigure_Throws()
    {
        var builder = new AgentBuilder();
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());

        Assert.Throws<ArgumentNullException>(() => builder.WithSessionRepository(repository, configure: null!));
    }

    [Fact]
    public void WithSessionRepository_CanChainMultipleCalls()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var builder = new AgentBuilder();

        var result = builder
            .WithSessionRepository(repository, persistAfterTurn: true)
            .WithName("TestAgent")
            .WithMaxFunctionCallTurns(50);

        Assert.Same(builder, result);
        Assert.Equal("TestAgent", builder.Config.Name);
        Assert.Equal(50, builder.Config.MaxAgenticIterations);
        Assert.Same(repository, builder.Config.SessionRepository);
    }
}
