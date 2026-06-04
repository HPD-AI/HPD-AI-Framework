using System.Text;

namespace HPD.Agent.Tests.Workspace;

public sealed class WorkspaceContentQueryTests
{
    private static WorkspacePrincipalRef System => WorkspacePrincipalRef.System;
    private static WorkspacePrincipalRef User1 => new("user", "user-1");
    private static WorkspacePrincipalRef User2 => new("user", "user-2");

    [Theory]
    [InlineData("memory")]
    [InlineData("json")]
    public async Task SearchContentAsync_ReturnsSpaceAndAttachmentContext(string storeKind)
    {
        var store = CreateStore(storeKind);
        var project = await CreateSpaceAsync(store, System, "project", "contract-review", "Contract Review");
        var skill = await CreateSpaceAsync(store, System, "skill", "citation", "Citation Skill");
        var content = await WriteTextAsync(store, System, project.Id, "shared", "summary.md", "draft");
        await AttachAsync(store, System, skill.Id, content, "reference", "example.md");

        var results = await store.SearchContentAsync(
            System,
            new WorkspaceVisibleContentQuery
            {
                TraversalMode = WorkspaceContentTraversalMode.AccessibleGraph,
                ContentType = "text/plain"
            });

        Assert.Equal(2, results.Count(result => result.ContentId == content.ContentId));
        Assert.Contains(results, result =>
            result.SpaceId == project.Id &&
            result.SpaceKind == "project" &&
            result.Name == "summary.md" &&
            result.Role == "draft");
        Assert.Contains(results, result =>
            result.SpaceId == skill.Id &&
            result.SpaceKind == "skill" &&
            result.Name == "example.md" &&
            result.Role == "reference");
    }

    [Fact]
    public async Task SearchContentAsync_SpaceDescendantsIncludesVisibleChildSpaces()
    {
        var store = new InMemoryWorkspaceStore();
        var session = await CreateSpaceAsync(store, System, "session", "session-1", "Session 1");
        var branch = await store.CreateChildSpaceAsync(
            System,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "branch",
                ExternalId = "main",
                Name = "main"
            });
        var content = await WriteTextAsync(store, System, branch.Id, "analysis", "analysis.md", WorkspaceContentRoles.Artifact);

        var spaceOnly = await store.SearchContentAsync(
            System,
            new WorkspaceVisibleContentQuery
            {
                SpaceId = session.Id,
                TraversalMode = WorkspaceContentTraversalMode.SpaceOnly
            });
        var descendants = await store.SearchContentAsync(
            System,
            new WorkspaceVisibleContentQuery
            {
                SpaceId = session.Id,
                TraversalMode = WorkspaceContentTraversalMode.SpaceDescendants
            });

        Assert.Empty(spaceOnly);
        var result = Assert.Single(descendants);
        Assert.Equal(branch.Id, result.SpaceId);
        Assert.Equal(content.ContentId, result.ContentId);
    }

    [Fact]
    public async Task SearchContentAsync_FiltersByPrincipalAccess()
    {
        var store = new InMemoryWorkspaceStore();
        var visible = await CreateSpaceAsync(store, User1, "project", "visible", "Visible");
        var hidden = await CreateSpaceAsync(store, System, "project", "hidden", "Hidden");
        await WriteTextAsync(store, User1, visible.Id, "yes", "visible.md", WorkspaceContentRoles.Content);
        await WriteTextAsync(store, System, hidden.Id, "no", "hidden.md", WorkspaceContentRoles.Content);

        var user1Results = await store.SearchContentAsync(User1, new WorkspaceVisibleContentQuery());
        var user2Results = await store.SearchContentAsync(User2, new WorkspaceVisibleContentQuery());

        var result = Assert.Single(user1Results);
        Assert.Equal("visible.md", result.Name);
        Assert.Empty(user2Results);
    }

    private static IWorkspaceStore CreateStore(string storeKind)
    {
        if (storeKind == "json")
        {
            var path = Path.Combine(Path.GetTempPath(), "hpd-workspace-query-tests", Guid.NewGuid().ToString("N"));
            return new JsonWorkspaceStore(path);
        }

        return new InMemoryWorkspaceStore();
    }

    private static Task<WorkspaceSpaceInfo> CreateSpaceAsync(
        IWorkspaceStore store,
        WorkspacePrincipalRef principal,
        string kind,
        string externalId,
        string name) =>
        store.CreateSpaceAsync(
            principal,
            new CreateWorkspaceSpaceRequest
            {
                Kind = kind,
                ExternalId = externalId,
                Name = name,
                Slug = externalId
            });

    private static Task<WorkspaceContentAttachmentInfo> WriteTextAsync(
        IWorkspaceStore store,
        WorkspacePrincipalRef principal,
        string spaceId,
        string text,
        string name,
        string role) =>
        store.WriteContentAsync(
            principal,
            spaceId,
            existingAttachmentId: null,
            new MemoryStream(Encoding.UTF8.GetBytes(text)),
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = "text/plain",
                Role = role,
                Name = name
            });

    private static Task<WorkspaceContentAttachmentInfo> AttachAsync(
        IWorkspaceStore store,
        WorkspacePrincipalRef principal,
        string spaceId,
        WorkspaceContentAttachmentInfo content,
        string role,
        string name) =>
        store.AttachContentAsync(
            principal,
            spaceId,
            content.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = role,
                Name = name,
                ContentVersion = content.ContentVersion,
                Permission = WorkspacePermissions.ReadWrite
            });
}
