namespace HPD.Agent.TUI.Models;

public enum TranscriptRunState
{
    Pending,
    Running,
    Cancelling,
    Completed,
    Failed,
    Cancelled,
    Backgrounded
}
