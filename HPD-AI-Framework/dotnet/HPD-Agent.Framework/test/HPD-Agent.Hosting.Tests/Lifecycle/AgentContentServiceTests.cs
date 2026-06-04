using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

public class AgentContentServiceTests : IDisposable
{
    private readonly InMemoryWorkspaceStore _workspace = new();
    private readonly WorkspaceSessionRepository _repository;
    private readonly TestSessionManager _sessionManager;

    public AgentContentServiceTests()
    {
        _repository = new WorkspaceSessionRepository(_workspace);
        _sessionManager = new TestSessionManager(_repository);
    }

    public void Dispose() => _sessionManager.Dispose();

    [Fact]
    public async Task DefaultWorkspaceStore_UsesSessionRepositoryWorkspace()
    {
        var service = new AgentContentService(_sessionManager);
        var (sessionId, _) = await _sessionManager.CreateSessionAsync("content-session");

        await using var data = new MemoryStream([1, 2, 3]);
        var result = await service.UploadContentAsync(
            sessionId,
            "main",
            data,
            "upload.bin",
            "application/octet-stream");

        result.Status.Should().Be(AgentServiceStatus.Success);

        var sessionSpace = await _workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = sessionId
            });
        sessionSpace.Should().NotBeNull();

        var branchSpace = await _workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = "main",
                ParentSpaceId = sessionSpace!.Id
            });
        branchSpace.Should().NotBeNull();

        var attachments = await _workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace!.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceContentRoles.Upload });

        attachments.Should().ContainSingle(attachment => attachment.ContentId == result.Value!.ContentId);
    }

    [Fact]
    public async Task ContentOperations_UseWorkspaceUploadAttachments()
    {
        var service = new AgentContentService(_sessionManager);
        var (sessionId, _) = await _sessionManager.CreateSessionAsync("content-roundtrip");

        await using var data = new MemoryStream([4, 5, 6]);
        var upload = await service.UploadContentAsync(
            sessionId,
            "main",
            data,
            "roundtrip.bin",
            "application/octet-stream");
        upload.Status.Should().Be(AgentServiceStatus.Success);

        var list = await service.ListContentAsync(sessionId, "main");
        list.Status.Should().Be(AgentServiceStatus.Success);
        list.Value.Should().ContainSingle(item => item.ContentId == upload.Value!.ContentId);

        var download = await service.DownloadContentAsync(sessionId, "main", upload.Value!.ContentId);
        download.Status.Should().Be(AgentServiceStatus.Success);
        download.Value!.Data.Should().Equal([4, 5, 6]);
        download.Value.ContentType.Should().Be("application/octet-stream");
        download.Value.FileName.Should().Be("roundtrip.bin");

        var delete = await service.DeleteContentAsync(sessionId, "main", upload.Value.ContentId);
        delete.Status.Should().Be(AgentServiceStatus.Success);

        var afterDelete = await service.ListContentAsync(sessionId, "main");
        afterDelete.Status.Should().Be(AgentServiceStatus.Success);
        afterDelete.Value.Should().BeEmpty();
    }

    [Fact]
    public void DefaultWorkspaceStore_RejectsNonWorkspaceSessionRepository()
    {
        using var sessionManager = new TestSessionManager(new NonWorkspaceSessionRepository());

        var act = () => new AgentContentService(sessionManager);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*workspace-backed session repository*");
    }

    private sealed class TestSessionManager(ISessionRepository repository) : SessionManager(repository);

    private sealed class NonWorkspaceSessionRepository : ISessionRepository
    {
        public Task<Session?> LoadSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Session?>(null);

        public Task SaveSessionAsync(
            Session session,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListSessionIdsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task DeleteSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Branch?> LoadBranchAsync(
            string sessionId,
            string branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Branch?>(null);

        public Task<BranchEventDocument?> LoadBranchDocumentAsync(
            string sessionId,
            string branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BranchEventDocument?>(null);

        public Task SaveBranchDocumentAsync(
            BranchEventDocument document,
            long? expectedSequenceNumber = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AppendBranchEventAsync(
            string sessionId,
            string branchId,
            AgentEvent evt,
            long? expectedSequenceNumber = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async IAsyncEnumerable<AgentEvent> ReadBranchEventsAsync(
            string sessionId,
            string branchId,
            HPD.Events.ReplayReadOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<string>> ListBranchIdsAsync(
            string sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task DeleteBranchAsync(
            string sessionId,
            string branchId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> DeleteInactiveSessionsAsync(
            TimeSpan inactivityThreshold,
            bool dryRun = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
