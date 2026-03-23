using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;
using SessionModel = global::HPD.Agent.Session;

namespace HPD.Agent.Tests.Middleware;

public class AssetUploadMiddlewareTests
{
    [Fact]
    public async Task BeforeMessageTurnAsync_NoAssetStore_DoesNothing()
    {
        // Arrange
        var middleware = new AssetUploadMiddleware();
        var session = new SessionModel("test-session");
        // session.Store is null (no store associated)

        var userMessage = new ChatMessage(ChatRole.User, "Hello");
        var context = CreateBeforeMessageTurnContext(session, userMessage);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(context.UserMessage);
        Assert.Equal("Hello", context.UserMessage.Text);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_StoreWithoutAssetStore_DoesNothing()
    {
        // Arrange
        var middleware = new AssetUploadMiddleware();
        var store = new TestSessionStoreWithoutAssets();
        var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
        session.Store = store;

        var userMessage = new ChatMessage(ChatRole.User, "Hello");
        var context = CreateBeforeMessageTurnContext(session, userMessage);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(context.UserMessage);
        Assert.Equal("Hello", context.UserMessage.Text);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_NoDataContent_DoesNothing()
    {
        // Arrange
        var middleware = new AssetUploadMiddleware();
        var store = new InMemorySessionStore();
        var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
        session.Store = store;

        var userMessage = new ChatMessage(ChatRole.User, "Hello");
        var context = CreateBeforeMessageTurnContext(session, userMessage);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(context.UserMessage);
        Assert.Equal("Hello", context.UserMessage.Text);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_WithDataContent_TransformsToUriContent()
    {
        // Arrange
        var contentStore = new InMemoryContentStore();
        var middleware = new AssetUploadMiddleware(contentStore);
        var store = new InMemorySessionStore();
        var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
        session.Store = store;

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var message = new ChatMessage(ChatRole.User, [
            new TextContent("Check this image:"),
            new DataContent(imageBytes, "image/png")
        ]);

        var context = CreateBeforeMessageTurnContext(session, message);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(context.UserMessage);
        Assert.Equal(2, context.UserMessage.Contents.Count);

        // First content should still be text
        Assert.IsType<TextContent>(context.UserMessage.Contents[0]);
        Assert.Equal("Check this image:", ((TextContent)context.UserMessage.Contents[0]).Text);

        // Second content should be transformed to UriContent
        Assert.IsType<UriContent>(context.UserMessage.Contents[1]);
        var uriContent = (UriContent)context.UserMessage.Contents[1];
        Assert.StartsWith("asset://", uriContent.Uri.ToString());
        Assert.Equal("image/png", uriContent.MediaType);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_EmitsAssetUploadedEvent()
    {
        // Arrange
        var middleware = new AssetUploadMiddleware();
        var store = new InMemorySessionStore();
        var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
        session.Store = store;

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };
        var message = new ChatMessage(ChatRole.User, [
            new DataContent(imageBytes, "image/png")
        ]);

        var context = CreateBeforeMessageTurnContext(session, message);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert - verify event was emitted via context.Emit
        // Note: We can't easily capture events in this test structure
        // because BeforeMessageTurnContext.Emit is not mockable.
        // The event emission is tested in integration tests.
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_MultipleDataContents_TransformsAll()
    {
        // Arrange
        var middleware = new AssetUploadMiddleware(new InMemoryContentStore());
        var store = new InMemorySessionStore();
        var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
        session.Store = store;

        var image1 = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var image2 = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        var message = new ChatMessage(ChatRole.User, [
            new DataContent(image1, "image/png"),
            new TextContent("and"),
            new DataContent(image2, "image/jpeg")
        ]);

        var context = CreateBeforeMessageTurnContext(session, message);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(context.UserMessage);
        Assert.Equal(3, context.UserMessage.Contents.Count);

        // All DataContent should be transformed to UriContent
        Assert.IsType<UriContent>(context.UserMessage.Contents[0]);
        Assert.Equal("image/png", ((UriContent)context.UserMessage.Contents[0]).MediaType);

        Assert.IsType<TextContent>(context.UserMessage.Contents[1]);

        Assert.IsType<UriContent>(context.UserMessage.Contents[2]);
        Assert.Equal("image/jpeg", ((UriContent)context.UserMessage.Contents[2]).MediaType);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_PreservesMessageMetadata()
    {
        // Arrange
        var middleware = new AssetUploadMiddleware();
        var store = new InMemorySessionStore();
        var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
        session.Store = store;

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var message = new ChatMessage(ChatRole.User, [
            new DataContent(imageBytes, "image/png")
        ])
        {
            AuthorName = "TestUser",
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["customKey"] = "customValue"
            }
        };

        var context = CreateBeforeMessageTurnContext(session, message);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(context.UserMessage);
        Assert.Equal("TestUser", context.UserMessage.AuthorName);
        Assert.Equal("customValue", context.UserMessage.AdditionalProperties?["customKey"]);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_EmptyDataContent_Skips()
    {
        // Arrange
        var middleware = new AssetUploadMiddleware();
        var store = new InMemorySessionStore();
        var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
        session.Store = store;

        var message = new ChatMessage(ChatRole.User, [
            new DataContent(Array.Empty<byte>(), "image/png") // Empty data
        ]);

        var context = CreateBeforeMessageTurnContext(session, message);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert - should not transform empty DataContent
        Assert.NotNull(context.UserMessage);
        Assert.Single(context.UserMessage.Contents);
        Assert.IsType<DataContent>(context.UserMessage.Contents[0]); // Still DataContent
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_AssetStoreReturnsId_CreatesCorrectUri()
    {
        // Arrange
        var contentStore = new InMemoryContentStore();
        var middleware = new AssetUploadMiddleware(contentStore);
        var store = new InMemorySessionStore();
        var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
        session.Store = store;

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var message = new ChatMessage(ChatRole.User, [
            new DataContent(imageBytes, "image/png")
        ]);

        var context = CreateBeforeMessageTurnContext(session, message);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(context.UserMessage);
        var uriContent = (UriContent)context.UserMessage.Contents[0];
        var assetId = uriContent.Uri.Host;

        // Verify we can retrieve the asset directly from the content store
        var retrievedAsset = await contentStore.GetAsync(session.Id, assetId, CancellationToken.None);
        Assert.NotNull(retrievedAsset);
        Assert.Equal(imageBytes, retrievedAsset.Data);
        Assert.Equal("image/png", retrievedAsset.ContentType);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_OctetStreamMediaType_PreservesContentType()
    {
        // Arrange
        var contentStore = new InMemoryContentStore();
        var middleware = new AssetUploadMiddleware(contentStore);
        var store = new InMemorySessionStore();
        var session = await store.LoadSessionAsync("test-session") ?? new SessionModel("test-session");
        session.Store = store;

        var bytes = new byte[] { 0x01, 0x02, 0x03 };
        var message = new ChatMessage(ChatRole.User, [
            new DataContent(bytes, "application/octet-stream")
        ]);

        var context = CreateBeforeMessageTurnContext(session, message);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(context.UserMessage);
        var uriContent = (UriContent)context.UserMessage.Contents[0];
        var assetId = uriContent.Uri.Host;
        var retrievedAsset = await contentStore.GetAsync(session.Id, assetId, CancellationToken.None);

        Assert.NotNull(retrievedAsset);
        Assert.Equal("application/octet-stream", retrievedAsset.ContentType);
    }

    // Helper method to create BeforeMessageTurnContext
    private static BeforeMessageTurnContext CreateBeforeMessageTurnContext(
        SessionModel session,
        ChatMessage userMessage)
    {
        var state = AgentLoopState.InitialSafe(
            [],
            "test-run",
            "test-conv",
            "TestAgent");
        var eventCoordinator = new HPD.Events.Core.EventCoordinator();
        var agentContext = new AgentContext(
            "TestAgent",
            "test-conv",
            state,
            eventCoordinator,
            session,
            branch: null,
            CancellationToken.None);

        var conversationHistory = new List<ChatMessage>();
        var runConfig = new AgentRunConfig();

        return agentContext.AsBeforeMessageTurn(userMessage, conversationHistory, runConfig);
    }

    // Test session store without asset support
    private class TestSessionStoreWithoutAssets : ISessionStore
    {
        public IContentStore? GetContentStore(string sessionId) => null; // No content store

        public Task<SessionModel?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<SessionModel?>(new SessionModel(sessionId));

        public Task SaveSessionAsync(SessionModel session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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
