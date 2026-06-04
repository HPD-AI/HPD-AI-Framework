using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent.Tests.Workspace;

public sealed class JsonWorkspaceStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"hpd-json-workspace-{Guid.NewGuid():N}");

    [Fact]
    public async Task WorkspaceState_RoundTripsAcrossStoreInstances()
    {
        var first = new JsonWorkspaceStore(_tempDir);
        var session = await first.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = "session-1",
                Name = "Session 1"
            });
        var branch = await first.CreateChildSpaceAsync(
            WorkspacePrincipalRef.System,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = "main",
                Name = "main"
            });
        await first.AppendEventAsync(
            WorkspacePrincipalRef.System,
            branch.Id,
            new AppendWorkspaceEventRequest
            {
                Role = WorkspaceSessionRepository.BranchEventStreamRole,
                Payload = Encoding.UTF8.GetBytes("""{"type":"created"}"""),
                ExpectedSequenceNumber = 0
            });

        var second = new JsonWorkspaceStore(_tempDir);
        var loadedSession = await second.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = "session-1"
            });
        var loadedBranches = await second.ListChildSpacesAsync(
            WorkspacePrincipalRef.System,
            loadedSession!.Id,
            new WorkspaceSpaceQuery { Kind = WorkspaceSessionRepository.BranchKind });
        var loadedBranch = Assert.Single(loadedBranches);

        var events = new List<WorkspaceEventRecord>();
        await foreach (var evt in second.ReadEventsAsync(
            WorkspacePrincipalRef.System,
            loadedBranch.Id,
            new WorkspaceEventStreamQuery { Role = WorkspaceSessionRepository.BranchEventStreamRole }))
        {
            events.Add(evt);
        }

        Assert.NotNull(loadedSession);
        Assert.Equal("session-1", loadedSession.ExternalId);
        Assert.Equal("main", loadedBranch.ExternalId);
        var loadedEvent = Assert.Single(events);
        Assert.Equal(1, loadedEvent.SequenceNumber);
        Assert.Equal("""{"type":"created"}""", Encoding.UTF8.GetString(loadedEvent.Payload.Span));
    }

    [Fact]
    public async Task ContentPayloads_AreStoredOutsideWorkspaceJson()
    {
        var store = new JsonWorkspaceStore(_tempDir);
        var project = await store.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "project",
                ExternalId = "contract-review",
                Name = "Contract Review"
            });

        await using var writeStream = new MemoryStream(Encoding.UTF8.GetBytes("hello from payload storage"));
        var attachment = await store.WriteContentAsync(
            WorkspacePrincipalRef.System,
            project.Id,
            existingAttachmentId: null,
            writeStream,
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = "text/plain",
                Role = "source_doc",
                Name = "notes.txt"
            });
        var content = await store.StatContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion);
        Assert.NotNull(content);

        var workspaceJson = await File.ReadAllTextAsync(Path.Combine(_tempDir, "workspace.json"));
        Assert.DoesNotContain("hello from payload storage", workspaceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Bytes\"", workspaceJson, StringComparison.Ordinal);
        Assert.Contains("\"StorageKey\"", workspaceJson, StringComparison.Ordinal);

        Assert.True(Directory.Exists(Path.Combine(_tempDir, "payloads")));
        Assert.True(File.Exists(Path.Combine(_tempDir, content!.StorageKey)));

        var reopened = new JsonWorkspaceStore(_tempDir);
        await using var readStream = await reopened.OpenContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion);
        Assert.NotNull(readStream);
        using var reader = new StreamReader(readStream);
        Assert.Equal("hello from payload storage", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ContentWrite_RemovesPendingReservationAfterCommit()
    {
        var store = new JsonWorkspaceStore(_tempDir);
        var project = await store.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "project",
                ExternalId = "contract-review",
                Name = "Contract Review"
            });

        await using var writeStream = new MemoryStream(Encoding.UTF8.GetBytes("committed"));
        await store.WriteContentAsync(
            WorkspacePrincipalRef.System,
            project.Id,
            existingAttachmentId: null,
            writeStream,
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = "text/plain",
                Role = "source_doc",
                Name = "notes.txt"
            });

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_tempDir, "workspace.json")));
        var pending = document.RootElement.GetProperty("PendingContentWrites");
        Assert.Empty(pending.EnumerateObject());
    }

    [Fact]
    public async Task ContentWrite_FailedPayloadWriteLeavesAbortedReservation()
    {
        var contentObjects = new ThrowingWorkspaceContentObjects();
        var store = new JsonWorkspaceStore(_tempDir, contentObjects: contentObjects);
        var project = await store.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "project",
                ExternalId = "contract-review",
                Name = "Contract Review"
            });

        await using var writeStream = new MemoryStream(Encoding.UTF8.GetBytes("blocked"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.WriteContentAsync(
                WorkspacePrincipalRef.System,
                project.Id,
                existingAttachmentId: null,
                writeStream,
                new WriteWorkspaceSpaceContentRequest
                {
                    ContentType = "text/plain",
                    Role = "source_doc",
                    Name = "notes.txt"
                }));

        var workspaceJson = await File.ReadAllTextAsync(Path.Combine(_tempDir, "workspace.json"));
        Assert.Contains("\"PendingContentWrites\"", workspaceJson, StringComparison.Ordinal);
        Assert.Contains("\"Status\": \"aborted\"", workspaceJson, StringComparison.Ordinal);
        Assert.Contains("payload blocked", workspaceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupPendingContentWrites_RemovesAbortedReservations()
    {
        var contentObjects = new ThrowingWorkspaceContentObjects();
        var store = new JsonWorkspaceStore(_tempDir, contentObjects: contentObjects);
        var project = await store.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "project",
                ExternalId = "contract-review",
                Name = "Contract Review"
            });

        await using var writeStream = new MemoryStream(Encoding.UTF8.GetBytes("blocked"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.WriteContentAsync(
                WorkspacePrincipalRef.System,
                project.Id,
                existingAttachmentId: null,
                writeStream,
                new WriteWorkspaceSpaceContentRequest
                {
                    ContentType = "text/plain",
                    Role = "source_doc",
                    Name = "notes.txt"
                }));

        var cleanup = await store.CleanupPendingContentWritesAsync(WorkspacePrincipalRef.System);

        Assert.Equal(1, cleanup.MatchedWrites);
        Assert.Equal(1, cleanup.DeletedVersions);
        Assert.Equal(1, cleanup.RemovedRecords);
        Assert.Equal(0, cleanup.FailedDeletes);
        Assert.Equal(2, contentObjects.DeleteVersionCalls);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_tempDir, "workspace.json")));
        var pending = document.RootElement.GetProperty("PendingContentWrites");
        Assert.Empty(pending.EnumerateObject());
    }

    [Fact]
    public async Task AppendEvent_CreatesWorkspaceEventStreamMetadata()
    {
        var store = new JsonWorkspaceStore(_tempDir);
        var session = await store.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = "session-1",
                Name = "Session 1"
            });
        var branch = await store.CreateChildSpaceAsync(
            WorkspacePrincipalRef.System,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = "main",
                Name = "main"
            });

        await store.AppendEventAsync(
            WorkspacePrincipalRef.System,
            branch.Id,
            new AppendWorkspaceEventRequest
            {
                Role = WorkspaceSessionRepository.BranchEventStreamRole,
                Payload = Encoding.UTF8.GetBytes("""{"type":"created"}"""),
                ExpectedSequenceNumber = 0
            });

        var workspaceJson = await File.ReadAllTextAsync(Path.Combine(_tempDir, "workspace.json"));
        Assert.Contains("\"EventStreams\"", workspaceJson, StringComparison.Ordinal);
        Assert.Contains(WorkspaceSessionRepository.BranchEventStreamRole, workspaceJson, StringComparison.Ordinal);
        Assert.Contains("\"LatestSequenceNumber\": 1", workspaceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepairEventStreamMetadata_UpdatesLatestSequenceFromBackendStat()
    {
        var store = new JsonWorkspaceStore(_tempDir);
        var session = await store.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = "session-1",
                Name = "Session 1"
            });
        var branch = await store.CreateChildSpaceAsync(
            WorkspacePrincipalRef.System,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = "main",
                Name = "main"
            });

        await store.AppendEventAsync(
            WorkspacePrincipalRef.System,
            branch.Id,
            new AppendWorkspaceEventRequest
            {
                Role = WorkspaceSessionRepository.BranchEventStreamRole,
                Payload = Encoding.UTF8.GetBytes("""{"type":"created"}"""),
                ExpectedSequenceNumber = 0
            });

        var workspacePath = Path.Combine(_tempDir, "workspace.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(workspacePath))!;
        var eventStreams = json["EventStreams"]!.AsObject();
        var stream = eventStreams.First().Value!;
        stream["LatestSequenceNumber"] = 0;
        await File.WriteAllTextAsync(workspacePath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var repairedStore = new JsonWorkspaceStore(_tempDir);
        var repair = await repairedStore.RepairEventStreamMetadataAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceEventStreamRepairRequest
            {
                SpaceId = branch.Id,
                Role = WorkspaceSessionRepository.BranchEventStreamRole
            });

        Assert.Equal(1, repair.MatchedStreams);
        Assert.Equal(1, repair.RepairedStreams);
        Assert.Equal(0, repair.MissingBackendStreams);

        using var repairedDocument = JsonDocument.Parse(await File.ReadAllTextAsync(workspacePath));
        var repairedStream = Assert.Single(repairedDocument.RootElement.GetProperty("EventStreams").EnumerateObject());
        Assert.Equal(1, repairedStream.Value.GetProperty("LatestSequenceNumber").GetInt64());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class ThrowingWorkspaceContentObjects : IWorkspaceContentObjects
    {
        public int DeleteVersionCalls { get; private set; }

        public Task<WorkspaceContentObjectWriteResult> WriteAsync(
            string contentId,
            string version,
            Stream data,
            WorkspaceContentObjectWriteRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("payload blocked");

        public Task<Stream?> OpenReadAsync(
            string contentId,
            string? version = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(null);

        public Task<WorkspaceContentObjectStat?> StatAsync(
            string contentId,
            string? version = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<WorkspaceContentObjectStat?>(null);

        public Task<Uri?> CreateReadUriAsync(
            string contentId,
            string? version,
            TimeSpan expiresIn,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Uri?>(null);

        public Task DeleteAsync(
            string contentId,
            string? ifMatchVersion = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteVersionAsync(
            string contentId,
            string version,
            CancellationToken cancellationToken = default)
        {
            DeleteVersionCalls++;
            return Task.CompletedTask;
        }
    }
}
