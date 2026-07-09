using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;
using SessionModel = global::HPD.Agent.Session;

#pragma warning disable MEAI001

namespace HPD.Agent.Tests.Integration;

/// <summary>
/// End-to-end integration tests demonstrating the complete content storage workflow.
/// Tests the full pipeline: DataContent → Upload → UriContent → Storage → Retrieval.
/// </summary>
public class ContentStorageIntegrationTests
{
    [Fact]
    public async Task EndToEnd_ImageUpload_WithJsonSessionStore()
    {
        // Arrange: Create a temporary directory for test storage
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var store = new JsonSessionStore(tempDir);
            var contentStore = new LocalFileContentStore(Path.Combine(tempDir, "content"));

            // Create fake chat client and enqueue a response
            var chatClient = new FakeChatClient();
            chatClient.EnqueueTextResponse("I see the image.");

            // Create agent with content store so ContentUploadMiddleware can upload content
            var agent = await new AgentBuilder()
                .WithName("TestAgent")
                .WithChatClient(chatClient)
                .WithContentStore(contentStore)
                .BuildAsync();

            // Load session and thread (sets session.Store automatically)
            var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
            session.Store = store;
            var thread = await store.LoadThreadAsync("test-session", "main") ?? session.CreateThread("main");
            thread.Session = session;
            Assert.NotNull(session.Store);
            Assert.Same(store, session.Store);

            // Create a test image (PNG header + some data)
            var imageBytes = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG header
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08  // Sample data
            };

            // Add multimodal message using MEAI types
            var userMessage = new ChatMessage(ChatRole.User,
            [
                new TextContent("What's in this image?"),
                new DataContent(imageBytes, "image/png")
            ]);

            // Track events emitted during execution
            var events = new List<AgentEvent>();
            using (var subscription = agent.SubscribeAny(evt =>
            {
                events.Add(evt);
                return ValueTask.CompletedTask;
            }))
            {
                await agent.RunAsync(new UserMessagesInputEvent { Messages = [userMessage],
                    Session = session,
                    Thread = thread
                });
            }

            // Assert: Verify ContentUploadedEvent was emitted
            var uploadEvent = events.OfType<ContentUploadedEvent>().FirstOrDefault();
            Assert.NotNull(uploadEvent);
            Assert.Equal("image/png", uploadEvent.MediaType);
            Assert.Equal(imageBytes.Length, uploadEvent.SizeBytes);

            // Assert: Verify message was transformed (DataContent → UriContent)
            var messages = thread.Messages;
            var transformedMessage = messages.First(m => m.Role == ChatRole.User);

            // Should have 2 contents: TextContent + UriContent
            Assert.Equal(2, transformedMessage.Contents.Count);

            var textContent = transformedMessage.Contents[0] as TextContent;
            Assert.NotNull(textContent);
            Assert.Equal("What's in this image?", textContent.Text);

            var uriContent = transformedMessage.Contents[1] as UriContent;
            Assert.NotNull(uriContent);
            Assert.StartsWith("hpd-content://", uriContent.Uri.ToString());
            Assert.Equal("image/png", uriContent.MediaType);

            // Assert: Verify content was stored and is retrievable
            var contentId = uriContent.Uri.Host;
            var contentScope = ContentStoreScopes.ForThread(session.Id, thread.Id);
            var retrievedContent = await contentStore.ReadBytesAsync(contentScope, contentId, CancellationToken.None);
            var retrievedInfo = await contentStore.StatAsync(contentScope, contentId, CancellationToken.None);
            Assert.NotNull(retrievedContent);
            Assert.NotNull(retrievedInfo);
            Assert.Equal(imageBytes, retrievedContent);
            Assert.Equal("image/png", retrievedInfo.ContentType);
            Assert.Equal(contentId, retrievedInfo.Id);

