namespace HPD.Agent.TUI.Models;

public sealed record TranscriptEntry(
    string Id,
    string? EntryKey,
    TranscriptCell Cell,
    TranscriptEntryMetadata Metadata,
    int VerticalSpacing = 2,
    TranscriptEntryState State = TranscriptEntryState.Final,
    TranscriptCommitPolicy CommitPolicy = TranscriptCommitPolicy.Immediate)
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

    public TranscriptEntry AsLive(TranscriptCommitPolicy commitPolicy = TranscriptCommitPolicy.WhenFinal)
        => this with
        {
            State = TranscriptEntryState.Live,
            CommitPolicy = commitPolicy
        };

    public TranscriptEntry AsFinal(TranscriptCommitPolicy commitPolicy = TranscriptCommitPolicy.Immediate)
        => this with
        {
            State = TranscriptEntryState.Final,
            CommitPolicy = commitPolicy
        };
}

public enum TranscriptEntryState
{
    Live,
    Final
}

public enum TranscriptCommitPolicy
{
    Never,
    Immediate,
    WhenFinal
}
