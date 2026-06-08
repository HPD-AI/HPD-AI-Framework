namespace HPD.Agent.TUI.Models;

public sealed record TranscriptEntry(
    string Id,
    string? EntryKey,
    TranscriptCell Cell,
    TranscriptEntryMetadata Metadata)
{
    public static TranscriptEntry FromEvent(
        AgentEvent evt,
        TranscriptCell cell,
        string? id = null,
        string? entryKey = null)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(cell);

        return new TranscriptEntry(
            id ?? evt.EventId ?? Guid.NewGuid().ToString("N"),
            entryKey,
            cell,
            TranscriptEntryMetadata.FromEvent(evt));
    }
}
