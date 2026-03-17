using FluentAssertions;
using HPD.Agent.Maui;
using Microsoft.Maui;
using Moq;

namespace HPD.Agent.Maui.Tests.Unit;

/// <summary>
/// Unit tests for EventStreamManager.
/// Tests SendRawMessage protocol formatting.
/// </summary>
public class EventStreamManagerTests
{
    private readonly Mock<IHybridWebView> _mockWebView;
    private readonly EventStreamManager _manager;
    private readonly List<string> _sentMessages;

    public EventStreamManagerTests()
    {
        _mockWebView = new Mock<IHybridWebView>();
        _sentMessages = new List<string>();
        _mockWebView.Setup(x => x.SendRawMessage(It.IsAny<string>()))
            .Callback<string>(msg => _sentMessages.Add(msg));
        _manager = new EventStreamManager(_mockWebView.Object);
    }

    #region SendRawMessage Protocol

    [Fact]
    public void SendEvent_FormatsMessage_Correctly()
    {
        // Arrange
        var streamId = "stream-123";
        var evt = new TextDeltaEvent("Hello", "msg-1");

        // Act
        _manager.SendEvent(streamId, evt);

        // Assert
        _sentMessages.Should().ContainSingle();
        _sentMessages[0].Should().StartWith($"agent_event:{streamId}:");
    }

    [Fact]
    public void SendEvent_SerializesEvent_UsingAgentEventSerializer()
    {
        // Arrange
        var streamId = "stream-123";
        var evt = new TextDeltaEvent("Test content", "msg-2");

        // Act
        _manager.SendEvent(streamId, evt);

        // Assert
        _sentMessages[0].Should().Contain("Test content");
        _sentMessages[0].Should().Contain("agent_event");
    }

    [Fact]
    public void SendEvent_IncludesStreamId_InMessage()
    {
        // Arrange
        var streamId = "my-stream-id";
        var evt = new TextDeltaEvent("content", "msg-3");

        // Act
        _manager.SendEvent(streamId, evt);

        // Assert
        _sentMessages[0].Should().Contain("my-stream-id");
    }

    [Fact]
    public void SendComplete_FormatsMessage_Correctly()
    {
        // Arrange
        var streamId = "stream-456";

        // Act
        _manager.SendComplete(streamId);

        // Assert
        _sentMessages.Should().ContainSingle();
        _sentMessages[0].Should().Be($"agent_complete:{streamId}");
    }

    [Fact]
    public void SendError_FormatsMessage_WithErrorMessage()
    {
        // Arrange
        var streamId = "stream-789";
        var errorMessage = "Something went wrong";

        // Act
        _manager.SendError(streamId, errorMessage);

        // Assert
        _sentMessages.Should().ContainSingle();
        _sentMessages[0].Should().Be($"agent_error:{streamId}:{errorMessage}");
    }

    [Fact]
    public void SendError_HandlesColons_InErrorMessage()
    {
        // Arrange
        var streamId = "stream-123";
        var errorMessage = "Error: Failed: System failure";

        // Act
        _manager.SendError(streamId, errorMessage);

        // Assert
        _sentMessages[0].Should().Be($"agent_error:{streamId}:{errorMessage}");
        _sentMessages[0].Should().Contain("Error: Failed: System failure");
    }

    [Fact]
    public void SendEvent_CallsSendRawMessage_OnHybridWebView()
    {
        // Arrange
        var evt = new TextDeltaEvent("test", "msg-0");

        // Act
        _manager.SendEvent("stream-1", evt);

        // Assert
        _mockWebView.Verify(x => x.SendRawMessage(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void SendComplete_CallsSendRawMessage_OnHybridWebView()
    {
        // Act
        _manager.SendComplete("stream-1");

        // Assert
        _mockWebView.Verify(x => x.SendRawMessage(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void SendError_CallsSendRawMessage_OnHybridWebView()
    {
        // Act
        _manager.SendError("stream-1", "error");

        // Assert
        _mockWebView.Verify(x => x.SendRawMessage(It.IsAny<string>()), Times.Once);
    }

    #endregion
}
