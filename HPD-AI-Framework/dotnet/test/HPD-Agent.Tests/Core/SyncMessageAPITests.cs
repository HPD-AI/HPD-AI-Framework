using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Unit tests for synchronous message API on Branch (V3).
/// Tests the public sync methods: Messages, MessageCount, AddMessage(), AddMessages().
/// </summary>
public class SyncMessageAPITests
{
    [Fact]
    public void Messages_Property_Returns_Messages_Directly()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var branch = session.CreateBranch();
        var msg = new ChatMessage(ChatRole.User, "Test");
        branch.AddMessage(msg);

        // Act
        var messages = branch.Messages;

        // Assert
        Assert.Single(messages);
        Assert.Equal("Test", messages[0].Text);
    }

    [Fact]
    public void Messages_Property_Returns_LiveView()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var branch = session.CreateBranch();
        branch.AddMessage(new ChatMessage(ChatRole.User, "Message 1"));

        // Act - capture reference
        var messages = branch.Messages;
        Assert.Single(messages);

        // Add another message
        branch.AddMessage(new ChatMessage(ChatRole.User, "Message 2"));

        // Assert - live view reflects changes
        Assert.Equal(2, messages.Count);
        Assert.Equal(2, branch.Messages.Count);
    }

    [Fact]
    public void MessageCount_Returns_Correct_Count()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var branch = session.CreateBranch();

        // Act & Assert
        Assert.Equal(0, branch.MessageCount);

        branch.AddMessage(new ChatMessage(ChatRole.User, "Test 1"));
        Assert.Equal(1, branch.MessageCount);

        branch.AddMessage(new ChatMessage(ChatRole.User, "Test 2"));
        Assert.Equal(2, branch.MessageCount);
    }

    [Fact]
    public void AddMessage_Adds_To_Store()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var branch = session.CreateBranch();
        var msg = new ChatMessage(ChatRole.User, "Test");

        // Act
        branch.AddMessage(msg);

        // Assert
        Assert.Single(branch.Messages);
        Assert.Equal("Test", branch.Messages[0].Text);
    }

    [Fact]
    public void AddMessages_Adds_Multiple_To_Store()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var branch = session.CreateBranch();
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "Test 1"),
            new ChatMessage(ChatRole.User, "Test 2"),
            new ChatMessage(ChatRole.User, "Test 3")
        };

        // Act
        branch.AddMessages(messages);

        // Assert
        Assert.Equal(3, branch.MessageCount);
        Assert.Equal("Test 1", branch.Messages[0].Text);
        Assert.Equal("Test 3", branch.Messages[2].Text);
    }

    [Fact]
    public void AddMessage_Updates_LastActivity()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var branch = session.CreateBranch();
        var initialActivity = branch.LastActivity;

        // Small delay to ensure timestamp difference
        Thread.Sleep(10);

        // Act
        branch.AddMessage(new ChatMessage(ChatRole.User, "Test"));

        // Assert
        Assert.True(branch.LastActivity > initialActivity);
    }

    [Fact]
    public void AddMessages_Updates_LastActivity()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var branch = session.CreateBranch();
        var initialActivity = branch.LastActivity;

        // Small delay to ensure timestamp difference
        Thread.Sleep(10);

        // Act
        branch.AddMessages(new[]
        {
            new ChatMessage(ChatRole.User, "Test 1"),
            new ChatMessage(ChatRole.User, "Test 2")
        });

        // Assert
        Assert.True(branch.LastActivity > initialActivity);
    }

    [Fact]
    public void AddMessages_With_Empty_Collection_Does_Not_Throw()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var branch = session.CreateBranch();

        // Act & Assert - should not throw
        branch.AddMessages(Array.Empty<ChatMessage>());

        Assert.Equal(0, branch.MessageCount);
    }

    [Fact]
    public void Multiple_Calls_To_Messages_Property_Return_Same_LiveView()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        var branch = session.CreateBranch();

        // Act
        var view1 = branch.Messages;
        branch.AddMessage(new ChatMessage(ChatRole.User, "Test"));
        var view2 = branch.Messages;

        // Assert - same underlying data (live view)
        Assert.Single(view1);  // view1 sees the new message
        Assert.Single(view2);
        Assert.Equal(view1.Count, view2.Count);
    }
}
