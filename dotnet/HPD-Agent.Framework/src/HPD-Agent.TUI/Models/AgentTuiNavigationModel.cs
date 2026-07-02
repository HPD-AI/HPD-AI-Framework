namespace HPD.Agent.TUI.Models;

public sealed class AgentTuiNavigationModel
{
    private readonly List<AgentTuiNavigationFrame> _frames = [AgentTuiNavigationFrame.Transcript()];

    public string? ActivePageId
    {
        get
        {
            for (var i = _frames.Count - 1; i >= 0; i--)
            {
                var frame = _frames[i];
                if (frame.Kind == AgentTuiNavigationFrameKind.Page)
                {
                    return frame.PageId;
                }
            }

            return null;
        }
    }

    public bool IsTranscriptActive => string.IsNullOrWhiteSpace(ActivePageId);

    public bool CanGoBack => _frames.Count > 1;

    public AgentTuiNavigationFrame ActiveFrame => _frames[^1];

    public IReadOnlyList<AgentTuiNavigationFrame> Frames => _frames;

    public IReadOnlyList<AgentTuiNavigationFrame> BackStack => _frames.Count <= 1
        ? Array.Empty<AgentTuiNavigationFrame>()
        : _frames.GetRange(0, _frames.Count - 1);

    public void GoToTranscript()
    {
        PopToRoot(invokeClose: true);
    }

    public void GoToPage(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        if (ActiveFrame.Kind == AgentTuiNavigationFrameKind.Page &&
            string.Equals(ActiveFrame.PageId, pageId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _frames.Add(AgentTuiNavigationFrame.Page(pageId));
    }

    public string PushDialog(string title, Action close)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(close);

        var id = $"dialog-{Guid.NewGuid():N}";
        _frames.Add(AgentTuiNavigationFrame.Dialog(id, title, close));
        return id;
    }

    public bool RemoveDialog(string frameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameId);
        for (var i = _frames.Count - 1; i >= 1; i--)
        {
            if (_frames[i].Kind == AgentTuiNavigationFrameKind.Dialog &&
                StringComparer.Ordinal.Equals(_frames[i].FrameId, frameId))
            {
                _frames.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public bool Back()
    {
        if (_frames.Count <= 1)
        {
            return false;
        }

        var frame = _frames[^1];
        _frames.RemoveAt(_frames.Count - 1);
        frame.Close?.Invoke();
        return true;
    }

    public void Clear()
    {
        PopToRoot(invokeClose: true);
    }

    private void PopToRoot(bool invokeClose)
    {
        while (_frames.Count > 1)
        {
            var frame = _frames[^1];
            _frames.RemoveAt(_frames.Count - 1);
            if (invokeClose)
            {
                frame.Close?.Invoke();
            }
        }
    }
}

public sealed record AgentTuiNavigationFrame(
    AgentTuiNavigationFrameKind Kind,
    string FrameId,
    string? PageId,
    string Title,
    Action? Close)
{
    public static AgentTuiNavigationFrame Transcript()
        => new(AgentTuiNavigationFrameKind.Transcript, "transcript", null, "Transcript", null);

    public static AgentTuiNavigationFrame Page(string pageId)
        => new(AgentTuiNavigationFrameKind.Page, $"page:{pageId}", pageId, pageId, null);

    public static AgentTuiNavigationFrame Dialog(string frameId, string title, Action close)
        => new(AgentTuiNavigationFrameKind.Dialog, frameId, null, title, close);
}

public enum AgentTuiNavigationFrameKind
{
    Transcript,
    Page,
    Dialog
}
