using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;
using SessionModel = global::HPD.Agent.Session;

namespace HPD.Agent.Tests.Integration;

/// <summary>
/// End-to-end integration tests demonstrating the complete asset storage workflow.
/// Tests the full pipeline: DataContent → Upload → UriContent → Storage → Retrieval.
/// </summary>
public class AssetStorageIntegrationTests
{
    [Fact]
    public async Task EndToEnd_ImageUpload_WithJsonSessionStore()
    {
        // Arrange: Create a temporary directory for test storage
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create store with asset support
            var store = new JsonSessionStore(tempDir);
            Assert.NotNull(store.GetContentStore("test-session"));

            // Create fake chat client and enqueue a response
            var chatClient = new FakeChatClient();
            chatClient.EnqueueTextResponse("I see the image.");

            // Create agent with content store so AssetUploadMiddleware can upload assets
            var agent = await new AgentBuilder()
                .WithName("TestAgent")
                .WithChatClient(chatClient)
                .WithContentStore(store.GetContentStore("test-session")!)
                .BuildAsync();

            // Load session and branch (sets session.Store automatically)
            var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
            session.Store = store;
            var branch = await store.LoadBranchAsync("test-session", "main") ?? session.CreateBranch("main");
            branch.Session = session;
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
            await foreach (var evt in agent.RunAsync([userMessage], session, branch))
            {
                events.Add(evt);
            }

            // Assert: Verify AssetUploadedEvent was emitted
            var uploadEvent = events.OfType<AssetUploadedEvent>().FirstOrDefault();
            Assert.NotNull(uploadEvent);
            Assert.Equal("image/png", uploadEvent.MediaType);
            Assert.Equal(imageBytes.Length, uploadEvent.SizeBytes);

            // Assert: Verify message was transformed (DataContent → UriContent)
            var messages = branch.Messages;
            var transformedMessage = messages.First(m => m.Role == ChatRole.User);

            // Should have 2 contents: TextContent + UriContent
            Assert.Equal(2, transformedMessage.Contents.Count);

            var textContent = transformedMessage.Contents[0] as TextContent;
            Assert.NotNull(textContent);
            Assert.Equal("What's in this image?", textContent.Text);

            var uriContent = transformedMessage.Contents[1] as UriContent;
            Assert.NotNull(uriContent);
            Assert.StartsWith("asset://", uriContent.Uri.ToString());
            Assert.Equal("image/png", uriContent.MediaType);

            // Assert: Verify asset was stored and is retrievable
            var assetId = uriContent.Uri.Host;
            var assetStore = store.GetContentStore(session.Id)!;
            var retrievedAsset = await assetStore.GetAsync(session.Id, assetId, CancellationToken.None);
            Assert.NotNull(retrievedAsset);
            Assert.Equal(imageBytes, retrievedAsset.Data);
            Assert.Equal("image/png", retrievedAsset.ContentType);
            Assert.Equal(assetId, retrievedAsset.Id);

            // Assert: Verify asset file exists on disk (exclude .meta companion files)
            // LocalFileContentStore stores at {basePath}/{scope}/{contentId}.ext
            // basePath = {tempDir}/{sessionId}/content, scope = sessionId
            var assetFiles = Directory.GetFiles(Path.Combine(tempDir, session.Id, "content", session.Id), $"{assetId}.*")
                .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".nameindex"))
                .ToArray();
            Assert.Single(assetFiles);
            Assert.EndsWith(".png", assetFiles[0]);

            // Save session and branch (V3: messages live in Branch, not Session)
            await session.SaveAsync();
            await store.SaveBranchAsync(session.Id, branch);

            // Assert: Verify branch was saved with URI reference (not bytes)
            var branchFile = Path.Combine(tempDir, session.Id, "branches", branch.Id, "branch.json");
            Assert.True(File.Exists(branchFile));
            var branchJson = await File.ReadAllTextAsync(branchFile);
            Assert.Contains($"asset://{assetId}", branchJson);
            Assert.DoesNotContain("\"Data\":", branchJson); // Binary data NOT in branch JSON
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EndToEnd_MultipleAssets_DifferentTypes()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var store = new JsonSessionStore(tempDir);

            // Create fake chat client and enqueue a response
            var chatClient = new FakeChatClient();
            chatClient.EnqueueTextResponse("I see multiple files.");

            var agent = await new AgentBuilder()
                .WithName("TestAgent")
                .WithChatClient(chatClient)
                .WithContentStore(store.GetContentStore("multi-asset-session")!)
                .BuildAsync();

            var session = await store.LoadSessionAsync("multi-asset-session") ?? new SessionModel("multi-asset-session");
            session.Store = store;
            var branch = await store.LoadBranchAsync("multi-asset-session", "main") ?? session.CreateBranch("main");
            branch.Session = session;

            // Create different asset types
            var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG
            var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF

            // Add message with multiple assets
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
            await foreach (var evt in agent.RunAsync([message], session, branch))
            {
                events.Add(evt);
            }

            // Assert: 3 upload events
            var uploadEvents = events.OfType<AssetUploadedEvent>().ToList();
            Assert.Equal(3, uploadEvents.Count);
            Assert.Contains(uploadEvents, e => e.MediaType == "image/png");
            Assert.Contains(uploadEvents, e => e.MediaType == "image/jpeg");
            Assert.Contains(uploadEvents, e => e.MediaType == "application/pdf");

            // Assert: Message transformed correctly
            var transformedMessage = branch.Messages.First(m => m.Role == ChatRole.User);
            Assert.Equal(6, transformedMessage.Contents.Count); // 3 text + 3 URI

            var uriContents = transformedMessage.Contents.OfType<UriContent>().ToList();
            Assert.Equal(3, uriContents.Count);

            // Assert: All assets retrievable
            var assetStore = store.GetContentStore(session.Id)!;
            foreach (var uriContent in uriContents)
            {
                var assetId = uriContent.Uri.Host;
                var asset = await assetStore.GetAsync(session.Id, assetId, CancellationToken.None);
                Assert.NotNull(asset);
            }

            // Assert: Correct file extensions on disk
            // LocalFileContentStore stores at {basePath}/{scope}/{contentId}.ext
            // basePath = {tempDir}/{sessionId}/content, scope = sessionId
            var assetDir = Path.Combine(tempDir, session.Id, "content", session.Id);
            Assert.True(Directory.GetFiles(assetDir, "*.png").Length >= 1);
            Assert.True(Directory.GetFiles(assetDir, "*.jpg").Length >= 1);
            Assert.True(Directory.GetFiles(assetDir, "*.pdf").Length >= 1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EndToEnd_NoAssetStore_SkipsUpload()
    {
        // Arrange: Use InMemorySessionStore WITHOUT AssetStore
        var store = new TestSessionStoreWithoutAssets();

        // Create fake chat client and enqueue a response
        var chatClient = new FakeChatClient();
        chatClient.EnqueueTextResponse("Response.");

        var agent = await new AgentBuilder()
            .WithName("TestAgent")
            .WithChatClient(chatClient)
            .BuildAsync();

        var session = await store.LoadSessionAsync("no-asset-session") ?? new SessionModel("no-asset-session");
        session.Store = store;
        var branch = await store.LoadBranchAsync("no-asset-session", "main") ?? session.CreateBranch("main");
        branch.Session = session;

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent("Image:"),
            new DataContent(imageBytes, "image/png")
        ]);

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAsync([message], session, branch))
        {
            events.Add(evt);
        }

        // Assert: NO upload events (middleware skipped)
        var uploadEvents = events.OfType<AssetUploadedEvent>().ToList();
        Assert.Empty(uploadEvents);

        // Assert: DataContent still present (not transformed)
        var userMessage = branch.Messages.First(m => m.Role == ChatRole.User);
        var dataContent = userMessage.Contents.OfType<DataContent>().FirstOrDefault();
        Assert.NotNull(dataContent);
        Assert.Equal(imageBytes, dataContent.Data.ToArray());
    }

    [Fact]
    public async Task EndToEnd_SessionRoundtrip_PreservesAssetReferences()
    {
        // Tests: Save session → Load session → Asset still retrievable
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var store = new JsonSessionStore(tempDir);

            // Create fake chat client and enqueue a response
            var chatClient = new FakeChatClient();
            chatClient.EnqueueTextResponse("Image processed.");

            var agent = await new AgentBuilder()
                .WithName("TestAgent")
                .WithChatClient(chatClient)
                .WithContentStore(store.GetContentStore("roundtrip-session")!)
                .BuildAsync();

            // First run: Upload asset
            var session1 = await store.LoadSessionAsync("roundtrip-session") ?? new SessionModel("roundtrip-session");
            session1.Store = store;
            var branch1 = await store.LoadBranchAsync("roundtrip-session", "main") ?? session1.CreateBranch("main");
            branch1.Session = session1;
            var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03 };

            var userMessage = new ChatMessage(ChatRole.User,
            [
                new TextContent("Process image"),
                new DataContent(imageBytes, "image/png")
            ]);

            await foreach (var _ in agent.RunAsync([userMessage], session1, branch1)) { }
            await session1.SaveAsync();
            await store.SaveBranchAsync(session1.Id, branch1);

            // Get asset ID from first branch
            var msg1 = branch1.Messages.First(m => m.Role == ChatRole.User);
            var uri1 = msg1.Contents.OfType<UriContent>().First();
            var assetId = uri1.Uri.Host;

            // Second run: Load session and branch, verify asset still accessible
            var session2 = await store.LoadSessionAsync("roundtrip-session") ?? new SessionModel("roundtrip-session");
            session2.Store = store;
            var branch2 = await store.LoadBranchAsync("roundtrip-session", "main") ?? session2.CreateBranch("main");
            branch2.Session = session2;
            Assert.NotNull(session2);

            var msg2 = branch2.Messages.First(m => m.Role == ChatRole.User);
            var uri2 = msg2.Contents.OfType<UriContent>().FirstOrDefault();
            Assert.NotNull(uri2);
            Assert.Equal(assetId, uri2.Uri.Host);

            // Assert: Asset still retrievable after roundtrip
            var assetStore = store.GetContentStore(session2.Id)!;
            var retrievedAsset = await assetStore.GetAsync(session2.Id, assetId, CancellationToken.None);
            Assert.NotNull(retrievedAsset);
            Assert.Equal(imageBytes, retrievedAsset.Data);
            Assert.Equal("image/png", retrievedAsset.ContentType);
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
            var branch = await store.LoadBranchAsync("convenience-session", "main") ?? session.CreateBranch("main");
            branch.Session = session;

            branch.AddMessage(new ChatMessage(ChatRole.User, "Test message"));

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

    // Test helper: Session store without asset support
    private class TestSessionStoreWithoutAssets : ISessionStore
    {
        private readonly Dictionary<string, SessionModel> _sessions = new();

        public IContentStore? GetContentStore(string sessionId) => null; // No content store

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

        public Task<Branch?> LoadBranchAsync(string sessionId, string branchId, CancellationToken cancellationToken = default)
            => Task.FromResult<Branch?>(null);

        public Task SaveBranchAsync(string sessionId, Branch branch, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<string>> ListBranchIdsAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());

        public Task DeleteBranchAsync(string sessionId, string branchId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<UncommittedTurn?> LoadUncommittedTurnAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<UncommittedTurn?>(null);

        public Task SaveUncommittedTurnAsync(UncommittedTurn turn, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteUncommittedTurnAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteInactiveSessionsAsync(TimeSpan inactivityThreshold, bool dryRun = false, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
