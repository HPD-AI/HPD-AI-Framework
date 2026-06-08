namespace HPD.Agent.TUI.Models;

public sealed class AgentTuiNavigationModel
{
    private readonly List<AgentTuiNavigationFrame> _backStack = [];

    public string? ActivePageId { get; private set; }

    public bool IsTranscriptActive => string.IsNullOrWhiteSpace(ActivePageId);

    public bool CanGoBack => _backStack.Count > 0 || !IsTranscriptActive;

    public IReadOnlyList<AgentTuiNavigationFrame> BackStack => _backStack;

    public void GoToTranscript()
    {
        _backStack.Clear();
        ActivePageId = null;
    }

    public void GoToPage(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        if (string.Equals(ActivePageId, pageId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PushCurrentFrame();
        ActivePageId = pageId;
    }

    public bool Back()
    {
        if (_backStack.Count == 0)
        {
            if (IsTranscriptActive)
            {
                return false;
            }

            ActivePageId = null;
            return true;
        }

        var frame = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        ActivePageId = frame.Kind == AgentTuiNavigationFrameKind.Page
            ? frame.PageId
            : null;
        return true;
    }

    public void Clear()
    {
        _backStack.Clear();
        ActivePageId = null;
    }

    private void PushCurrentFrame()
    {
        _backStack.Add(IsTranscriptActive
            ? AgentTuiNavigationFrame.Transcript()
            : AgentTuiNavigationFrame.Page(ActivePageId!));
    }
}

public sealed record AgentTuiNavigationFrame(
    AgentTuiNavigationFrameKind Kind,
    string? PageId,
    string Title)
{
    public static AgentTuiNavigationFrame Transcript()
        => new(AgentTuiNavigationFrameKind.Transcript, null, "Transcript");

    public static AgentTuiNavigationFrame Page(string pageId)
        => new(AgentTuiNavigationFrameKind.Page, pageId, pageId);
}

public enum AgentTuiNavigationFrameKind
{
    Transcript,
    Page
}
