using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Hosting.Tests.Infrastructure;
using System.Reflection;

namespace HPD.Agent.Hosting.Tests.Data;

public class AgentBuilderAgentRepositoryDxTests
{
    [Fact]
    public async Task BuildAsync_WithAgentId_UsesSharedDefaultInMemoryWorkspace()
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
    public async Task BuildAsync_WithPersistOnBuild_SavesDefinitionToConfiguredRepository()
    {
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());
        var agentId = $"persist-{Guid.NewGuid():N}";

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentRepository(repository, persistOnBuild: true)
            .WithName("Persisted Agent")
            .BuildAsync();

        var stored = await repository.LoadAsync(agentId);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Persisted Agent");
        stored.Config.Name.Should().Be(agent.Config!.Name);
        stored.Config.AgentId.Should().BeNull();
        stored.Config.AgentRepository.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithAgentRepository_PersistsDefinitionToWorkspaceRepository()
    {
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());
        var agentId = $"repo-persist-{Guid.NewGuid():N}";

        await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentRepository(repository, persistOnBuild: true)
            .WithName("Repository Agent")
            .BuildAsync();

        var stored = await repository.LoadAsync(agentId);

        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Repository Agent");
        stored.Config.AgentRepository.Should().BeNull();
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
    public async Task BuildAsync_WithConfiguredRepositoryAndMissingDefinition_DoesNotSaveByDefault()
    {
        var repository = new RecordingAgentRepository();
        var agentId = $"missing-no-save-{Guid.NewGuid():N}";

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentRepository(repository)
            .WithName("Transient Agent")
            .BuildAsync();

        agent.Config!.Name.Should().Be("Transient Agent");
        repository.SaveCount.Should().Be(0);
        (await repository.LoadAsync(agentId)).Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithConfiguredRepository_LoadsExistingDefinition()
    {
        var repository = new RecordingAgentRepository();
        var agentId = $"load-{Guid.NewGuid():N}";

        await repository.SaveAsync(new StoredAgent
        {
            Id = agentId,
            Name = "Stored Agent",
            Config = CreateConfig("Stored Agent")
        });
        repository.SaveCount = 0;

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentRepository(repository)
            .BuildAsync();

        agent.Config!.Name.Should().Be("Stored Agent");
        agent.Config.SystemInstructions.Should().Be("Stored instructions");
        repository.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task BuildAsync_CurrentBuilderValuesOverrideStoredDefaults()
    {
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());
        var agentId = $"override-{Guid.NewGuid():N}";

        await repository.SaveAsync(new StoredAgent
        {
            Id = agentId,
            Name = "Stored Agent",
            Config = CreateConfig("Stored Agent")
        });

        var agent = await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentRepository(repository)
            .WithName("Builder Override")
            .BuildAsync();

        agent.Config!.Name.Should().Be("Builder Override");
        agent.Config.SystemInstructions.Should().Be("Stored instructions");
    }

    [Fact]
    public async Task BuildAsync_PersistOnBuild_PreservesExistingMetadataAndCreatedAt()
    {
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());
        var agentId = $"metadata-{Guid.NewGuid():N}";
        var createdAt = DateTime.UtcNow.AddDays(-2);
        var metadata = new Dictionary<string, object> { ["owner"] = "platform" };

        await repository.SaveAsync(new StoredAgent
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
            .WithAgentRepository(repository, persistOnBuild: true)
            .WithName("Updated Stored Agent")
            .BuildAsync();

        var stored = await repository.LoadAsync(agentId);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Updated Stored Agent");
        stored.CreatedAt.Should().Be(createdAt);
        stored.UpdatedAt.Should().BeAfter(createdAt);
        stored.Metadata.Should().ContainKey("owner");
        stored.Metadata!["owner"].Should().Be("platform");
    }

    [Fact]
    public async Task BuildAsync_WithOptionsCallback_PersistsWhenEnabled()
    {
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());
        var agentId = $"options-{Guid.NewGuid():N}";

        await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentRepository(repository, options => options.PersistOnBuild = true)
            .WithName("Options Persisted")
            .BuildAsync();

        var stored = await repository.LoadAsync(agentId);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Options Persisted");
    }

