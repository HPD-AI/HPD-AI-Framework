using Xunit;
using HPD.Agent;
using HPD.Agent.Serialization;

using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for AgentBuilderSessionExtensions.WithSessionStore() overloads.
/// Verifies correct configuration of SessionStore and SessionStoreOptions.
/// </summary>
public class SessionBuilderExtensionTests : AgentTestBase
{
    //──────────────────────────────────────────────────────────────────
    // WithSessionStore(ISessionStore) - Manual Save Mode
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void WithSessionStore_StoreOnly_SetsAutoSave()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var builder = new AgentBuilder();

        // Act
        builder.WithSessionStore(store);

        // Assert
        Assert.Same(store, builder.Config.SessionStore);
        Assert.NotNull(builder.Config.SessionStoreOptions);
        Assert.True(builder.Config.SessionStoreOptions.PersistAfterTurn);
    }

    //──────────────────────────────────────────────────────────────────
    // WithSessionStore(ISessionStore, bool persistAfterTurn)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void WithSessionStore_WithPersistAfterTurnTrue_SetsPersistAfterTurn()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var builder = new AgentBuilder();

        // Act
        builder.WithSessionStore(store, persistAfterTurn: true);

        // Assert
        Assert.Same(store, builder.Config.SessionStore);
        Assert.True(builder.Config.SessionStoreOptions?.PersistAfterTurn);
    }

    [Fact]
    public void WithSessionStore_WithPersistAfterTurnFalse_SetsManualSave()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var builder = new AgentBuilder();

        // Act
        builder.WithSessionStore(store, persistAfterTurn: false);

        // Assert
        Assert.Same(store, builder.Config.SessionStore);
        Assert.False(builder.Config.SessionStoreOptions?.PersistAfterTurn);
    }

    //──────────────────────────────────────────────────────────────────
    // WithSessionStore(ISessionStore, Action<SessionStoreOptions>)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void WithSessionStore_WithConfigureAction_AllowsFullConfiguration()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var builder = new AgentBuilder();

        // Act
        builder.WithSessionStore(store, options =>
        {
            options.PersistAfterTurn = true;
        });

        // Assert
        var opts = builder.Config.SessionStoreOptions;
        Assert.NotNull(opts);
        Assert.True(opts.PersistAfterTurn);
    }

    //──────────────────────────────────────────────────────────────────
    // WithSessionStore(FileSessionStore, bool persistAfterTurn)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void WithSessionStore_WithPath_CreatesFileSessionStore()
    {
        // Arrange
        var builder = new AgentBuilder();
        var tempPath = Path.Combine(Path.GetTempPath(), $"session-test-{Guid.NewGuid()}");

        try
        {
            // Act
            builder.WithSessionStore(
                new FileSessionStore(tempPath, HPD.Agent.Tests.TestEventApplication.Codec),
                persistAfterTurn: true);

            // Assert
            Assert.NotNull(builder.Config.SessionStore);
            Assert.IsType<FileSessionStore>(builder.Config.SessionStore);
            Assert.True(builder.Config.SessionStoreOptions?.PersistAfterTurn);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void WithSessionStore_WithPathDefaultPersistAfterTurn_SetsAutoSave()
    {
        // Arrange
        var builder = new AgentBuilder();
        var tempPath = Path.Combine(Path.GetTempPath(), $"session-test-{Guid.NewGuid()}");

        try
        {
            // Act
            builder.WithSessionStore(
                new FileSessionStore(tempPath, HPD.Agent.Tests.TestEventApplication.Codec));

            // Assert
            Assert.True(builder.Config.SessionStoreOptions?.PersistAfterTurn);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void WithInMemorySessionStore_DefersConstructionUntilCompositionResolution()
    {
        var builder = new AgentBuilder().WithInMemorySessionStore();

        Assert.Null(builder.Config.SessionStore);
        Assert.NotNull(builder._sessionStoreFactory);
        Assert.IsType<InMemorySessionStore>(builder._sessionStoreFactory!(CoreAgentEventComposition.Instance));
    }

    [Fact]
    public void WithFileSessionStore_SelectsDeferredRestartDurableContentDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), $"session-test-{Guid.NewGuid()}");
        var builder = new AgentBuilder().WithFileSessionStore(path);

        Assert.Null(builder.Config.SessionStore);
        Assert.NotNull(builder._sessionStoreFactory);
        Assert.NotNull(builder._implicitContentStoreFactory);
        Assert.Equal(
            ContentStorePersistenceCapability.RestartDurable,
            builder._implicitContentStoreFactory!().PersistenceCapability);
    }

    [Fact]
    public void SessionStoreFactory_RejectsExplicitStoreInEitherOrder()
    {
        var store = new InMemorySessionStore(TestEventApplication.Codec);

        Assert.Throws<InvalidOperationException>(() =>
            new AgentBuilder().WithInMemorySessionStore().WithSessionStore(store));
        Assert.Throws<InvalidOperationException>(() =>
            new AgentBuilder().WithSessionStore(store).WithInMemorySessionStore());
    }

    [Fact]
    public async Task BuildFailure_DisposesBuilderOwnedContentStore()
    {
        var contentStore = new TrackingContentStore();
        var builder = new AgentBuilder().WithInMemorySessionStore();
        builder.Config.EventComposition = TestEventApplication.Composition;
        builder._implicitContentStoreFactory = () => contentStore;
        builder.Config.Skills.ActivationLifetime = SkillActivationLifetime.Session;

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync());

        Assert.True(contentStore.Disposed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync());
    }

    private sealed class TrackingContentStore : InMemoryContentStore, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    //──────────────────────────────────────────────────────────────────
    // SESSION STORE OPTIONS DEFAULTS
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void SessionStoreOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new SessionStoreOptions();

        // Assert
        Assert.False(options.PersistAfterTurn);
    }

    //──────────────────────────────────────────────────────────────────
    // NULL ARGUMENT CHECKS
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void WithSessionStore_NullBuilder_Throws()
    {
        // Arrange
        AgentBuilder builder = null!;
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.WithSessionStore(store));
    }

    [Fact]
    public void WithSessionStore_NullStore_Throws()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.WithSessionStore((ISessionStore)null!));
    }

    //──────────────────────────────────────────────────────────────────
    // FLUENT API CHAINING
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void WithSessionStore_ReturnsBuilder_ForChaining()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var builder = new AgentBuilder();

        // Act
        var result = builder.WithSessionStore(store);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void WithSessionStore_CanChainMultipleCalls()
    {
        // Arrange
        var builder = new AgentBuilder();
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);

        // Act - Chain multiple builder methods
        var result = builder
            .WithSessionStore(store, persistAfterTurn: true)
            .WithName("TestAgent")
            .WithMaxFunctionCallTurns(50);

        // Assert
        Assert.Same(builder, result);
        Assert.Equal("TestAgent", builder.Config.Name);
        Assert.Equal(50, builder.Config.MaxAgenticIterations);
        Assert.Same(store, builder.Config.SessionStore);
    }
}
