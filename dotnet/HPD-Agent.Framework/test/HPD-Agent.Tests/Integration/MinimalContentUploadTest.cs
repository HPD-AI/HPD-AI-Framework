using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Integration;

public class MinimalContentUploadTest
{
    [Fact]
    public async Task Middleware_ReceivesSession_EmitsEvent()
    {
        // Create a minimal session with store
        var tempDir = Path.Combine(Path.GetTempPath(), $"minimal-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var store = new FileSessionStore(tempDir);
            var contentStore = new LocalFileContentStore(Path.Combine(tempDir, "content"));
            var session = await store.LoadSessionAsync("minimal-session") ?? new HPD.Agent.Session("minimal-session");
            session.Store = store;
            var thread = await store.ProjectThreadAsync("minimal-session", "main", ThreadProjectionPurpose.ThreadHistory) ?? session.CreateThread("test-agent", "main");
            thread.Session = session;

            // Verify session has store
            Assert.NotNull(session.Store);
            Assert.Same(store, session.Store);

            // Create agent with content store so ContentUploadMiddleware can upload content
            var chatClient = new FakeChatClient();
            chatClient.EnqueueTextResponse("Response");

            var agent = await new AgentBuilder()
                .WithName("MinimalAgent")
                .WithChatClient(chatClient)
                .WithContentStore(contentStore)
                .BuildAsync();

            // Create message with DataContent
            var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            var userMessage = new ChatMessage(ChatRole.User,
            [
                new DataContent(imageBytes, "image/png")
            ]);

            // Run agent and collect events
            var events = new List<AgentEvent>();
            using (var subscription = agent.SubscribeAny(evt =>
            {
                events.Add(evt);
                if (evt is ContentUploadedEvent upload)
                {
                    Console.WriteLine($"ContentUploadedEvent: {upload.ContentId}");
                }

                return ValueTask.CompletedTask;
            }))
            {
                await agent.RunAsync(new UserMessagesInputEvent { Messages = [userMessage],
                    Session = session,
                    Thread = thread
                });
            }

            // Verify
            var uploadEvent = events.OfType<ContentUploadedEvent>().FirstOrDefault();
            if (uploadEvent == null)
            {
                Console.WriteLine($"NO ContentUploadedEvent!");
                Console.WriteLine($"Total events: {events.Count}");
                Console.WriteLine($"Session.Store: {session.Store != null}");
                Console.WriteLine($"ContentStore: {contentStore != null}");
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
