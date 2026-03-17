using System.Text;
using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;
using AgentSession = HPD.Agent.Session;

namespace HPD.Agent.Tests.Content;

/// <summary>
/// Tests for FolderDiscoveryMiddleware — the context injection layer
/// that tells agents about available folders at the start of each turn.
/// </summary>
public class FolderDiscoveryMiddlewareTests
{
    private const string AgentName = "test-agent";

    private static InMemoryContentStore CreateStoreWithFolders()
    {
        var store = new InMemoryContentStore();
        store.CreateFolder("knowledge", new FolderOptions { Description = "Knowledge base", Permissions = ContentPermissions.Read });
        store.CreateFolder("memory", new FolderOptions { Description = "Agent memory", Permissions = ContentPermissions.Full });
        return store;
    }

    private static BeforeMessageTurnContext CreateContext(string? sessionId = null)
    {
        var state = AgentLoopState.InitialSafe([], "test-run", "test-conv", AgentName);
        var eventCoordinator = new HPD.Events.Core.EventCoordinator();

        AgentSession? session = sessionId != null
            ? new AgentSession { Id = sessionId, CreatedAt = DateTime.UtcNow, LastActivity = DateTime.UtcNow, Metadata = [] }
            : null;

        var agentContext = new AgentContext(
            AgentName, "test-conv", state, eventCoordinator, session, branch: null, CancellationToken.None);

        return agentContext.AsBeforeMessageTurn(
            new ChatMessage(ChatRole.User, "hello"),
            new List<ChatMessage>(),
            new AgentRunConfig());
    }

