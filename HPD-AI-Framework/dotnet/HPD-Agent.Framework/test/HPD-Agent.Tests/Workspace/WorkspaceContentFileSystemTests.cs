using System.Text;
using HPD.Agent;

namespace HPD.Agent.Tests.Workspace;

public sealed class WorkspaceContentFileSystemTests
{
    private static WorkspacePrincipalRef Principal => WorkspacePrincipalRef.System;

    [Fact]
    public async Task ListAsync_RootShowsWorkspaceKinds()
    {
        var fileSystem = new WorkspaceContentFileSystem(new InMemoryWorkspaceStore());

        var entries = await fileSystem.ListAsync(Principal, "/");

        Assert.Contains(entries, entry => entry.Path == "/agents");
        Assert.Contains(entries, entry => entry.Path == "/projects");
        Assert.Contains(entries, entry => entry.Path == "/sessions");
    }

    [Fact]
    public async Task ListAsync_ProjectShowsAttachedContent()
    {
        var store = new InMemoryWorkspaceStore();
        var fileSystem = new WorkspaceContentFileSystem(store);
        var project = await CreateSpaceAsync(store, "project", "contract-review", "Contract Review");
        var content = await WriteTextAsync(
            store,
            project.Id,
            "contract",
            "contract.pdf",
            "source_doc",
            "application/pdf");

        var entries = await fileSystem.ListAsync(Principal, "/projects/contract-review");

        var entry = Assert.Single(entries, item => item.Name == "contract.pdf");
        Assert.Equal(WorkspaceContentPathKind.Content, entry.Kind);
        Assert.Equal(content.ContentId, entry.Attachment!.ContentId);
        Assert.Equal("/projects/contract-review/contract.pdf", entry.Path);
    }

    [Fact]
    public async Task ReadTextAsync_ResolvesBranchUploadRoleDirectory()
    {
        var store = new InMemoryWorkspaceStore();
        var fileSystem = new WorkspaceContentFileSystem(store);
        var session = await CreateSpaceAsync(store, "session", "session-1", "Session 1");
        var branch = await store.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest { Kind = "branch", ExternalId = "main", Name = "main", Slug = "main" });
        await WriteTextAsync(
            store,
            branch.Id,
            "uploaded text",
            "notes.txt",
            WorkspaceContentRoles.Upload);

        var text = await fileSystem.ReadTextAsync(Principal, "/sessions/session-1/branches/main/uploads/notes.txt");

        Assert.Equal("uploaded text", text);
    }

    [Fact]
    public async Task ReadTextAsync_ResolvesBranchChildSpaceContent()
    {
        var store = new InMemoryWorkspaceStore();
        var fileSystem = new WorkspaceContentFileSystem(store);
        var session = await CreateSpaceAsync(store, "session", "session-1", "Session 1");
        var branch = await store.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "branch",
                ExternalId = "main",
                Name = "main"
            });
        await store.AppendEventAsync(
            Principal,
            branch.Id,
            new AppendWorkspaceEventRequest
            {
                Role = "branch_event_stream",
                Payload = Encoding.UTF8.GetBytes("""{"type":"message"}"""),
                ExpectedSequenceNumber = 0
            });

        var text = await fileSystem.ReadTextAsync(Principal, "/sessions/session-1/branches/main/events.jsonl");

        Assert.Equal("""{"type":"message"}""" + "\n", text);
    }

    [Fact]
    public async Task WriteTextAsync_CreatesAndReplacesProjectContentByPath()
    {
        var store = new InMemoryWorkspaceStore();
        var fileSystem = new WorkspaceContentFileSystem(store);
        await CreateSpaceAsync(store, "project", "contract-review", "Contract Review");

        var created = await fileSystem.WriteTextAsync(
            Principal,
            "/projects/contract-review/summary.md",
            "v1");
        var updated = await fileSystem.WriteTextAsync(
            Principal,
            "/projects/contract-review/summary.md",
            "v2");

        Assert.Equal("/projects/contract-review/summary.md", created.Path);
        Assert.Equal("/projects/contract-review/summary.md", updated.Path);
        Assert.Equal(created.Attachment!.ContentId, updated.Attachment!.ContentId);
        Assert.NotEqual(created.Attachment.ContentVersion, updated.Attachment.ContentVersion);
        Assert.Equal("v2", await fileSystem.ReadTextAsync(Principal, "/projects/contract-review/summary.md"));

        var entries = await fileSystem.ListAsync(Principal, "/projects/contract-review");
        Assert.Single(entries.Where(entry => entry.Name == "summary.md"));
    }

    [Fact]
    public async Task ResolveAsync_DetectsAmbiguousSpaceNames()
    {
        var store = new InMemoryWorkspaceStore();
        var fileSystem = new WorkspaceContentFileSystem(store);
        await CreateSpaceAsync(store, "project", "project-1", "Project");
        await CreateSpaceAsync(store, "project", "project-2", "Project");

        await Assert.ThrowsAsync<WorkspacePathAmbiguousException>(() =>
            fileSystem.ResolveAsync(Principal, "/projects/Project"));
    }

    private static Task<WorkspaceSpaceInfo> CreateSpaceAsync(
        IWorkspaceStore store,
        string kind,
        string externalId,
        string name) =>
        store.CreateSpaceAsync(
            Principal,
            new CreateWorkspaceSpaceRequest
            {
                Kind = kind,
                ExternalId = externalId,
                Name = name
            });

    private static Task<WorkspaceContentAttachmentInfo> WriteTextAsync(
        IWorkspaceStore store,
        string spaceId,
        string text,
        string name,
        string role,
        string contentType = "text/plain")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return store.WriteContentAsync(
            Principal,
            spaceId,
            existingAttachmentId: null,
            new MemoryStream(bytes),
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = contentType,
                Role = role,
                Name = name
            });
    }
}