            // Assert: Verify content file exists on disk (exclude .meta companion files)
            // LocalFileContentStore stores at {basePath}/{scope}/{contentId}.ext
            // basePath = {tempDir}/content, scope = thread-scoped content scope
            var contentFiles = Directory.GetFiles(Path.Combine(tempDir, "content", contentScope), $"{contentId}.*")
                .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".nameindex"))
                .ToArray();
            Assert.Single(contentFiles);
            Assert.EndsWith(".png", contentFiles[0]);

            // Save session and thread ( messages live in Thread, not Session)
            await session.SaveAsync();
            await store.SaveInitialThreadAsync(session.Id, thread);

            // Assert: Verify thread stream was saved with URI reference (not bytes)
            var threadEventsFile = Path.Combine(tempDir, session.Id, "threads", thread.Id, "thread.events.jsonl");
            Assert.True(File.Exists(threadEventsFile));
            var threadEventsJson = await File.ReadAllTextAsync(threadEventsFile);
            Assert.Contains($"hpd-content://{contentId}", threadEventsJson);
            Assert.DoesNotContain("\"Data\":", threadEventsJson); // Binary data NOT in thread event stream
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EndToEnd_MultipleContentItems_DifferentTypes()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var store = new JsonSessionStore(tempDir);
            var contentStore = new LocalFileContentStore(Path.Combine(tempDir, "content"));

            // Create fake chat client and enqueue a response
            var chatClient = new FakeChatClient();
            chatClient.EnqueueTextResponse("I see multiple files.");

            var agent = await new AgentBuilder()
                .WithName("TestAgent")
                .WithChatClient(chatClient)
                .WithContentStore(contentStore)
                .BuildAsync();

            var session = await store.LoadSessionAsync("multi-content-session") ?? new SessionModel("multi-content-session");
            session.Store = store;
            var thread = await store.LoadThreadAsync("multi-content-session", "main") ?? session.CreateThread("main");
            thread.Session = session;

            // Create different content types
            var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG
            var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF

            // Add message with multiple content items
            var message = new ChatMessage(ChatRole.User,
            [
                new TextContent("Analyze these files:"),
                new DataContent(pngBytes, "image/png"),
                new TextContent("and"),
                new DataContent(jpegBytes, "image/jpeg"),
                new TextContent("plus"),
                new DataContent(pdfBytes, "application/pdf")
            ]);

            // Act
            var events = new List<AgentEvent>();
            using (var subscription = agent.SubscribeAny(evt =>
            {
                events.Add(evt);
                return ValueTask.CompletedTask;
            }))
            {
                await agent.RunAsync(new UserMessagesInputEvent { Messages = [message],
                    Session = session,
                    Thread = thread
                });
            }

            // Assert: 3 upload events
            var uploadEvents = events.OfType<ContentUploadedEvent>().ToList();
            Assert.Equal(3, uploadEvents.Count);
            Assert.Contains(uploadEvents, e => e.MediaType == "image/png");
            Assert.Contains(uploadEvents, e => e.MediaType == "image/jpeg");
            Assert.Contains(uploadEvents, e => e.MediaType == "application/pdf");

            // Assert: Message transformed correctly
            var transformedMessage = thread.Messages.First(m => m.Role == ChatRole.User);
            Assert.Equal(6, transformedMessage.Contents.Count); // 3 text + 3 URI

            var uriContents = transformedMessage.Contents.OfType<UriContent>().ToList();
            Assert.Equal(3, uriContents.Count);

            // Assert: All content items retrievable
            var contentScope = ContentStoreScopes.ForThread(session.Id, thread.Id);
            foreach (var uriContent in uriContents)
            {
                var contentId = uriContent.Uri.Host;
                var content = await contentStore.ReadBytesAsync(contentScope, contentId, CancellationToken.None);
                Assert.NotNull(content);
            }

            // Assert: Correct file extensions on disk
            // LocalFileContentStore stores at {basePath}/{scope}/{contentId}.ext
            // basePath = {tempDir}/content, scope = thread-scoped content scope
            var contentDir = Path.Combine(tempDir, "content", contentScope);
            Assert.True(Directory.GetFiles(contentDir, "*.png").Length >= 1);
            Assert.True(Directory.GetFiles(contentDir, "*.jpg").Length >= 1);
            Assert.True(Directory.GetFiles(contentDir, "*.pdf").Length >= 1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EndToEnd_DefaultContentStore_UploadsContent()
    {
        // Arrange: AgentBuilder provides a default in-memory content store
        var store = new TestSessionStore();

        // Create fake chat client and enqueue a response
        var chatClient = new FakeChatClient();
        chatClient.EnqueueTextResponse("Response.");

        var agent = await new AgentBuilder()
            .WithName("TestAgent")
            .WithChatClient(chatClient)
            .BuildAsync();

        var session = await store.LoadSessionAsync("no-content-store-session") ?? new SessionModel("no-content-store-session");
        session.Store = store;
        var thread = await store.LoadThreadAsync("no-content-store-session", "main") ?? session.CreateThread("main");
        thread.Session = session;

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent("Image:"),
            new DataContent(imageBytes, "image/png")
        ]);

        // Act
        var events = new List<AgentEvent>();
        using (var subscription = agent.SubscribeAny(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        }))
        {
            await agent.RunAsync(new UserMessagesInputEvent { Messages = [message],
                Session = session,
                Thread = thread
            });
        }

        // Assert: default in-memory content store uploads the content.
        var uploadEvents = events.OfType<ContentUploadedEvent>().ToList();
        Assert.Single(uploadEvents);

        // Assert: DataContent was replaced with a content reference for history.
        var userMessage = thread.Messages.First(m => m.Role == ChatRole.User);
        var uriContent = userMessage.Contents.OfType<UriContent>().FirstOrDefault();
        Assert.NotNull(uriContent);
        Assert.Equal("hpd-content", uriContent.Uri.Scheme);
    }

    [Fact]
    public async Task EndToEnd_SessionRoundtrip_PreservesContentReferences()
    {
        // Tests: Save session -> Load session -> content still retrievable
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var store = new JsonSessionStore(tempDir);
            var contentStore = new LocalFileContentStore(Path.Combine(tempDir, "content"));

            // Create fake chat client and enqueue a response
            var chatClient = new FakeChatClient();
            chatClient.EnqueueTextResponse("Image processed.");

            var agent = await new AgentBuilder()
                .WithName("TestAgent")
                .WithChatClient(chatClient)
                .WithContentStore(contentStore)
                .BuildAsync();

            // First run: Upload content
            var session1 = await store.LoadSessionAsync("roundtrip-session") ?? new SessionModel("roundtrip-session");
            session1.Store = store;
            var thread1 = await store.LoadThreadAsync("roundtrip-session", "main") ?? session1.CreateThread("main");
            thread1.Session = session1;
            var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03 };

            var userMessage = new ChatMessage(ChatRole.User,
            [
                new TextContent("Process image"),
                new DataContent(imageBytes, "image/png")
            ]);

            await agent.RunAsync(new UserMessagesInputEvent { Messages = [userMessage],
                Session = session1,
                Thread = thread1
            });
            await session1.SaveAsync();
            await store.SaveInitialThreadAsync(session1.Id, thread1);

            // Get content ID from first thread
            var msg1 = thread1.Messages.First(m => m.Role == ChatRole.User);
            var uri1 = msg1.Contents.OfType<UriContent>().First();
            var contentId = uri1.Uri.Host;

            // Second run: Load session and thread, verify content still accessible
            var session2 = await store.LoadSessionAsync("roundtrip-session") ?? new SessionModel("roundtrip-session");
            session2.Store = store;
            var thread2 = await store.LoadThreadAsync("roundtrip-session", "main") ?? session2.CreateThread("main");
            thread2.Session = session2;
            Assert.NotNull(session2);

            var msg2 = thread2.Messages.First(m => m.Role == ChatRole.User);
            var uri2 = msg2.Contents.OfType<UriContent>().FirstOrDefault();
            Assert.NotNull(uri2);
            Assert.Equal(contentId, uri2.Uri.Host);

            // Assert: Content still retrievable after roundtrip
            var contentScope = ContentStoreScopes.ForThread(session2.Id, thread2.Id);
            var retrievedContent = await contentStore.ReadBytesAsync(contentScope, contentId, CancellationToken.None);
            var retrievedInfo = await contentStore.StatAsync(contentScope, contentId, CancellationToken.None);
            Assert.NotNull(retrievedContent);
            Assert.NotNull(retrievedInfo);
            Assert.Equal(imageBytes, retrievedContent);
            Assert.Equal("image/png", retrievedInfo.ContentType);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EndToEnd_ConvenienceMethod_SaveAsync()
    {
        // Tests: session.SaveAsync() convenience method
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var store = new JsonSessionStore(tempDir);
            var session = await store.LoadSessionAsync("convenience-session") ?? new SessionModel("convenience-session");
            session.Store = store;
            var thread = await store.LoadThreadAsync("convenience-session", "main") ?? session.CreateThread("main");
            thread.Session = session;

            thread.AddMessage(new ChatMessage(ChatRole.User, "Test message"));

            // Act: Use convenience method
            await session.SaveAsync();

            // Assert: Session saved
            var sessionFile = Path.Combine(tempDir, session.Id, "session.json");
            Assert.True(File.Exists(sessionFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EndToEnd_HostedFileProvider_UsesResolvedClientSetAndClientMiddleware()
    {
        var chatClient = new FakeChatClient();
        chatClient.EnqueueTextResponse("I can see the hosted file.");

        var hostedClient = new FakeHostedFileClient();
        var provider = new HostedFileTestProvider(chatClient, hostedClient);
        var registry = new SingleProviderRegistry(provider);
        var contentStore = new InMemoryContentStore();
        RecordingHostedFileClient? wrappedHostedClient = null;

        var config = new AgentConfig
        {
            Name = "HostedFileAgent"
        };
        config.SetClientConfig(ProviderClientFamily.Chat, new ClientProviderConfig
        {
            ProviderKey = provider.ProviderKey,
            ModelName = "fake-chat"
        });
        config.SetClientConfig(ProviderClientFamily.HostedFiles, new ClientProviderConfig
        {
            ProviderKey = provider.ProviderKey
        });

        var agent = await new AgentBuilder(config, registry)
            .WithContentStore(contentStore)
            .UseHostedFileClientMiddleware((client, _) =>
            {
                wrappedHostedClient = new RecordingHostedFileClient(client);
                return wrappedHostedClient;
            })
            .BuildAsync();

        var session = new SessionModel("hosted-session");
        var thread = session.CreateThread("main");
        var data = new DataContent(new byte[] { 1, 2, 3, 4 }, "application/pdf") { Name = "report.pdf" };
        var message = new ChatMessage(ChatRole.User, [new TextContent("Read this"), data]);
        var events = new List<AgentEvent>();

        using (agent.SubscribeAny(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        }))
        {
            await agent.RunAsync(new UserMessagesInputEvent { Messages = [message],
                Session = session,
                Thread = thread
            });
        }

        Assert.NotNull(wrappedHostedClient);
        Assert.Equal(1, wrappedHostedClient.UploadCount);
        Assert.Single(hostedClient.Uploads);
        Assert.Empty(await contentStore.QueryAsync(ContentStoreScopes.ForThread(session.Id, thread.Id)));

        var uploadEvent = events.OfType<HostedFileUploadedEvent>().Single();
        Assert.Equal("file-1", uploadEvent.FileId);
        Assert.DoesNotContain(events, evt => evt is ContentUploadedEvent);

        var persistedUserMessage = thread.Messages.First(m => m.Role == ChatRole.User);
        var hostedContent = persistedUserMessage.Contents.OfType<HostedFileContent>().Single();
        Assert.Equal("file-1", hostedContent.FileId);
        Assert.Equal("application/pdf", hostedContent.MediaType);
        Assert.Equal("report.pdf", hostedContent.Name);

        var requestUserMessage = chatClient.CapturedRequests.Single().First(m => m.Role == ChatRole.User);
        Assert.Single(requestUserMessage.Contents.OfType<HostedFileContent>());
    }

    [Fact]
    public async Task EndToEnd_SessionWithoutStore_SaveAsyncThrows()
    {
        // Tests: session.SaveAsync() throws when Store is null
        var session = new SessionModel("no-store-session");
        Assert.Null(session.Store);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.SaveAsync());

        Assert.Contains("no associated store", ex.Message);
        Assert.Contains("LoadSessionAsync", ex.Message);
    }

    private class TestSessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionModel> _sessions = new();

        public Task<SessionModel?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return Task.FromResult(session);
        }

        public Task SaveSessionAsync(SessionModel session, CancellationToken cancellationToken = default)
        {
            _sessions[session.Id] = session;
            return Task.CompletedTask;
        }

        public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_sessions.Keys.ToList());

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            _sessions.Remove(sessionId);
            return Task.CompletedTask;
        }

        public Task<Thread?> LoadThreadAsync(string sessionId, string threadId, CancellationToken cancellationToken = default)
            => Task.FromResult<Thread?>(null);

        public Task<ThreadEventDocument?> LoadThreadDocumentAsync(string sessionId, string threadId, CancellationToken cancellationToken = default)
            => Task.FromResult<ThreadEventDocument?>(null);

        public Task AppendThreadEventAsync(
            string sessionId,
            string threadId,
            AgentEvent evt,
            long? expectedSequenceNumber = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<string>> ListThreadIdsAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());

        public Task DeleteThreadAsync(string sessionId, string threadId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteInactiveSessionsAsync(TimeSpan inactivityThreshold, bool dryRun = false, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class SingleProviderRegistry(IProvider provider) : IProviderRegistry
    {
        public void Register(IProvider provider)
        {
        }

        public IProvider? GetProvider(string providerKey)
            => string.Equals(providerKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase) ? provider : null;

        public TProvider? GetProvider<TProvider>(string providerKey)
            where TProvider : class, IProvider
            => GetProvider(providerKey) as TProvider;

        public bool IsRegistered(string providerKey)
            => string.Equals(providerKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> GetRegisteredProviders() => [provider.ProviderKey];

        public void Clear()
        {
        }
    }

    private sealed class HostedFileTestProvider(
        IChatClient chatClient,
        IHostedFileClient hostedFileClient) : IChatClientProvider, IHostedFileClientProvider
    {
        public string ProviderKey => "hosted-test";

        public string DisplayName => "Hosted Test Provider";

        public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
            => chatClient;

        public IHostedFileClient CreateHostedFileClient(ClientProviderConfig config, IServiceProvider? services = null)
            => hostedFileClient;

        public HPD.Agent.ErrorHandling.IProviderErrorHandler CreateErrorHandler()
            => new TestErrorHandler();

        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat
                },
                [ProviderClientFamily.HostedFiles] = new()
                {
                    Family = ProviderClientFamily.HostedFiles
                }
            }
        };

        public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class RecordingHostedFileClient(IHostedFileClient inner) : IHostedFileClient
    {
        public int UploadCount { get; private set; }

        public Task<HostedFileContent> UploadAsync(
            Stream content,
            string? mediaType = null,
            string? fileName = null,
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            UploadCount++;
            return inner.UploadAsync(content, mediaType, fileName, options, cancellationToken);
        }

        public Task<HostedFileDownloadStream> DownloadAsync(
            string fileId,
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
            => inner.DownloadAsync(fileId, options, cancellationToken);

        public Task<HostedFileContent?> GetFileInfoAsync(
            string fileId,
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
            => inner.GetFileInfoAsync(fileId, options, cancellationToken);

        public IAsyncEnumerable<HostedFileContent> ListFilesAsync(
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
            => inner.ListFilesAsync(options, cancellationToken);

        public Task<bool> DeleteAsync(
            string fileId,
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
            => inner.DeleteAsync(fileId, options, cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : inner.GetService(serviceType, serviceKey);

        public void Dispose()
        {
        }
    }

    private sealed class FakeHostedFileClient : IHostedFileClient
    {
        public List<(string? MediaType, string? FileName, HostedFileClientOptions? Options, byte[] Data)> Uploads { get; } = new();

        public Task<HostedFileContent> UploadAsync(
            Stream content,
            string? mediaType = null,
            string? fileName = null,
            HostedFileClientOptions? options = null,
            CancellationToken cancellationToken = default)
        {
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