    // ═══════════════════════════════════════════════════════════════════
    // FDM-1: First turn injects the folder XML block
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FirstTurn_InjectsFolderStructure()
    {
        var store = CreateStoreWithFolders();
        var middleware = new FolderDiscoveryMiddleware(store, AgentName);
        var context = CreateContext();

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Contains(context.ConversationHistory,
            m => m.Text?.Contains("<content_store>") == true);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FDM-2: Second turn with identical folder structure — no re-injection
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SubsequentTurn_SameStructure_NoReinjection()
    {
        var store = CreateStoreWithFolders();
        var middleware = new FolderDiscoveryMiddleware(store, AgentName);

        var ctx1 = CreateContext();
        await middleware.BeforeMessageTurnAsync(ctx1, CancellationToken.None);
        var injectionsAfterFirst = ctx1.ConversationHistory.Count(m => m.Text?.Contains("<content_store>") == true);

        var ctx2 = CreateContext();
        await middleware.BeforeMessageTurnAsync(ctx2, CancellationToken.None);
        var injectionsAfterSecond = ctx2.ConversationHistory.Count(m => m.Text?.Contains("<content_store>") == true);

        // First turn injects once, second turn (same structure) should not inject again
        Assert.Equal(1, injectionsAfterFirst);
        Assert.Equal(0, injectionsAfterSecond);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FDM-3: Folder added between turns triggers re-injection
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SubsequentTurn_FolderAdded_Reinjection()
    {
        var store = CreateStoreWithFolders();
        var middleware = new FolderDiscoveryMiddleware(store, AgentName);

        var ctx1 = CreateContext();
        await middleware.BeforeMessageTurnAsync(ctx1, CancellationToken.None);

        // Add a new folder between turns
        store.CreateFolder("artifacts", new FolderOptions { Description = "Agent outputs", Permissions = ContentPermissions.ReadWrite });

        var ctx2 = CreateContext();
        await middleware.BeforeMessageTurnAsync(ctx2, CancellationToken.None);

        // Second turn should re-inject because structure changed
        Assert.Contains(ctx2.ConversationHistory,
            m => m.Text?.Contains("content_store") == true || m.Text?.Contains("artifacts") == true);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FDM-4: With session — /uploads and /artifacts appear in the XML
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WithSession_IncludesUploadsAndArtifacts()
    {
        var store = CreateStoreWithFolders();
        var middleware = new FolderDiscoveryMiddleware(store, AgentName);
        var context = CreateContext(sessionId: "sess-123");

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        var injectedMessage = context.ConversationHistory
            .FirstOrDefault(m => m.Text?.Contains("<content_store>") == true);

        Assert.NotNull(injectedMessage);
        Assert.Contains("/uploads", injectedMessage.Text);
        Assert.Contains("/artifacts", injectedMessage.Text);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FDM-5: Without session — /uploads and /artifacts are absent
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WithoutSession_NoSessionFolders()
    {
        var store = CreateStoreWithFolders();
        var middleware = new FolderDiscoveryMiddleware(store, AgentName);
        var context = CreateContext(sessionId: null); // no session

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        var injectedMessage = context.ConversationHistory
            .FirstOrDefault(m => m.Text?.Contains("<content_store>") == true);

        Assert.NotNull(injectedMessage);
        Assert.DoesNotContain("/uploads", injectedMessage.Text);
        Assert.DoesNotContain("/artifacts", injectedMessage.Text);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FDM-6: Injected message is placed after any existing system messages
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InjectedAfterSystemMessages()
    {
        var store = CreateStoreWithFolders();
        var middleware = new FolderDiscoveryMiddleware(store, AgentName);

        var state = AgentLoopState.InitialSafe([], "test-run", "test-conv", AgentName);
        var eventCoordinator = new HPD.Events.Core.EventCoordinator();
        var agentContext = new AgentContext(AgentName, "test-conv", state, eventCoordinator, null, null, CancellationToken.None);

        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "System instruction 1"),
            new(ChatRole.System, "System instruction 2"),
        };
        var context = agentContext.AsBeforeMessageTurn(
            new ChatMessage(ChatRole.User, "hello"), history, new AgentRunConfig());

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Find where the content_store message was inserted
        var insertIndex = context.ConversationHistory
            .FindIndex(m => m.Text?.Contains("<content_store>") == true);

        // It must come after both system messages (index >= 2)
        Assert.True(insertIndex >= 2, $"content_store was inserted at index {insertIndex}, expected >= 2");

        // System messages must still be at indexes 0 and 1
        Assert.Equal(ChatRole.System, context.ConversationHistory[0].Role);
        Assert.Equal(ChatRole.System, context.ConversationHistory[1].Role);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FDM-7: SetToolkit → SetSessionId is called each turn
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetToolkit_PropagatesSessionIdEachTurn()
    {
        var store = CreateStoreWithFolders();
        store.CreateFolder("artifacts", new FolderOptions
        {
            Description = "Agent outputs",
            Permissions = ContentPermissions.ReadWrite
        });

        var middleware = new FolderDiscoveryMiddleware(store, AgentName);
        var toolkit = new ContentStoreToolkit(store, AgentName);
        middleware.SetToolkit(toolkit);

        // First turn with session ID
        var ctx1 = CreateContext(sessionId: "session-001");
        await middleware.BeforeMessageTurnAsync(ctx1, CancellationToken.None);

        // Write via toolkit — should use session-001 scope
        await toolkit.WriteAsync("/artifacts/turn1.txt", "output from turn 1");

        var items = await store.QueryAsync("session-001", new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/artifacts" }
        });
        Assert.Single(items);

        // Second turn with a different session — toolkit session ID updates
        var ctx2 = CreateContext(sessionId: "session-002");
        await middleware.BeforeMessageTurnAsync(ctx2, CancellationToken.None);

        await toolkit.WriteAsync("/artifacts/turn2.txt", "output from turn 2");

        var items2 = await store.QueryAsync("session-002", new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/artifacts" }
        });
        Assert.Single(items2);
        Assert.Equal("turn2.txt", items2[0].Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FDM-8: Injected XML contains folder path, description, permissions, scope
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task XmlOutput_ContainsAllFolderDetails()
    {
        var store = new InMemoryContentStore();
        store.CreateFolder("knowledge", new FolderOptions
        {
            Description = "API documentation and guides",
            Permissions = ContentPermissions.Read
        });

        var middleware = new FolderDiscoveryMiddleware(store, AgentName);
        var context = CreateContext(sessionId: "sess-xyz");

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        var injectedMessage = context.ConversationHistory
            .FirstOrDefault(m => m.Text?.Contains("<content_store>") == true);

        Assert.NotNull(injectedMessage);
        var xml = injectedMessage.Text!;

        Assert.Contains("/knowledge", xml);
        Assert.Contains("API documentation and guides", xml);
        Assert.Contains("read", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agent-scoped", xml, StringComparison.OrdinalIgnoreCase);
        // Session folders should also be present
        Assert.Contains("/uploads", xml);
        Assert.Contains("session-scoped", xml, StringComparison.OrdinalIgnoreCase);
        // Tool listing
        Assert.Contains("content_list", xml);
        Assert.Contains("content_read", xml);
    }
}
