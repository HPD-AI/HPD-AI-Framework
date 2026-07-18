using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Unit tests for synchronous message API on Thread (V3).
/// Tests the public sync methods: Messages, MessageCount, AddMessage(), AddMessages().
/// </summary>
public class SyncMessageAPITests
{
    [Fact]
    public void Messages_Property_Returns_Messages_Directly()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var thread = session.CreateThread("test-agent");
        var msg = new ChatMessage(ChatRole.User, "Test");
        thread.AddMessage(msg);

        // Act
        var messages = thread.Messages;

        // Assert
        Assert.Single(messages);
        Assert.Equal("Test", messages[0].Text);
    }

    [Fact]
    public void Messages_Property_Returns_LiveView()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var thread = session.CreateThread("test-agent");
        thread.AddMessage(new ChatMessage(ChatRole.User, "Message 1"));

        // Act - capture reference
        var messages = thread.Messages;
        Assert.Single(messages);

        // Add another message
        thread.AddMessage(new ChatMessage(ChatRole.User, "Message 2"));

        // Assert - live view reflects changes
        Assert.Equal(2, messages.Count);
        Assert.Equal(2, thread.Messages.Count);
    }

    [Fact]
    public void MessageCount_Returns_Correct_Count()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var thread = session.CreateThread("test-agent");

        // Act & Assert
        Assert.Equal(0, thread.MessageCount);

        thread.AddMessage(new ChatMessage(ChatRole.User, "Test 1"));
        Assert.Equal(1, thread.MessageCount);

        thread.AddMessage(new ChatMessage(ChatRole.User, "Test 2"));
        Assert.Equal(2, thread.MessageCount);
    }

    [Fact]
    public void AddMessage_Adds_To_Store()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var thread = session.CreateThread("test-agent");
        var msg = new ChatMessage(ChatRole.User, "Test");

        // Act
        thread.AddMessage(msg);

        // Assert
        Assert.Single(thread.Messages);
        Assert.Equal("Test", thread.Messages[0].Text);
    }

    [Fact]
    public void AddMessages_Adds_Multiple_To_Store()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var thread = session.CreateThread("test-agent");
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "Test 1"),
            new ChatMessage(ChatRole.User, "Test 2"),
            new ChatMessage(ChatRole.User, "Test 3")
        };

        // Act
        thread.AddMessages(messages);

        // Assert
        Assert.Equal(3, thread.MessageCount);
        Assert.Equal("Test 1", thread.Messages[0].Text);
        Assert.Equal("Test 3", thread.Messages[2].Text);
    }

    [Fact]
    public void AddMessage_Updates_LastActivity()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var thread = session.CreateThread("test-agent");
        var initialActivity = thread.LastActivity;

        // Small delay to ensure timestamp difference
        System.Threading.Thread.Sleep(10);

        // Act
        thread.AddMessage(new ChatMessage(ChatRole.User, "Test"));

        // Assert
        Assert.True(thread.LastActivity > initialActivity);
    }

    [Fact]
    public void AddMessages_Updates_LastActivity()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var thread = session.CreateThread("test-agent");
        var initialActivity = thread.LastActivity;

        // Small delay to ensure timestamp difference
        System.Threading.Thread.Sleep(10);

        // Act
        thread.AddMessages(new[]
        {
            new ChatMessage(ChatRole.User, "Test 1"),
            new ChatMessage(ChatRole.User, "Test 2")
        });

        // Assert
        Assert.True(thread.LastActivity > initialActivity);
    }

    [Fact]
    public void AddMessages_With_Empty_Collection_Does_Not_Throw()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var thread = session.CreateThread("test-agent");

        // Act & Assert - should not throw
        thread.AddMessages(Array.Empty<ChatMessage>());

        Assert.Equal(0, thread.MessageCount);
    }

    [Fact]
    public void Multiple_Calls_To_Messages_Property_Return_Same_LiveView()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var thread = session.CreateThread("test-agent");

        // Act
        var view1 = thread.Messages;
        thread.AddMessage(new ChatMessage(ChatRole.User, "Test"));
        var view2 = thread.Messages;

        // Assert - same underlying data (live view)
        Assert.Single(view1);  // view1 sees the new message
        Assert.Single(view2);
        Assert.Equal(view1.Count, view2.Count);
    }
}
