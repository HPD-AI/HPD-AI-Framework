using System.Text;

namespace HPD.Agent.Tests.Content;

public sealed class WorkspaceContentToolHarnessTests
{
    private static WorkspacePrincipalRef Principal => WorkspacePrincipalRef.System;

    private static (InMemoryWorkspaceStore Workspace, WorkspaceContentToolHarness Harness) CreateHarness()
    {
        var workspace = new InMemoryWorkspaceStore();
        var harness = new WorkspaceContentToolHarness(workspace);
        return (workspace, harness);
    }

    [Fact]
    public async Task ListAsync_Root_ShowsWorkspaceRoots()
    {
        var (_, harness) = CreateHarness();

        var result = await harness.ListAsync("/");

        Assert.Contains("agents/", result);
        Assert.Contains("projects/", result);
        Assert.Contains("sessions/", result);
        Assert.Contains("workspaces/", result);
        Assert.DoesNotContain("role=skill", result);
        Assert.DoesNotContain("folder", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ReturnsContentVisibleThroughProjectSpace()
    {
        var (workspace, harness) = CreateHarness();
        var project = await CreateSpaceAsync(workspace, "project", "contract-review", "Contract Review");
        await WriteTextAsync(
            workspace,
            project.Id,
            "# Summary\nLine 2",
            "summary.md",
            WorkspaceContentRoles.Content);

        var result = await harness.ReadAsync("/projects/contract-review/summary.md");

        Assert.Contains("# Summary", result);
        Assert.DoesNotContain("Error", result);
    }

    [Fact]
    public async Task WriteAsync_CreatesAndReplacesProjectContent()
    {
        var (workspace, harness) = CreateHarness();
        await CreateSpaceAsync(workspace, "project", "contract-review", "Contract Review");

        var created = await harness.WriteAsync("/projects/contract-review/report.md", "v1");
        var updated = await harness.WriteAsync("/projects/contract-review/report.md", "v2");

        Assert.Contains("Written: /projects/contract-review/report.md", created);
        Assert.Contains("Written: /projects/contract-review/report.md", updated);
        Assert.Equal("v2", await harness.ReadAsync("/projects/contract-review/report.md"));

        var project = await workspace.FindSpaceAsync(
            Principal,
            new WorkspaceSpaceQuery { Kind = "project", ExternalId = "contract-review" });
        var attachments = await workspace.ListContentAsync(Principal, project!.Id);
        Assert.Single(attachments.Where(attachment => attachment.Name == "report.md"));
    }

    [Fact]
    public async Task DetachAsync_RemovesAttachmentFromVisiblePath()
    {
        var (workspace, harness) = CreateHarness();
        var session = await CreateSpaceAsync(workspace, "session", "session-1", "Session 1");
        var branch = await workspace.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest { Kind = "branch", ExternalId = "main", Name = "main", Slug = "main" });
        await WriteTextAsync(
            workspace,
            branch.Id,
            "old",
            "old.md",
            WorkspaceContentRoles.Artifact);

        var result = await harness.DetachAsync("/sessions/session-1/branches/main/artifacts/old.md");

        Assert.Contains("Detached", result);
        Assert.Empty(await workspace.ListContentAsync(
            Principal,
            branch.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceContentRoles.Artifact }));
    }

    [Fact]
    public async Task FindAsync_SearchesWorkspaceTree()
    {
        var (workspace, harness) = CreateHarness();
        var project = await CreateSpaceAsync(workspace, "project", "contract-review", "Contract Review");
        await WriteTextAsync(
            workspace,
            project.Id,
            "notes",
            "research-notes.md",
            WorkspaceContentRoles.Content);

        var result = await harness.FindAsync("*.md", "/projects");

        Assert.Contains("/projects/contract-review/research-notes.md", result);
    }

    [Fact]
    public async Task AttachAsync_AttachesExistingContentToDestinationSpace()
    {
        var (workspace, harness) = CreateHarness();
        var project = await CreateSpaceAsync(workspace, "project", "contract-review", "Contract Review");
        var source = await WriteTextAsync(
            workspace,
            project.Id,
            "source",
            "source.txt",
            WorkspaceContentRoles.Content);
        var skill = await CreateSpaceAsync(workspace, "skill", "citation", "Citation");

        var result = await harness.AttachAsync("/agents", source.ContentId);

        Assert.Contains("Error:", result);

        result = await harness.AttachAsync("/projects/contract-review", source.ContentId, "copy.txt", "reference");

        Assert.Contains("Attached: /projects/contract-review/copy.txt", result);
        var attachments = await workspace.ListContentAsync(Principal, project.Id);
        Assert.Contains(attachments, attachment => attachment.Name == "copy.txt" && attachment.Role == "reference");
        Assert.Empty(await workspace.ListContentAsync(Principal, skill.Id));
    }

    [Fact]
    public async Task TreeAsync_ShowsRecursiveWorkspaceShape()
    {
        var (workspace, harness) = CreateHarness();
        var session = await CreateSpaceAsync(workspace, "session", "session-1", "Session 1");
        var branch = await workspace.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "branch",
                ExternalId = "main",
                Name = "main"
            });
        await workspace.AppendEventAsync(
            Principal,
            branch.Id,
            new AppendWorkspaceEventRequest
            {
                Role = "branch_event_stream",
                Payload = Encoding.UTF8.GetBytes("""{"type":"message"}"""),
                ExpectedSequenceNumber = 0
            });

        var result = await harness.TreeAsync("/sessions", depth: 4);

        Assert.Contains("session-1", result);
        Assert.Contains("branches/", result);
        Assert.Contains("events.jsonl", result);
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
                Name = name,
                Slug = externalId
            });

    private static Task<WorkspaceContentAttachmentInfo> WriteTextAsync(
        IWorkspaceStore store,
        string spaceId,
        string text,
        string name,
        string role)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return store.WriteContentAsync(
            Principal,
            spaceId,
            existingAttachmentId: null,
            new MemoryStream(bytes),
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = "text/plain",
                Role = role,
                Name = name
            });
    }
}
