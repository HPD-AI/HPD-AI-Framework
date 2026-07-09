using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Integration tests for synchronous message API with the full agent.
/// Tests that sync API works correctly in real agent execution scenarios.
/// </summary>
public class SyncMessageAPIIntegrationTests : AgentTestBase
{
    [Fact]
    public async Task Agent_Run_With_Sync_Message_API_Works()
    {
        // Arrange
        var client = new FakeChatClient();
        client.EnqueueTextResponse("Hello! I'm here to help.");

        var config = DefaultConfig();
        var agent = CreateAgent(config, client);
        var session = new global::HPD.Agent.Session("test-session-id");
        var thread = new global::HPD.Agent.Thread("test-session-id");

        // Act - use sync API to build conversation
        thread.AddMessage(new ChatMessage(ChatRole.User, "Hello"));

        // Verify sync API works before agent run
        Assert.Equal(1, thread.MessageCount);
        Assert.Single(thread.Messages);

        // Run agent (async for LLM)
        var events = new List<AgentEvent>();
        using (var subscription = agent.SubscribeAny(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        }))
        {
            await agent.RunAsync(new UserMessagesInputEvent { Messages = Array.Empty<ChatMessage>(),
                Session = session,
                Thread = thread
            }, TestCancellationToken);
        }

        // Assert - sync API works after agent run
        Assert.True(thread.MessageCount >= 2);  // User message + agent response
        Assert.NotEmpty(thread.Messages);
        Assert.Contains(thread.Messages, m => m.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task Agent_Run_With_Batch_AddMessages_Works()
    {
        // Arrange
        var client = new FakeChatClient();
        client.EnqueueTextResponse("I understand the context.");

        var config = DefaultConfig();
        var agent = CreateAgent(config, client);
        var session = new global::HPD.Agent.Session("test-session-id");
        var thread = new global::HPD.Agent.Thread("test-session-id");

        // Act - load conversation history using sync batch API
        var history = new[]
        {
            new ChatMessage(ChatRole.System, "You are a helpful assistant."),
            new ChatMessage(ChatRole.User, "Hello"),
            new ChatMessage(ChatRole.Assistant, "Hi! How can I help?"),
            new ChatMessage(ChatRole.User, "Tell me about yourself")
        };

        thread.AddMessages(history);

        // Verify batch add worked
        Assert.Equal(4, thread.MessageCount);

        // Run agent
        await agent.RunAsync(new UserMessagesInputEvent { Messages = Array.Empty<ChatMessage>(),
            Session = session,
            Thread = thread
        }, TestCancellationToken);

        // Assert - agent processed the history
        Assert.True(thread.MessageCount > 4);
    }

    [Fact]
    public void Sync_API_Does_Not_Interfere_With_Thread_Properties()
    {
        // Arrange
        var session = new global::HPD.Agent.Session("test-session-id");
        var thread = new global::HPD.Agent.Thread("test-session-id")
        {
            Description = "Test Conversation"
        };

        // Act - use sync API
        thread.AddMessage(new ChatMessage(ChatRole.User, "Test"));

        // Assert - other properties still work
        Assert.Equal("Test Conversation", thread.Description);
        Assert.NotEqual(default, session.CreatedAt);
        Assert.NotEqual(default, session.LastActivity);
    }

    [Fact]
    public async Task Sync_Messages_Property_Reflects_Agent_Responses()
    {
        // Arrange
        var client = new FakeChatClient();
        client.EnqueueTextResponse("Response 1");
        client.EnqueueTextResponse("Response 2");

        var config = DefaultConfig();
        var agent = CreateAgent(config, client);
        var session = new global::HPD.Agent.Session("test-session-id");
        var thread = new global::HPD.Agent.Thread("test-session-id");

        // Act - add message and run agent
        thread.AddMessage(new ChatMessage(ChatRole.User, "Question 1"));
        await agent.RunAsync(new UserMessagesInputEvent { Messages = Array.Empty<ChatMessage>(),
            Session = session,
            Thread = thread
        }, TestCancellationToken);

        var countAfterFirstRun = thread.MessageCount;

        // Add another message and run again
        thread.AddMessage(new ChatMessage(ChatRole.User, "Question 2"));
        await agent.RunAsync(new UserMessagesInputEvent { Messages = Array.Empty<ChatMessage>(),
            Session = session,
            Thread = thread
        }, TestCancellationToken);

        var countAfterSecondRun = thread.MessageCount;

        // Assert - message count increases after each run
        Assert.True(countAfterSecondRun > countAfterFirstRun);
        Assert.Contains(thread.Messages, m => m.Text == "Response 2");
    }

    [Fact]
    public async Task MessageCount_Increases_As_Agent_Responds()
    {
        // Arrange
        var client = new FakeChatClient();
        client.EnqueueTextResponse("Got it!");

        var config = DefaultConfig();
        var agent = CreateAgent(config, client);
        var session = new global::HPD.Agent.Session("test-session-id");
        var thread = new global::HPD.Agent.Thread("test-session-id");

        // Act
        var initialCount = thread.MessageCount;
        Assert.Equal(0, initialCount);

        thread.AddMessage(new ChatMessage(ChatRole.User, "Hello"));
        var countAfterUserMessage = thread.MessageCount;
        Assert.Equal(1, countAfterUserMessage);

        await agent.RunAsync(new UserMessagesInputEvent { Messages = Array.Empty<ChatMessage>(),
            Session = session,
            Thread = thread
        }, TestCancellationToken);

        var countAfterAgentRun = thread.MessageCount;

        // Assert
        Assert.True(countAfterAgentRun > countAfterUserMessage);
    }

    [Fact]
    public async Task Mixing_Sync_And_Async_APIs_Works_Correctly()
    {
        // Arrange
        var client = new FakeChatClient();
        client.EnqueueTextResponse("Response");

        var config = DefaultConfig();
        var agent = CreateAgent(config, client);
        var session = new global::HPD.Agent.Session("test-session-id");
        var thread = new global::HPD.Agent.Thread("test-session-id");

        // Act - add messages using sync API
        thread.AddMessage(new ChatMessage(ChatRole.User, "Message 1"));
        thread.AddMessage(new ChatMessage(ChatRole.User, "Message 2"));
        thread.AddMessages(new[]
        {
            new ChatMessage(ChatRole.User, "Message 3"),
            new ChatMessage(ChatRole.User, "Message 4")
        });

        // Assert - all messages present
        Assert.Equal(4, thread.MessageCount);
        Assert.Equal(4, thread.Messages.Count);

        // Run agent with mixed history
        await agent.RunAsync(new UserMessagesInputEvent { Messages = Array.Empty<ChatMessage>(),
            Session = session,
            Thread = thread
        }, TestCancellationToken);

        Assert.True(thread.MessageCount > 4);
    }
}
