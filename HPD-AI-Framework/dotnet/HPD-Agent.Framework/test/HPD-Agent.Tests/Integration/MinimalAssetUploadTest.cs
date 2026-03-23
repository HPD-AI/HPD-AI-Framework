using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Integration;

public class MinimalAssetUploadTest
{
    [Fact]
    public async Task Middleware_ReceivesSession_EmitsEvent()
    {
        // Create a minimal session with store
        var tempDir = Path.Combine(Path.GetTempPath(), $"minimal-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var store = new JsonSessionStore(tempDir);
            var session = await store.LoadSessionAsync("minimal-session") ?? new HPD.Agent.Session("minimal-session");
            session.Store = store;

            // Verify session has store
            Assert.NotNull(session.Store);
            Assert.Same(store, session.Store);

            // Create agent with content store so AssetUploadMiddleware can upload assets
            var chatClient = new FakeChatClient();
            chatClient.EnqueueTextResponse("Response");

            var agent = await new AgentBuilder()
                .WithName("MinimalAgent")
                .WithChatClient(chatClient)
                .WithContentStore(store.GetContentStore("minimal-session")!)
                .BuildAsync();

            // Create message with DataContent
            var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            var userMessage = new ChatMessage(ChatRole.User,
            [
                new DataContent(imageBytes, "image/png")
            ]);

            // Run agent and collect events
            var events = new List<AgentEvent>();
            await foreach (var evt in agent.RunAsync([userMessage], session))
            {
                events.Add(evt);
                if (evt is AssetUploadedEvent upload)
                {
                    Console.WriteLine($"✓ AssetUploadedEvent: {upload.AssetId}");
                }
            }

            // Verify
            var uploadEvent = events.OfType<AssetUploadedEvent>().FirstOrDefault();
            if (uploadEvent == null)
            {
                Console.WriteLine($" NO AssetUploadedEvent!");
                Console.WriteLine($"Total events: {events.Count}");
                Console.WriteLine($"Session.Store: {session.Store != null}");
                Console.WriteLine($"ContentStore: {session.Store?.GetContentStore(session.Id) != null}");
                foreach (var evt in events.Take(10))
                {
                    Console.WriteLine($"  - {evt.GetType().Name}");
                }
            }

            Assert.NotNull(uploadEvent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