    [Fact]
    public async Task BuildAsync_WithOptionsCallback_DoesNotPersistWhenDisabled()
    {
        var repository = new RecordingAgentRepository();
        var agentId = $"options-disabled-{Guid.NewGuid():N}";

        await CreateBuilder()
            .WithAgentId(agentId)
            .WithAgentRepository(repository, options => options.PersistOnBuild = false)
            .WithName("Options Not Persisted")
            .BuildAsync();

        repository.SaveCount.Should().Be(0);
        (await repository.LoadAsync(agentId)).Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithStoragePath_PersistsAndLoadsWorkspaceDefinition()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-store-{Guid.NewGuid():N}");
        var agentId = $"json-{Guid.NewGuid():N}";

        try
        {
            await CreateBuilder()
                .WithAgentId(agentId)
                .WithJsonWorkspace(path)
                .WithName("Workspace Stored Agent")
                .BuildAsync();

            var loaded = await CreateBuilder()
                .WithAgentId(agentId)
                .WithJsonWorkspace(path, persistOnBuild: false)
                .BuildAsync();

            loaded.Config!.Name.Should().Be("Workspace Stored Agent");
            File.Exists(Path.Combine(path, "workspace.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void WithJsonWorkspace_ConfiguresSessionAgentAndWorkspaceToolingOverSameWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-workspace-{Guid.NewGuid():N}");
        var builder = CreateBuilder();

        builder.WithJsonWorkspace(path);

        var agentRepository = builder.Config.AgentRepository.Should()
            .BeOfType<WorkspaceAgentRepository>().Subject;
        var sessionRepository = builder.Config.SessionRepository.Should()
            .BeOfType<WorkspaceSessionRepository>().Subject;

        agentRepository.Workspace.Should().BeSameAs(sessionRepository.Workspace);
        builder._workspaceStore.Should().BeSameAs(agentRepository.Workspace);
        builder.Config.AgentRepositoryOptions!.PersistOnBuild.Should().BeTrue();
        builder.Config.SessionRepositoryOptions!.PersistAfterTurn.Should().BeTrue();
    }

    [Fact]
    public void WithJsonWorkspace_AfterUseDefaultWorkspaceContent_RetargetsDefaultContentTooling()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-workspace-{Guid.NewGuid():N}");
        var builder = CreateBuilder();

        builder.UseDefaultWorkspaceContent();
        var registeredWorkspaceStore = builder._workspaceStore;

        builder.WithJsonWorkspace(path);

        var agentRepository = builder.Config.AgentRepository.Should()
            .BeOfType<WorkspaceAgentRepository>().Subject;
        var sessionRepository = builder.Config.SessionRepository.Should()
            .BeOfType<WorkspaceSessionRepository>().Subject;
        var toolHarness = builder._instanceRegistrations
            .Single(registration => registration.ToolTypeName == nameof(WorkspaceContentToolHarness))
            .Instance.Should().BeOfType<WorkspaceContentToolHarness>().Subject;

        builder._workspaceStore.Should().NotBeSameAs(registeredWorkspaceStore);
        agentRepository.Workspace.Should().BeSameAs(sessionRepository.Workspace);
        builder._workspaceStore.Should().BeSameAs(agentRepository.Workspace);
        GetToolHarnessWorkspace(toolHarness).Should().BeSameAs(agentRepository.Workspace);
    }

    [Fact]
    public void WithJsonWorkspace_AfterExplicitWorkspaceStore_PreservesCustomWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-workspace-{Guid.NewGuid():N}");
        var customWorkspace = new InMemoryWorkspaceStore();
        var builder = CreateBuilder()
            .WithWorkspaceStore(customWorkspace);

        builder.WithJsonWorkspace(path);

        builder._workspaceStore.Should().BeSameAs(customWorkspace);
        var agentRepository = builder.Config.AgentRepository.Should()
            .BeOfType<WorkspaceAgentRepository>().Subject;
        var sessionRepository = builder.Config.SessionRepository.Should()
            .BeOfType<WorkspaceSessionRepository>().Subject;
        agentRepository.Workspace.Should().BeSameAs(sessionRepository.Workspace);
    }

