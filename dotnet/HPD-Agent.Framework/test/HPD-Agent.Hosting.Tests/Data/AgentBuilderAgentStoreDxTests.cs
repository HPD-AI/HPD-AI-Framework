using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Hosting.Tests.Infrastructure;

namespace HPD.Agent.Hosting.Tests.Data;

public class AgentBuilderAgentStoreDxTests
{
    [Fact]
    public async Task BuildAsync_WithAgentId_UsesSharedDefaultInMemoryStore()
    {
        var agentId = $"default-store-{Guid.NewGuid():N}";

        await CreateBuilder()
            .WithAgentId(agentId)
            .WithName("Shared Default Agent")
            .BuildAsync();

        var loaded = await CreateBuilder()
            .WithAgentId(agentId)
            .BuildAsync();

        loaded.Config!.Name.Should().Be("Shared Default Agent");
    }

    [Fact]
    public async Task BuildAsync_WithPersistOnBuild_SavesDefinitionToConfiguredStore()
    {
        var store = new InMemoryAgentStore();
        var agentId = $"persist-{Guid.NewGuid():N}";

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentStore(store, persistOnBuild: true)
            .WithName("Persisted Agent")
            .BuildAsync();

        var stored = await store.LoadAsync(agentId);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Persisted Agent");
        stored.Config.Name.Should().Be(agent.Config!.Name);
        stored.Config.AgentId.Should().BeNull();
        stored.Config.AgentStore.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithAgentId_DefaultsNameToAgentId()
    {
        var agentId = $"identity-{Guid.NewGuid():N}";

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .BuildAsync();

        agent.AgentId.Should().Be(agentId);
        agent.Name.Should().Be(agentId);
        agent.Config!.Name.Should().Be(agentId);
    }

    [Fact]
    public async Task BuildAsync_WithAgentId_DoesNotOverwriteExplicitName()
    {
        var agentId = $"identity-name-{Guid.NewGuid():N}";

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .WithName("Display Name")
            .BuildAsync();

        agent.AgentId.Should().Be(agentId);
        agent.Name.Should().Be("Display Name");
        agent.Config!.Name.Should().Be("Display Name");
    }

    [Fact]
    public async Task BuildAsync_WithoutAgentId_AgentIdFallsBackToName()
    {
        var agent = await CreateBuilder()
            .WithName("Only Name")
            .BuildAsync();

        agent.AgentId.Should().Be("Only Name");
        agent.Name.Should().Be("Only Name");
    }

    [Fact]
    public async Task BuildAsync_WithConfiguredStoreAndMissingDefinition_DoesNotSaveByDefault()
    {
        var store = new RecordingAgentStore();
        var agentId = $"missing-no-save-{Guid.NewGuid():N}";

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentStore(store)
            .WithName("Transient Agent")
            .BuildAsync();

        agent.Config!.Name.Should().Be("Transient Agent");
        store.SaveCount.Should().Be(0);
        (await store.LoadAsync(agentId)).Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithConfiguredStore_LoadsExistingDefinition()
    {
        var store = new RecordingAgentStore();
        var agentId = $"load-{Guid.NewGuid():N}";

        await store.SaveAsync(new StoredAgent
        {
            Id = agentId,
            Name = "Stored Agent",
            Config = CreateConfig("Stored Agent")
        });
        store.SaveCount = 0;

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentStore(store)
            .BuildAsync();

        agent.Config!.Name.Should().Be("Stored Agent");
        agent.Config.SystemInstructions.Should().Be("Stored instructions");
        store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task BuildAsync_CurrentBuilderValuesOverrideStoredDefaults()
    {
        var store = new InMemoryAgentStore();
        var agentId = $"override-{Guid.NewGuid():N}";

        await store.SaveAsync(new StoredAgent
        {
            Id = agentId,
            Name = "Stored Agent",
            Config = CreateConfig("Stored Agent")
        });

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentStore(store)
            .WithName("Builder Override")
            .BuildAsync();

        agent.Config!.Name.Should().Be("Builder Override");
        agent.Config.SystemInstructions.Should().Be("Stored instructions");
    }

    [Fact]
    public async Task BuildAsync_PersistOnBuild_PreservesExistingMetadataAndCreatedAt()
    {
        var store = new InMemoryAgentStore();
        var agentId = $"metadata-{Guid.NewGuid():N}";
        var createdAt = DateTime.UtcNow.AddDays(-2);
        var metadata = new Dictionary<string, object> { ["owner"] = "platform" };

        await store.SaveAsync(new StoredAgent
        {
            Id = agentId,
            Name = "Original Stored Agent",
            Config = CreateConfig("Original Stored Agent"),
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            Metadata = metadata
        });

        await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentStore(store, persistOnBuild: true)
            .WithName("Updated Stored Agent")
            .BuildAsync();

        var stored = await store.LoadAsync(agentId);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Updated Stored Agent");
        stored.CreatedAt.Should().Be(createdAt);
        stored.UpdatedAt.Should().BeAfter(createdAt);
        stored.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public async Task BuildAsync_WithOptionsCallback_PersistsWhenEnabled()
    {
        var store = new InMemoryAgentStore();
        var agentId = $"options-{Guid.NewGuid():N}";

        await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentStore(store, options => options.PersistOnBuild = true)
            .WithName("Options Persisted")
            .BuildAsync();

        var stored = await store.LoadAsync(agentId);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Options Persisted");
    }

    [Fact]
    public async Task BuildAsync_WithOptionsCallback_DoesNotPersistWhenDisabled()
    {
        var store = new RecordingAgentStore();
        var agentId = $"options-disabled-{Guid.NewGuid():N}";

        await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentStore(store, options => options.PersistOnBuild = false)
            .WithName("Options Not Persisted")
            .BuildAsync();

        store.SaveCount.Should().Be(0);
        (await store.LoadAsync(agentId)).Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithStoragePath_PersistsAndLoadsDefinition()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-store-{Guid.NewGuid():N}");
        var agentId = $"json-{Guid.NewGuid():N}";

        try
        {
            await CreateBuilder()
                .WithAgentId(agentId)
                .WithAgentStore(path)
                .WithName("Json Stored Agent")
                .BuildAsync();

            var loaded = await CreateBuilder()
                .WithAgentId(agentId)
                .WithAgentStore(path, persistOnBuild: false)
                .BuildAsync();

            loaded.Config!.Name.Should().Be("Json Stored Agent");
            File.Exists(Path.Combine(path, agentId, "agent.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_WithStoragePathAndPersistDisabled_DoesNotCreateDefinition()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-store-{Guid.NewGuid():N}");
        var agentId = $"json-no-save-{Guid.NewGuid():N}";

        try
        {
            await CreateBuilder()
                .WithAgentId(agentId)
                .WithAgentStore(path, persistOnBuild: false)
                .WithName("Json Transient Agent")
                .BuildAsync();

            File.Exists(Path.Combine(path, agentId, "agent.json")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void WithAgentId_Throws_WhenAgentIdIsNullOrWhitespace()
    {
        var builder = CreateBuilder();

        var nullAct = () => builder.WithAgentId(null!);
        var emptyAct = () => builder.WithAgentId(" ");

        nullAct.Should().Throw<ArgumentException>();
        emptyAct.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithAgentStore_Throws_WhenStoreIsNull()
    {
        var builder = CreateBuilder();

        var plainAct = () => builder.WithAgentStore((IAgentStore)null!);
        var boolAct = () => builder.WithAgentStore((IAgentStore)null!, persistOnBuild: true);
        var optionsAct = () => builder.WithAgentStore((IAgentStore)null!, options => options.PersistOnBuild = true);

        plainAct.Should().Throw<ArgumentNullException>();
        boolAct.Should().Throw<ArgumentNullException>();
        optionsAct.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithAgentStore_Throws_WhenOptionsCallbackIsNull()
    {
        var builder = CreateBuilder();
        var store = new InMemoryAgentStore();

        var act = () => builder.WithAgentStore(store, configure: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithAgentStore_Throws_WhenStoragePathIsNullOrWhitespace()
    {
        var builder = CreateBuilder();

        var nullAct = () => builder.WithAgentStore((string)null!);
        var emptyAct = () => builder.WithAgentStore(" ");

        nullAct.Should().Throw<ArgumentException>();
        emptyAct.Should().Throw<ArgumentException>();
    }

    private static AgentBuilder CreateBuilder()
        => new AgentBuilder(CreateConfig(), new TestProviderRegistry(new FakeChatClient()));

    private static AgentConfig CreateConfig(string name = "HPD-Agent")
        => new()
        {
            Name = name,
            SystemInstructions = name == "HPD-Agent"
                ? "You are a helpful assistant."
                : "Stored instructions",
            Clients = new AgentClientsConfig { Chat = new ProviderClientConfig {
                ProviderKey = "test",
                ModelName = "test-model"
            } }
        };

    private sealed class RecordingAgentStore : IAgentStore
    {
        private readonly InMemoryAgentStore _inner = new();

        public int SaveCount { get; set; }

        public Task<StoredAgent?> LoadAsync(string agentId, CancellationToken ct = default)
            => _inner.LoadAsync(agentId, ct);

        public async Task SaveAsync(StoredAgent agent, CancellationToken ct = default)
        {
            SaveCount++;
            await _inner.SaveAsync(agent, ct);
        }

        public Task DeleteAsync(string agentId, CancellationToken ct = default)
            => _inner.DeleteAsync(agentId, ct);

        public Task<List<string>> ListIdsAsync(CancellationToken ct = default)
            => _inner.ListIdsAsync(ct);
    }
}
