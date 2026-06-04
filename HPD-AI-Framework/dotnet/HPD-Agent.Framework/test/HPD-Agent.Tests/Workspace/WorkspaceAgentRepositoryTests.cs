namespace HPD.Agent.Tests.Workspace;

public sealed class WorkspaceAgentRepositoryTests
{
    [Fact]
    public async Task SaveAgent_CreatesAgentSpaceAndDefinitionDocument()
    {
        var workspace = new InMemoryWorkspaceStore();
        var repository = new WorkspaceAgentRepository(workspace);
        var agent = MakeAgent("agent-1", "Researcher");

        await repository.SaveAsync(agent);

        var space = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceAgentRepository.AgentKind,
                ExternalId = "agent-1"
            });
        Assert.NotNull(space);
        Assert.Equal("Researcher", space.Name);

        var definitions = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            space.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceAgentRepository.AgentDefinitionRole });
        var definition = Assert.Single(definitions);
        Assert.Equal("definition.json", definition.Name);

        var loaded = await repository.LoadAsync("agent-1");
        Assert.NotNull(loaded);
        Assert.Equal("agent-1", loaded.Id);
        Assert.Equal("Researcher", loaded.Name);
        Assert.Equal("You research.", loaded.Config.SystemInstructions);
    }

    [Fact]
    public async Task SaveAgent_ReplacesDefinitionDocumentForSameAgentRole()
    {
        var workspace = new InMemoryWorkspaceStore();
        var repository = new WorkspaceAgentRepository(workspace);

        await repository.SaveAsync(MakeAgent("agent-1", "Original"));
        await repository.SaveAsync(MakeAgent("agent-1", "Updated"));

        var space = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceAgentRepository.AgentKind,
                ExternalId = "agent-1"
            });
        Assert.NotNull(space);

        var definitions = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            space.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceAgentRepository.AgentDefinitionRole });
        Assert.Single(definitions);

        var loaded = await repository.LoadAsync("agent-1");
        Assert.NotNull(loaded);
        Assert.Equal("Updated", loaded.Name);
    }

    [Fact]
    public async Task LoadAgent_ReturnsNullWhenAgentSpaceOrDefinitionIsMissing()
    {
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());

        var loaded = await repository.LoadAsync("missing");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadAgent_NormalizesMetadataJsonElementsToClrValues()
    {
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());
        var agent = MakeAgent("agent-1", "Researcher");
        agent.Metadata = new Dictionary<string, object>
        {
            ["owner"] = "platform",
            ["revision"] = 3,
            ["enabled"] = true,
            ["tags"] = new[] { "research", "internal" },
            ["routing"] = new Dictionary<string, object>
            {
                ["lane"] = "workspace"
            }
        };

        await repository.SaveAsync(agent);

        var loaded = await repository.LoadAsync(agent.Id);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Metadata);
        Assert.Equal("platform", loaded.Metadata["owner"]);
        Assert.Equal(3, loaded.Metadata["revision"]);
        Assert.Equal(true, loaded.Metadata["enabled"]);

        var tags = Assert.IsAssignableFrom<IEnumerable<string>>(loaded.Metadata["tags"]);
        Assert.Equal(["research", "internal"], tags);

        var routing = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(loaded.Metadata["routing"]);
        Assert.Equal("workspace", routing["lane"]);
    }

    [Fact]
    public async Task ListIds_ReturnsAgentSpaceExternalIds()
    {
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());

        await repository.SaveAsync(MakeAgent("agent-b", "Agent B"));
        await repository.SaveAsync(MakeAgent("agent-a", "Agent A"));

        var ids = await repository.ListIdsAsync();

        Assert.Equal(["agent-a", "agent-b"], ids);
    }

    [Fact]
    public async Task DeleteAgent_RemovesAgentSpaceAndDefinition()
    {
        var repository = new WorkspaceAgentRepository(new InMemoryWorkspaceStore());

        await repository.SaveAsync(MakeAgent("agent-1", "Researcher"));
        await repository.DeleteAsync("agent-1");

        Assert.Null(await repository.LoadAsync("agent-1"));
        Assert.Empty(await repository.ListIdsAsync());
    }

    private static StoredAgent MakeAgent(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Config = new AgentConfig
            {
                Name = name,
                SystemInstructions = "You research."
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
}