    [Fact]
    public async Task BuildAsync_WithWorkspaceSessionRepository_DefaultWorkspaceUsesSameWorkspace()
    {
        var workspace = new InMemoryWorkspaceStore();
        var sessionRepository = new WorkspaceSessionRepository(workspace);
        var builder = CreateBuilder()
            .WithSessionRepository(sessionRepository);

        await builder.BuildAsync();

        builder._workspaceStore.Should().BeSameAs(workspace);

        var session = new HPD.Agent.Session("session-1");
        await sessionRepository.SaveSessionAsync(session);
        var sessionSpace = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery { Kind = WorkspaceSessionRepository.SessionKind, ExternalId = session.Id });
        sessionSpace.Should().NotBeNull();
        var branchSpace = await workspace.CreateChildSpaceAsync(
            WorkspacePrincipalRef.System,
            sessionSpace!.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = "main",
                Name = "main",
                Slug = "main"
            });

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("upload"));
        await workspace.WriteContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace!.Id,
            existingAttachmentId: null,
            stream,
            new WriteWorkspaceSpaceContentRequest
            {
                Name = "upload.txt",
                ContentType = "text/plain",
                Role = WorkspaceContentRoles.Upload,
                PathHint = WorkspaceContentPaths.BranchUploads(session.Id, "main")
            });

        var uploads = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace!.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceContentRoles.Upload });
        uploads.Should().ContainSingle(attachment => attachment.Name == "upload.txt");
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
                .WithJsonWorkspace(path, persistOnBuild: false)
                .WithName("Workspace Transient Agent")
                .BuildAsync();

            File.Exists(Path.Combine(path, "workspace.json")).Should().BeFalse();
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
    public void WithAgentRepository_Throws_WhenRepositoryIsNull()
    {
        var builder = CreateBuilder();

        var plainAct = () => builder.WithAgentRepository(null!);
        var boolAct = () => builder.WithAgentRepository(null!, persistOnBuild: true);
        var optionsAct = () => builder.WithAgentRepository(null!, options => options.PersistOnBuild = true);

        plainAct.Should().Throw<ArgumentNullException>();
        boolAct.Should().Throw<ArgumentNullException>();
        optionsAct.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithAgentRepository_Throws_WhenOptionsCallbackIsNull()
    {
        var builder = CreateBuilder();
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());

        var act = () => builder.WithAgentRepository(repository, configure: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithJsonWorkspace_Throws_WhenStoragePathIsNullOrWhitespace()
    {
        var builder = CreateBuilder();

        var nullAct = () => builder.WithJsonWorkspace((string)null!);
        var emptyAct = () => builder.WithJsonWorkspace(" ");

        nullAct.Should().Throw<ArgumentException>();
        emptyAct.Should().Throw<ArgumentException>();
    }

    private static AgentBuilder CreateBuilder()
        => new AgentBuilder(CreateConfig(), new TestProviderRegistry(new FakeChatClient()));

    private static IWorkspaceStore GetToolHarnessWorkspace(WorkspaceContentToolHarness toolHarness)
        => (IWorkspaceStore)typeof(WorkspaceContentToolHarness)
            .GetField("_workspace", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(toolHarness)!;

    private static AgentConfig CreateConfig(string name = "HPD-Agent")
        => new()
        {
            Name = name,
            SystemInstructions = name == "HPD-Agent"
                ? "You are a helpful assistant."
                : "Stored instructions",
            Clients = new AgentClientConfig { Chat = new ClientProviderConfig {
                ProviderKey = "test",
                ModelName = "test-model"
            } }
        };

    private sealed class RecordingAgentRepository : IAgentRepository
    {
        private readonly IAgentRepository _inner = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());

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

        public Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct = default)
            => _inner.ListIdsAsync(ct);
    }
}
