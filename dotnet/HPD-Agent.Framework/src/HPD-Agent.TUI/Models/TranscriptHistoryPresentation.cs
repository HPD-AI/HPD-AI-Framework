namespace HPD.Agent.TUI.Models;

/// <summary>Controls how durable transcript history is projected into a terminal frame.</summary>
public enum TranscriptHistoryPresentation
{
    /// <summary>Render one physical viewport and navigate older rows inside the TUI.</summary>
    Viewport,

    /// <summary>Render all rows so the terminal owns transcript scrollback.</summary>
    TerminalScrollback
}
