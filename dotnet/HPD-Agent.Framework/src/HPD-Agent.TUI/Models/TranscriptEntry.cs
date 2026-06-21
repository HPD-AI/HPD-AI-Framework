namespace HPD.Agent.TUI.Models;

public sealed record TranscriptEntry(
    string Id,
    string? EntryKey,
    TranscriptCell Cell,
    TranscriptEntryMetadata Metadata,
    int VerticalSpacing = 2)
{
    public static TranscriptEntry FromEvent(
        AgentEvent evt,
        TranscriptCell cell,
        string? id = null,
        string? entryKey = null,
        int verticalSpacing = 2)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(cell);

        return new TranscriptEntry(
            id ?? evt.EventId ?? Guid.NewGuid().ToString("N"),
            entryKey,
            cell,
            TranscriptEntryMetadata.FromEvent(evt),
            verticalSpacing);
    }
}
