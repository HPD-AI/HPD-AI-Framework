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
    public async Task BeforeMessageTurnAsync_NoDataContent_DoesNothing()
    {
        var middleware = new ContentUploadMiddleware();
        var session = new SessionModel("test-session");
        var message = new ChatMessage(ChatRole.User, "Hello");
        using var capture = new EventCapture();
        var context = CreateBeforeMessageTurnContext(session, message, capture.Coordinator);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Equal("Hello", context.UserMessage!.Text);
        Assert.Empty(capture.Events);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_StrategyLocal_WithContentStore_UploadsToStore()
    {
        var contentStore = new InMemoryContentStore();
        var middleware = new ContentUploadMiddleware(providerRegistry: null, contentStore);
        var session = new SessionModel("test-session");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var message = new ChatMessage(ChatRole.User, [
            new TextContent("Analyze:"),
            new DataContent(imageBytes, "image/png") { Name = "image.png" }
        ]);
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig { UploadStrategy = UploadStrategy.Local };
        var context = CreateBeforeMessageTurnContext(session, message, capture.Coordinator, runConfig);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Equal(2, context.UserMessage!.Contents.Count);
        Assert.IsType<TextContent>(context.UserMessage.Contents[0]);
        var uriContent = Assert.IsType<UriContent>(context.UserMessage.Contents[1]);
        Assert.Equal(ContentReferenceResolverMiddleware.ContentUriScheme, uriContent.Uri.Scheme);
        Assert.Equal("image/png", uriContent.MediaType);

        var stored = await contentStore.ReadBytesAsync(ContentStoreScopes.ForThread(session.Id, "main"), uriContent.Uri.Host);
        Assert.NotNull(stored);
        Assert.Equal(imageBytes, stored);
        Assert.Single(await capture.WaitForAsync<ContentUploadedEvent>());
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_TransformedMessage_PreservesMessageIdentity()
    {
        var contentStore = new InMemoryContentStore();
        var middleware = new ContentUploadMiddleware(providerRegistry: null, contentStore);
        var session = new SessionModel("test-session");
        var createdAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var raw = new object();
        var message = new ChatMessage(ChatRole.User, [
            new DataContent(new byte[] { 1, 2, 3 }, "application/pdf") { Name = "report.pdf" }
        ])
        {
            AuthorName = "ewoof",
            CreatedAt = createdAt,
            MessageId = "message-1",
            RawRepresentation = raw
        };
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig { UploadStrategy = UploadStrategy.Local };
        var context = CreateBeforeMessageTurnContext(session, message, capture.Coordinator, runConfig);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.NotSame(message, context.UserMessage);
        Assert.Equal("message-1", context.UserMessage!.MessageId);
        Assert.Equal(createdAt, context.UserMessage.CreatedAt);
        Assert.Equal("ewoof", context.UserMessage.AuthorName);
        Assert.Same(raw, context.UserMessage.RawRepresentation);
        Assert.IsType<UriContent>(Assert.Single(context.UserMessage.Contents));
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_StrategyHosted_UsesRunConfigHostedClient()
    {
        var hostedClient = new FakeHostedFileClient();
        var middleware = new ContentUploadMiddleware(providerRegistry: null, contentStore: null);
        var session = new SessionModel("test-session");
        var data = new DataContent(new byte[] { 1, 2, 3 }, "application/pdf") { Name = "report.pdf" };
        var message = new ChatMessage(ChatRole.User, [data]);
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig
        {
            UploadStrategy = UploadStrategy.Hosted,
            OverrideHostedFileClient = hostedClient
        };
        var context = CreateBeforeMessageTurnContext(session, message, capture.Coordinator, runConfig);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        var hosted = Assert.IsType<HostedFileContent>(Assert.Single(context.UserMessage!.Contents));
        Assert.Equal("file-1", hosted.FileId);
        Assert.Equal("application/pdf", hosted.MediaType);
        Assert.Equal("report.pdf", hosted.Name);
        Assert.Equal("report.pdf", hostedClient.Uploads.Single().FileName);
        Assert.Single(await capture.WaitForAsync<HostedFileUploadedEvent>());
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_Auto_UsesResolvedHostedClientBeforeLocalStore()
    {
        var contentStore = new InMemoryContentStore();
        var hostedClient = new FakeHostedFileClient();
        var middleware = new ContentUploadMiddleware(providerRegistry: null, contentStore);
        var session = new SessionModel("test-session");
        var message = new ChatMessage(ChatRole.User, [new DataContent(new byte[] { 1 }, "image/png")]);
        using var capture = new EventCapture();
        var context = CreateBeforeMessageTurnContext(
            session,
            message,
            capture.Coordinator,
            clientSet: new AgentClientSet { HostedFiles = hostedClient });

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.IsType<HostedFileContent>(Assert.Single(context.UserMessage!.Contents));
        Assert.Single(hostedClient.Uploads);
        Assert.Empty(await contentStore.QueryAsync(ContentStoreScopes.ForThread(session.Id, "main")));
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_Auto_HostedFailureFallsBackToLocal()
    {
        var contentStore = new InMemoryContentStore();
        var hostedClient = new FakeHostedFileClient { ThrowOnUpload = true };
        var middleware = new ContentUploadMiddleware(providerRegistry: null, contentStore);
        var session = new SessionModel("test-session");
        var bytes = new byte[] { 9, 8, 7 };
        var message = new ChatMessage(ChatRole.User, [new DataContent(bytes, "image/png")]);
        using var capture = new EventCapture();
        var context = CreateBeforeMessageTurnContext(
            session,
            message,
            capture.Coordinator,
            clientSet: new AgentClientSet { HostedFiles = hostedClient });

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        var uriContent = Assert.IsType<UriContent>(Assert.Single(context.UserMessage!.Contents));
        var stored = await contentStore.ReadBytesAsync(ContentStoreScopes.ForThread(session.Id, "main"), uriContent.Uri.Host);
        Assert.NotNull(stored);
        Assert.Equal(bytes, stored);
        Assert.Single(await capture.WaitForAsync<HostedFileUploadFailedEvent>());
        Assert.Single(await capture.WaitForAsync<ContentUploadedEvent>());
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_StrategyHosted_NoHostedClient_EmitsFailureKeepsOriginal()
    {
        var contentStore = new InMemoryContentStore();
        var middleware = new ContentUploadMiddleware(providerRegistry: null, contentStore);
        var session = new SessionModel("test-session");
        var data = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var message = new ChatMessage(ChatRole.User, [data]);
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig { UploadStrategy = UploadStrategy.Hosted };
        var context = CreateBeforeMessageTurnContext(session, message, capture.Coordinator, runConfig);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Same(data, Assert.Single(context.UserMessage!.Contents));
        Assert.Single(await capture.WaitForAsync<HostedFileUploadFailedEvent>());
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_MultipleDataContents_AllTransformed()
    {
        var contentStore = new InMemoryContentStore();
        var middleware = new ContentUploadMiddleware(providerRegistry: null, contentStore);
        var session = new SessionModel("test-session");
        var message = new ChatMessage(ChatRole.User, [
            new TextContent("Compare:"),
            new DataContent(new byte[] { 0x89, 0x50, 0x4E }, "image/png"),
            new TextContent("and"),
            new DataContent(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg")
        ]);
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig { UploadStrategy = UploadStrategy.Local };
        var context = CreateBeforeMessageTurnContext(session, message, capture.Coordinator, runConfig);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Equal(4, context.UserMessage!.Contents.Count);
        Assert.IsType<TextContent>(context.UserMessage.Contents[0]);
        Assert.IsType<UriContent>(context.UserMessage.Contents[1]);
        Assert.IsType<TextContent>(context.UserMessage.Contents[2]);
        Assert.IsType<UriContent>(context.UserMessage.Contents[3]);
        Assert.Equal(2, (await capture.WaitForAsync<ContentUploadedEvent>(2)).Count);
    }

    private static BeforeMessageTurnContext CreateBeforeMessageTurnContext(
        SessionModel session,
        ChatMessage userMessage,
        EventCoordinator coordinator,
        AgentRunConfig? runConfig = null,
        AgentClientSet? clientSet = null)
    {
        var context = CreateAgentContext(session, coordinator, clientSet);
        return context.AsBeforeMessageTurn(userMessage, new List<ChatMessage>(), runConfig ?? new AgentRunConfig());
    }

    private static AgentContext CreateAgentContext(
        SessionModel session,
        EventCoordinator coordinator,
        AgentClientSet? clientSet = null)
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            session.Id,
            "TestAgent");

        return new AgentContext(
            "TestAgent",
            session.Id,
            state,
            coordinator,
            session,
            new Thread(session.Id, "main"),
            CancellationToken.None,
            clientSet: clientSet);
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
    public async Task BeforeMessageTurnAsync_NoContentUris_DoesNothing()
    {
        var contentStore = new InMemoryContentStore();
        var middleware = new ContentReferenceResolverMiddleware(contentStore);
        var session = new SessionModel("test-session");
        var message = new ChatMessage(ChatRole.User, [
            new TextContent("Hello"),
            new UriContent(new Uri("https://example.com/image.png"), "image/png")
        ]);
        using var capture = new EventCapture();
        var context = CreateBeforeIterationContext(session, message, capture.Coordinator);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        Assert.Equal(2, context.Messages[0].Contents.Count);
        Assert.IsType<TextContent>(context.Messages[0].Contents[0]);
        Assert.IsType<UriContent>(context.Messages[0].Contents[1]);
        Assert.Empty(capture.Events);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_WithContentReference_ResolvesToDataContent()
    {
        var contentStore = new InMemoryContentStore();
        var middleware = new ContentReferenceResolverMiddleware(contentStore);
        var session = new SessionModel("test-session");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var contentInfo = await contentStore.WriteBytesAsync(
            ContentStoreScopes.ForThread(session.Id, "main"),
            imageBytes,
            "image/png",
            new ContentMetadata { Origin = ContentSource.User });
        var contentUri = new UriContent(ContentReferenceResolverMiddleware.CreateContentUri(contentInfo.Id), "image/png");
        var message = new ChatMessage(ChatRole.User, [new TextContent("Analyze:"), contentUri]);
        using var capture = new EventCapture();
        var context = CreateBeforeIterationContext(session, message, capture.Coordinator);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        var resolved = Assert.IsType<DataContent>(context.Messages[0].Contents[1]);
        Assert.Equal(imageBytes, resolved.Data.ToArray());
        Assert.Equal("image/png", resolved.MediaType);
        Assert.Equal(contentInfo.Id, (await capture.WaitForAsync<ContentReferenceResolvedEvent>()).Single().ContentUri.Host);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_ContentReferenceNotFound_KeepsOriginal()
    {
        var contentStore = new InMemoryContentStore();
        var middleware = new ContentReferenceResolverMiddleware(contentStore);
        var session = new SessionModel("test-session");
        var contentUri = new UriContent(new Uri("hpd-content://non-existent-id"), "image/png");
        var message = new ChatMessage(ChatRole.User, [contentUri]);
        using var capture = new EventCapture();
        var context = CreateBeforeIterationContext(session, message, capture.Coordinator);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        Assert.Same(contentUri, Assert.Single(context.Messages[0].Contents));
        Assert.Contains(
            "not found",
            (await capture.WaitForAsync<ContentReferenceResolutionFailedEvent>()).Single().Error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_NoContentStore_DoesNothing()
    {
        var middleware = new ContentReferenceResolverMiddleware(contentStore: null);
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
        var contentStore = new InMemoryContentStore();
        var uploadMiddleware = new ContentUploadMiddleware(providerRegistry: null, contentStore);
        var resolveMiddleware = new ContentReferenceResolverMiddleware(contentStore);
        var session = new SessionModel("test-session");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var originalMessage = new ChatMessage(ChatRole.User, [new DataContent(imageBytes, "image/png")]);
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig { UploadStrategy = UploadStrategy.Local };
        var context = CreateBeforeMessageTurnContext(session, originalMessage, capture.Coordinator, runConfig);

        await uploadMiddleware.BeforeMessageTurnAsync(context, CancellationToken.None);
        var uploadedMessage = context.UserMessage!;
        Assert.IsType<UriContent>(uploadedMessage.Contents[0]);

        var iterationContext = CreateBeforeIterationContext(session, uploadedMessage, capture.Coordinator, runConfig);
        await resolveMiddleware.BeforeIterationAsync(iterationContext, CancellationToken.None);

        var resolved = Assert.IsType<DataContent>(iterationContext.Messages[0].Contents[0]);
        Assert.Equal(imageBytes, resolved.Data.ToArray());
        Assert.Equal("image/png", resolved.MediaType);
    }

    [Fact]
    public async Task RoundTrip_ContentReference_DoesNotResolveFromSiblingThreadScope()
    {
        var contentStore = new InMemoryContentStore();
        var uploadMiddleware = new ContentUploadMiddleware(providerRegistry: null, contentStore);
        var resolveMiddleware = new ContentReferenceResolverMiddleware(contentStore);
        var session = new SessionModel("test-session");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var originalMessage = new ChatMessage(ChatRole.User, [new DataContent(imageBytes, "image/png")]);
        using var capture = new EventCapture();
        var runConfig = new AgentRunConfig { UploadStrategy = UploadStrategy.Local };
        var context = CreateBeforeMessageTurnContext(session, originalMessage, capture.Coordinator, runConfig, "main");

        await uploadMiddleware.BeforeMessageTurnAsync(context, CancellationToken.None);
        var uploadedMessage = context.UserMessage!;
        var contentReference = Assert.IsType<UriContent>(uploadedMessage.Contents[0]);

        var siblingContext = CreateBeforeIterationContext(session, uploadedMessage, capture.Coordinator, runConfig, "alternate");
        await resolveMiddleware.BeforeIterationAsync(siblingContext, CancellationToken.None);

        Assert.Same(contentReference, Assert.Single(siblingContext.Messages[0].Contents));
        Assert.Single(await capture.WaitForAsync<ContentReferenceResolutionFailedEvent>());
    }

    private static BeforeIterationContext CreateBeforeIterationContext(
        SessionModel session,
        ChatMessage userMessage,
        EventCoordinator coordinator,
        AgentRunConfig? runConfig = null,
        string threadId = "main")
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
            new Thread(session.Id, threadId),
            CancellationToken.None);

        return context.AsBeforeIteration(
            0,
            new List<ChatMessage> { userMessage },
            new ChatOptions(),
            runConfig ?? new AgentRunConfig());
    }

    private static BeforeMessageTurnContext CreateBeforeMessageTurnContext(
        SessionModel session,
        ChatMessage userMessage,
        EventCoordinator coordinator,
        AgentRunConfig? runConfig = null,
        string threadId = "main")
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
            new Thread(session.Id, threadId),
            CancellationToken.None);

        return context.AsBeforeMessageTurn(userMessage, new List<ChatMessage>(), runConfig ?? new AgentRunConfig());
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
