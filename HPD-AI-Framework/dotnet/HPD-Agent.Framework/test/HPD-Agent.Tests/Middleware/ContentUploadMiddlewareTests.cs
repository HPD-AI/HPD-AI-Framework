using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Xunit;
using SessionModel = global::HPD.Agent.Session;

#pragma warning disable MEAI001

namespace HPD.Agent.Tests.Middleware;

public class ContentUploadMiddlewareTests
{
    [Fact]
    public async Task BeforeMessageTurnAsync_StrategyLocal_UploadsToWorkspace()
    {
        var workspace = new InMemoryWorkspaceStore();
        var middleware = new ContentUploadMiddleware(providerRegistry: null, workspace);
        var session = new SessionModel("test-session");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var message = new ChatMessage(ChatRole.User, [
            new TextContent("Analyze:"),
            new DataContent(imageBytes, "image/png") { Name = "image.png" }
        ]);
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig { UploadStrategy = UploadStrategy.Local };
        var context = CreateBeforeMessageTurnContextForResolver(session, message, capture.Coordinator, runConfig);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        var uriContent = Assert.IsType<UriContent>(context.UserMessage!.Contents[1]);
        Assert.Equal(ContentReferenceResolverMiddleware.ContentUriScheme, uriContent.Uri.Scheme);

        var branchSpace = await FindBranchSpaceAsync(workspace, session.Id, context.Branch!.Id);
        var attachment = Assert.Single(await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceContentRoles.Upload }));
        Assert.Equal(uriContent.Uri.Host, attachment.ContentId);
        Assert.Equal("image.png", attachment.Name);

        await using var stream = await workspace.OpenContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion);
        Assert.NotNull(stream);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        Assert.Equal(imageBytes, memory.ToArray());
        Assert.Single(await capture.WaitForAsync<ContentUploadedEvent>());
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_Auto_UsesHostedBeforeWorkspace()
    {
        var workspace = new InMemoryWorkspaceStore();
        var hostedClient = new FakeHostedFileClient();
        var middleware = new ContentUploadMiddleware(providerRegistry: null, workspace);
        var session = new SessionModel("test-session");
        var message = new ChatMessage(ChatRole.User, [new DataContent(new byte[] { 1 }, "image/png")]);
        using var capture = new EventCapture();
        var context = CreateBeforeMessageTurnContextForResolver(
            session,
            message,
            capture.Coordinator,
            clientSet: new AgentClientSet { HostedFiles = hostedClient });

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.IsType<HostedFileContent>(Assert.Single(context.UserMessage!.Contents));
        Assert.Single(hostedClient.Uploads);
        Assert.Empty(await workspace.ListSpacesAsync(WorkspacePrincipalRef.System));
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_Auto_HostedFailureFallsBackToWorkspace()
    {
        var workspace = new InMemoryWorkspaceStore();
        var hostedClient = new FakeHostedFileClient { ThrowOnUpload = true };
        var middleware = new ContentUploadMiddleware(providerRegistry: null, workspace);
        var session = new SessionModel("test-session");
        var bytes = new byte[] { 9, 8, 7 };
        var message = new ChatMessage(ChatRole.User, [new DataContent(bytes, "image/png")]);
        using var capture = new EventCapture();
        var context = CreateBeforeMessageTurnContextForResolver(
            session,
            message,
            capture.Coordinator,
            clientSet: new AgentClientSet { HostedFiles = hostedClient });

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        var uriContent = Assert.IsType<UriContent>(Assert.Single(context.UserMessage!.Contents));
        var branchSpace = await FindBranchSpaceAsync(workspace, session.Id, context.Branch!.Id);
        var attachment = Assert.Single(await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceContentRoles.Upload }));
        Assert.Equal(uriContent.Uri.Host, attachment.ContentId);
        Assert.Single(await capture.WaitForAsync<HostedFileUploadFailedEvent>());
        Assert.Single(await capture.WaitForAsync<ContentUploadedEvent>());
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_StrategyLocal_NoWorkspace_KeepsOriginal()
    {
        var middleware = new ContentUploadMiddleware(providerRegistry: null, workspaceStore: null);
        var session = new SessionModel("test-session");
        var data = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var message = new ChatMessage(ChatRole.User, [data]);
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig { UploadStrategy = UploadStrategy.Local };
        var context = CreateBeforeMessageTurnContextForResolver(session, message, capture.Coordinator, runConfig);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Same(data, Assert.Single(context.UserMessage!.Contents));
        Assert.Empty(capture.Events);
    }

    internal static BeforeMessageTurnContext CreateBeforeMessageTurnContextForResolver(
        SessionModel session,
        ChatMessage userMessage,
        EventCoordinator coordinator,
        AgentRunConfig? runConfig = null,
        AgentClientSet? clientSet = null)
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            session.Id,
            "TestAgent");

        var context = new AgentContext(
            "TestAgent",
            session.Id,
            state,
            coordinator,
            session,
            new Branch(session.Id),
            CancellationToken.None,
            clientSet: clientSet);

        return context.AsBeforeMessageTurn(userMessage, new List<ChatMessage>(), runConfig ?? new AgentRunConfig());
    }

    private static async Task<WorkspaceSpaceInfo> FindSessionSpaceAsync(IWorkspaceStore workspace, string sessionId)
    {
        var sessionSpace = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = sessionId
            });
        Assert.NotNull(sessionSpace);
        return sessionSpace;
    }

    private static async Task<WorkspaceSpaceInfo> FindBranchSpaceAsync(
        IWorkspaceStore workspace,
        string sessionId,
        string branchId)
    {
        var sessionSpace = await FindSessionSpaceAsync(workspace, sessionId);
        var branchSpace = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = branchId,
                ParentSpaceId = sessionSpace.Id
            });
        Assert.NotNull(branchSpace);
        return branchSpace;
    }

    private sealed class FakeHostedFileClient : IHostedFileClient
    {
        public List<(string? MediaType, string? FileName, HostedFileClientOptions? Options, byte[] Data)> Uploads { get; } = new();

        public bool ThrowOnUpload { get; init; }

        public Task<HostedFileContent> UploadAsync(
            Stream content,
            string? mediaType = null,
            string? fileName = null,
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnUpload)
                throw new InvalidOperationException("upload failed");

            using var memory = new MemoryStream();
            content.CopyTo(memory);
            var data = memory.ToArray();
            Uploads.Add((mediaType, fileName, options, data));

            return Task.FromResult(new HostedFileContent($"file-{Uploads.Count}")
            {
                MediaType = mediaType,
                Name = fileName,
                SizeInBytes = data.Length,
                Purpose = options?.Purpose
            });
        }

        public Task<HostedFileDownloadStream> DownloadAsync(
            string fileId,
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<HostedFileContent?> GetFileInfoAsync(
            string fileId,
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<HostedFileContent?>(null);

        public IAsyncEnumerable<HostedFileContent> ListFilesAsync(
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
            => EmptyFiles();

        public Task<bool> DeleteAsync(
            string fileId,
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }

        private static async IAsyncEnumerable<HostedFileContent> EmptyFiles()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}

public class ContentReferenceResolverMiddlewareTests
{
    [Fact]
    public async Task BeforeIterationAsync_WithContentReference_ResolvesToDataContent()
    {
        var workspace = new InMemoryWorkspaceStore();
        var middleware = new ContentReferenceResolverMiddleware(workspace);
        var session = new SessionModel("test-session");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var sessionSpace = await workspace.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = session.Id,
                Name = session.Id
            });
        var attachment = await workspace.WriteContentAsync(
            WorkspacePrincipalRef.System,
            sessionSpace.Id,
            existingAttachmentId: null,
            new MemoryStream(imageBytes, writable: false),
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = "image/png",
                Role = WorkspaceContentRoles.Upload,
                Name = "image.png"
            });
        var contentUri = new UriContent(ContentReferenceResolverMiddleware.CreateContentUri(attachment.ContentId), "image/png");
        var message = new ChatMessage(ChatRole.User, [new TextContent("Analyze:"), contentUri]);
        using var capture = new EventCapture();
        var context = CreateBeforeIterationContext(session, message, capture.Coordinator);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        var resolved = Assert.IsType<DataContent>(context.Messages[0].Contents[1]);
        Assert.Equal(imageBytes, resolved.Data.ToArray());
        Assert.Equal("image/png", resolved.MediaType);
        Assert.Equal(attachment.ContentId, (await capture.WaitForAsync<ContentReferenceResolvedEvent>()).Single().ContentUri.Host);
    }

    [Fact]
    public async Task BeforeIterationAsync_NoWorkspace_DoesNothing()
    {
        var middleware = new ContentReferenceResolverMiddleware(workspaceStore: null);
        var session = new SessionModel("test-session");
        var contentUri = new UriContent(new Uri("hpd-content://any-id"), "image/png");
        var message = new ChatMessage(ChatRole.User, [contentUri]);
        using var capture = new EventCapture();
        var context = CreateBeforeIterationContext(session, message, capture.Coordinator);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        Assert.Same(contentUri, Assert.Single(context.Messages[0].Contents));
        Assert.Empty(capture.Events);
    }

    [Fact]
    public async Task RoundTrip_Upload_Then_Resolve()
    {
        var workspace = new InMemoryWorkspaceStore();
        var uploadMiddleware = new ContentUploadMiddleware(providerRegistry: null, workspace);
        var resolveMiddleware = new ContentReferenceResolverMiddleware(workspace);
        var session = new SessionModel("test-session");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var originalMessage = new ChatMessage(ChatRole.User, [new DataContent(imageBytes, "image/png")]);
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig { UploadStrategy = UploadStrategy.Local };
        var context = ContentUploadMiddlewareTests.CreateBeforeMessageTurnContextForResolver(
            session,
            originalMessage,
            capture.Coordinator,
            runConfig);

        await uploadMiddleware.BeforeMessageTurnAsync(context, CancellationToken.None);
        var uploadedMessage = context.UserMessage!;
        Assert.IsType<UriContent>(uploadedMessage.Contents[0]);

        var iterationContext = CreateBeforeIterationContext(session, uploadedMessage, capture.Coordinator, runConfig);
        await resolveMiddleware.BeforeIterationAsync(iterationContext, CancellationToken.None);

        var resolved = Assert.IsType<DataContent>(iterationContext.Messages[0].Contents[0]);
        Assert.Equal(imageBytes, resolved.Data.ToArray());
        Assert.Equal("image/png", resolved.MediaType);
    }

    private static BeforeIterationContext CreateBeforeIterationContext(
        SessionModel session,
        ChatMessage userMessage,
        EventCoordinator coordinator,
        AgentRunConfig? runConfig = null)
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            session.Id,
            "TestAgent");

        var context = new AgentContext(
            "TestAgent",
            session.Id,
            state,
            coordinator,
            session,
            new Branch(session.Id),
            CancellationToken.None);

        return context.AsBeforeIteration(
            0,
            new List<ChatMessage> { userMessage },
            new ChatOptions(),
            runConfig ?? new AgentRunConfig());
    }
}

internal sealed class EventCapture : IDisposable
{
    private readonly object _gate = new();
    private readonly IDisposable _subscription;

    public EventCapture()
    {
        Coordinator = new EventCoordinator();
        _subscription = Coordinator.SubscribeAny(evt =>
        {
            if (evt is AgentEvent agentEvent)
            {
                lock (_gate)
                {
                    Events.Add(agentEvent);
                }
            }

            return ValueTask.CompletedTask;
        });
    }

    public EventCoordinator Coordinator { get; }

    public List<AgentEvent> Events { get; } = new();

    public async Task<List<TEvent>> WaitForAsync<TEvent>(int count = 1)
        where TEvent : AgentEvent
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            lock (_gate)
            {
                var matching = Events.OfType<TEvent>().ToList();
                if (matching.Count >= count)
                    return matching;
            }

            await Task.Delay(10);
        }

        lock (_gate)
        {
            return Events.OfType<TEvent>().ToList();
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        Coordinator.Dispose();
    }
}
