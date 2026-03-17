using HPD.Agent.Serialization;

namespace HPD.Agent.Maui;

/// <summary>
/// Manages sending agent events via SendRawMessage with protocol:
/// - agent_event:{streamId}:{json}
/// - agent_complete:{streamId}
/// - agent_error:{streamId}:{errorMessage}
/// </summary>
public sealed class EventStreamManager
{
    private readonly IHybridWebView _hybridWebView;

    public EventStreamManager(IHybridWebView hybridWebView)
    {
        _hybridWebView = hybridWebView;
    }

    public void SendEvent(string streamId, AgentEvent evt)
    {
        var json = AgentEventSerializer.ToJson(evt);
        _hybridWebView.SendRawMessage($"agent_event:{streamId}:{json}");
    }

    public void SendComplete(string streamId)
    {
        _hybridWebView.SendRawMessage($"agent_complete:{streamId}");
    }

    public void SendError(string streamId, string errorMessage)
    {
        _hybridWebView.SendRawMessage($"agent_error:{streamId}:{errorMessage}");
    }
}
